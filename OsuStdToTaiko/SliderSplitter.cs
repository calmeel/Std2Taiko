using System.Globalization;
using System.Collections.Generic;

namespace OsuStdToTaiko
{
    internal static class SliderSplitter
    {
        private const float VELOCITY_MULTIPLIER = 1.4f;      // TaikoBeatmapConverter.VELOCITY_MULTIPLIER
        private const float BASE_SCORING_DISTANCE = 100f;    // TaikoBeatmapConverter.BASE_SCORING_DISTANCE // 100 は float として扱う

        // Slider を HitObject に変換する
        internal static string SplitSlidersToTaikoHits_LazerLike(
            string osuText,
            List<(double time, double beatLen, int uninherited)>? timingOverride,
            int inputFormatVersion,
            HashSet<int>? noSplitStartTimes = null)
        {
            // テキスト処理
            var lines = osuText.Replace("\r\n", "\n").Split('\n').ToList();

            int idxDiff = lines.FindIndex(l => l.Trim().Equals("[Difficulty]", StringComparison.OrdinalIgnoreCase));
            int idxTiming = lines.FindIndex(l => l.Trim().Equals("[TimingPoints]", StringComparison.OrdinalIgnoreCase));
            int idxHit = lines.FindIndex(l => l.Trim().Equals("[HitObjects]", StringComparison.OrdinalIgnoreCase));

            if (idxHit < 0) return osuText;

            // Split 内ではヘッダを見ない
            int inputVersion = inputFormatVersion > 0 ? inputFormatVersion : 14;

            // Difficulty: SliderMultiplier, SliderTickRate（より頑丈に取得）
            // sliderMultiplier と sliderTickRate の osu! デフォルト値を設定（情報が抜けている際のみこちらを採用する）
            double sliderMultiplier = 1.4;
            double sliderTickRate = 1.0;

            // Difficulty 行を解析するローカル関数
            void TryParseDifficultyLine(string line)
            {
                var t = line.Trim();

                if (t.StartsWith("SliderMultiplier:", StringComparison.OrdinalIgnoreCase))
                {
                    if (double.TryParse(t.Split(':', 2)[1].Trim(),
                        NumberStyles.Float, CultureInfo.InvariantCulture, out var sm))
                        sliderMultiplier = sm;
                }
                else if (t.StartsWith("SliderTickRate:", StringComparison.OrdinalIgnoreCase))
                {
                    if (double.TryParse(t.Split(':', 2)[1].Trim(),
                        NumberStyles.Float, CultureInfo.InvariantCulture, out var tr))
                        sliderTickRate = tr;
                }
            }

            // [Difficulty] セクション内だけを走査
            if (idxDiff >= 0)
            {
                for (int i = idxDiff + 1; i < lines.Count; i++)
                {
                    var t = lines[i].Trim();
                    if (t.StartsWith("[") && t.EndsWith("]")) break;
                    if (t.Length == 0 || t.StartsWith("//")) continue;
                    TryParseDifficultyLine(t);
                }
            }
            else  // [Difficulty] が見つからない場合（保険）
            {
                foreach (var ln in lines)
                    TryParseDifficultyLine(ln);
            }

            // Timing points: output側がSVを落とす環境があるので、override があればそれを使う
            List<(double time, double beatLen, int uninherited)> timing;
            if (timingOverride != null && timingOverride.Count > 0)
            {
                timing = timingOverride;
            }
            else
            {
                // fallback: 出力osuから読む
                if (idxTiming < 0)
                {
                    timing = new List<(double time, double beatLen, int uninherited)>();
                }
                else
                {
                    timing = new List<(double time, double beatLen, int uninherited)>();

                    for (int i = idxTiming + 1; i < lines.Count; i++)
                    {
                        var t = lines[i].Trim();
                        if (t.StartsWith("[") && t.EndsWith("]")) break;
                        if (t.Length == 0 || t.StartsWith("//")) continue;

                        var p = t.Split(',');
                        if (p.Length < 2) continue;

                        if (!double.TryParse(p[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var tm)) continue;
                        if (!double.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var bl)) continue;

                        // 列7 uninherited（1=赤線, 0=緑線）
                        // 無い/壊れている場合は赤線扱い（安全側）
                        int uninherited = 1;
                        if (p.Length >= 7)
                        {
                            if (!int.TryParse(p[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out uninherited))
                                uninherited = 1;
                        }

                        timing.Add((tm, bl, uninherited));
                    }

                    // time順。同時刻なら赤線(uninherited=1)を先に
                    timing.Sort((a, b) =>
                    {
                        int c = a.time.CompareTo(b.time);
                        if (c != 0) return c;

                        if (a.uninherited == 1 && b.uninherited == 0) return -1;
                        if (a.uninherited == 0 && b.uninherited == 1) return 1;
                        return 0;
                    });
                }
            }


            // 時刻 t 時点で有効な uninherited timing point の beatLength を返す関数
            // [TimingPoints] BPM（赤線）の扱い方針
            // ■ 内部計算用（Split判定・配置に使用）
            // ・uninherited == 1（赤線）のみを BPM 線として扱う
            // ・beatLength は必ず有限・正の値に正規化する
            // ・beatLength = NaN            → 無視（直前の有効赤線にフォールバック）
            // ・beatLength < 0              → 絶対値で救済
            // ・beatLength = ±Infinity      → 60000 にクランプ（BPM 1）
            // ・beatLength が非正規化数     → 1 にクランプ（BPM 60000）
            // ・beatLength がアンダーフロー → 1 にクランプ
            // ・beatLength = 0              → 1 にクランプ（※この 1 という値に特に意味はないが、プログラムが破綻しないために下限を設けている）
            // ・0 < beatLength < 1          → 1 にクランプ（※上限も60000でクランプしていたが、unshakable の aspire map が引っかかったので撤廃）
            double BaseBeatLengthAt(double t)
            {
                double cur = 500; // TimingPoint が無い場合は 120 bpm 相当

                foreach (var tp in timing)
                {
                    if (tp.time > t) break;

                    // ★赤線判定は beatLen の符号ではなく uninherited で行う
                    if (tp.uninherited == 1)
                    {
                        double bl = tp.beatLen;

                        // --- 内部用 BPM（赤線）正規化 ---
                        // NaN は無視（直前の有効赤線にフォールバック）
                        if (double.IsNaN(bl))
                            continue;

                        // Infinity は最も遅い BPM に救済
                        if (double.IsInfinity(bl))
                            bl = 60000.0;

                        // 負の値は符号を無視して絶対値で救済
                        bl = Math.Abs(bl);

                        // 非正規化数 / アンダーフロー / 0 は最も速い BPM に救済
                        if (Math.Abs(bl) < 1e-9)
                            bl = 1.0;

                        // 下限ガードのみ
                        if (bl < 1.0)
                            bl = 1.0;

                        cur = bl;
                    }
                }

                return cur;
            }

            // 時刻 t 時点で有効な SV を返す（stable/lazer観測に合わせた仮説仕様）
            // [TimingPoints] SV（緑線）の扱い方針
            // - 赤線(beatLen>0)が来たらSV=1.0にリセット
            // - 緑線(beatLen<0)が来たらSVを更新 (sv=100/abs)
            // - 同時刻に赤＋緑がある場合：赤→緑の順に適用される想定
            // ■ 内部計算用（Split判定・配置に使用）
            // ・uninherited == 0（緑線）のみを SV 線として扱う
            // ・beatLength = NaN   → 無視する（直前の有効な緑線を維持 / フォールバック）
            // ・beatLength が正の値  → 符号を負に正規化する（-abs）
            // ・beatLength = +Infinity  → beatLength = -1000 にクランプ（SV = 0.1 相当）
            // ・beatLength = -Infinity  → beatLength = -1000 にクランプ（SV = 0.1 相当）
            // ・beatLength が非正規化数（例：1e-310 など）  → beatLength = -10 にクランプ（SV = 10 相当）
            // ・beatLength がアンダーフロー（例：1e-325 など）  → beatLength = -10 にクランプ（SV = 10 相当）
            // ・beatLength = 0  → beatLength = -10 にクランプ
            // ・beatLength は必ず [-1000, -10] に収める  → SV は必ず [0.1, 10] の範囲に収まる
            double SvAt(double t)
            {
                double sv = 1.0;  // SV指定がない場合は等倍
                const double SV_MIN = 0.1;
                const double SV_MAX = 10.0;

                const double BL_MIN = -1000.0; // SV=0.1 相当
                const double BL_MAX = -10.0;   // SV=10  相当
                const double EPS_BL = 1e-9;

                foreach (var tp in timing)
                {
                    if (tp.time > t) break;
                    if (tp.uninherited == 1)  // ★緑線判定は beatLen の符号ではなく uninherited で行う
                    {
                        sv = 1.0;  // 新しい timing section（赤線）に入ったら SV をデフォルトに戻す
                        continue;
                    }

                    if (tp.uninherited != 0)
                    {
                        continue;  // 想定外の値は無視（安全側）
                    }

                    // ---- ここから「内部SV（緑線）」正規化 ----
                    double bl = tp.beatLen;

                    // NaN は無視して、直前の有効な緑線を維持
                    if (double.IsNaN(bl))
                        continue;

                    // ±Infinity は最も遅いSV(=0.1)相当へ（beatLength=-1000）
                    if (double.IsInfinity(bl))
                    {
                        bl = BL_MIN; // -1000
                    }
                    else
                    {
                        // 符号は必ず負へ正規化（正の値が来ても -abs で救済）
                        bl = -Math.Abs(bl);

                        // 非正規化/アンダーフロー/0 相当は最も速いSV(=10)相当へ（beatLength=-10）
                        if (Math.Abs(bl) < EPS_BL)
                            bl = BL_MAX; // -10

                        // 最終的に [-1000, -10] に収める
                        bl = Math.Clamp(bl, BL_MIN, BL_MAX);
                    }

                    // SV = -100 / beatLength （beatLengthは負なのでSVは正になる）
                    sv = -100.0 / bl;

                    // 念のため（ここに来る時点で範囲内のはずだが保険）
                    if (sv < SV_MIN) sv = SV_MIN;
                    if (sv > SV_MAX) sv = SV_MAX;
                }
                return sv;
            }

            // ---- rewrite HitObjects ----
            // 「[HitObjects] セクションだけを走査して、スライダー行だけを（必要なら）分解して差し替える

            // outLines を新規作成し、入力テキスト lines の 先頭〜[HitObjects] 行までをそのままコピー
            var outLines = new List<string>();
            outLines.AddRange(lines.Take(idxHit + 1)); // include [HitObjects]

            for (int i = idxHit + 1; i < lines.Count; i++) // [HitObjects] の中身だけを1行ずつ処理する
            {
                var raw = lines[i];
                var t = raw.Trim();

                if (t.StartsWith("[") && t.EndsWith("]"))
                {
                    outLines.AddRange(lines.Skip(i));
                    break;
                }

                // 空行 or コメント行はそのまま保持
                if (t.Length == 0 || t.StartsWith("//"))
                {
                    outLines.Add(raw);
                    continue;
                }

                // HitObjects行を カンマ区切りで分割
                var parts = t.Split(',');
                // スライダー判定に必要な最低フィールド数が無い（＝壊れている/想定外）場合、改変せず原文のまま出力
                if (parts.Length < 8) { outLines.Add(raw); continue; }

                if (!int.TryParse(parts[2], out int startTime)) { outLines.Add(raw); continue; }  // 開始時刻（ms）

                // --- Aspire sanitizer が触った slider は分解しない（キー一致のみ） ---
                bool isNoSplit = (noSplitStartTimes != null && noSplitStartTimes.Contains(startTime));

                if (isNoSplit)
                {
                    // ここで raw をそのまま出力して終わり（= splitしない）
                    outLines.Add(raw);
                    continue;
                }

                if (!int.TryParse(parts[3], out int type)) { outLines.Add(raw); continue; }  // type ビットフラグ

                // type のビット 2 が立っていればスライダー
                bool isSlider = (type & 2) != 0;

                // スライダー以外は 一切触らずそのまま出力
                if (!isSlider) { outLines.Add(raw); continue; }

                // slider fields
                if (!int.TryParse(parts[6], out int repeats)) repeats = 1;  // スライダーの repeat count（osu!の「repeat回数」＝ spanCount 相当）
                repeats = Math.Max(1, repeats);  // 最低1保証

                if (!double.TryParse(parts[7], NumberStyles.Float, CultureInfo.InvariantCulture, out double pixelLength))
                { outLines.Add(raw); continue; }

                // slider curve definition (e.g., B|..., L|..., P|..., C|...)
                string curve = parts[5];  // スライダーの曲線定義フィールド

                // --- hitsound 情報（don/kat/big 判定用） ---
                int baseHitSound = 0;  // HitObjectの hitsound（頭の音）を整数として読む
                int.TryParse(parts[4], out baseHitSound);

                // head / repeat / tail それぞれのエッジ音を配列で読む
                int[] edgeHitSounds = Array.Empty<int>();
                if (parts.Length >= 9 && !string.IsNullOrWhiteSpace(parts[8]))
                {
                    edgeHitSounds = parts[8]
                        .Split('|')
                        .Select(s => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0)
                        .ToArray();
                }

                // 空なら「1個だけ」扱いにして循環させる（Count=0 を避ける）
                if (edgeHitSounds.Length == 0)
                    edgeHitSounds = new[] { baseHitSound };

                // スライダー開始座標。ExpectedDistance の「近似」用。
                int srcX = 0, srcY = 0;
                int.TryParse(parts[0], out srcX);
                int.TryParse(parts[1], out srcY);

                // distance を公式互換の slidableDistance として使う
                var (calculatedDistance, distance) =
                    LazerSliderPathDistance.Compute(curve, srcX, srcY, pixelLength);

                // 太鼓譜面出力座標を固定（中央）
                int x = 256, y = 192;


                // ==== Taiko split decision (official-compatible) ====
                // spans
                int spans = repeats;

                // timing beatLength（赤線）
                double timingBL = BaseBeatLengthAt(startTime);

                // SV倍率
                double sliderVel = Math.Max(1e-9, SvAt(startTime));

                // speed-adjusted beatLength (for taikoDuration/osuVelocity)
                double sliderVelocityAsBeatLength = -100 / sliderVel;
                double bpmMultiplier = sliderVelocityAsBeatLength < 0
                    ? Math.Clamp((float)-sliderVelocityAsBeatLength, 10, 10000) / 100.0
                    : 1;
                double beatLength0 = timingBL * bpmMultiplier; // speed-adjusted

                // beatLength used for tickSpacing & RHS compare (official: restore for v8+ AFTER osuVelocity computed)
                double beatLength = (inputFormatVersion >= 8) ? timingBL : beatLength0;

                // distance: MUST use SliderPath.Distance (already scaled to ExpectedDistance=pixelLength)
                double dist = distance; // <--- the 'distance' from LazerSliderPathDistance.Compute(...)
                dist *= (double)VELOCITY_MULTIPLIER;  // ★重要：微小誤差を残すために1 行でまとめずに2 行で記述（Do not combineとの記載あり）
                dist *= spans;

                // sliderScoringPointDistance
                double sliderScoringPointDistance =
                    BASE_SCORING_DISTANCE * (sliderMultiplier * (double)VELOCITY_MULTIPLIER) / sliderTickRate;

                // taikoVelocity
                double taikoVelocity = sliderScoringPointDistance * sliderTickRate;

                // taikoDuration (cast position matters)
                int taikoDuration = (int)(dist / taikoVelocity * beatLength0);

                // osuVelocity
                // ★重要：公式と同じ1000f を混ぜて float 丸め誤差を再現する
                double osuVelocity = taikoVelocity * (1000f / beatLength0);

                // tickSpacing uses restored beatLength (official)
                double tickSpacing = Math.Min(
                    beatLength / sliderTickRate,
                    (double)taikoDuration / spans
                );

                // final decision
                double lhs = dist / osuVelocity * 1000;
                double rhs = 2 * beatLength;

                bool shouldConvertToHits =
                    tickSpacing > 0 &&
                    lhs < rhs;

                // Diagnostic logging for split decision (boundary/ExpectedDistance analysis)
                const bool LOG_SPLIT_DIAGNOSTIC = false;

                if (LOG_SPLIT_DIAGNOSTIC)
                {
                    SplitDiagnostics.SplitDiagOfficial(
                        startTime: startTime,
                        curve: curve,
                        beatmapVersion: inputFormatVersion,
                        repeats: spans,
                        pixelLength: pixelLength,
                        calculatedDistance: calculatedDistance,
                        sliderPathDistance: distance,      // ← SliderPath.Compute の distance
                        timingBL: timingBL,
                        sliderVel: sliderVel,
                        bpmMultiplier: bpmMultiplier,
                        beatLength0: beatLength0,
                        beatLength: beatLength,
                        sliderMultiplier: sliderMultiplier,
                        sliderTickRate: sliderTickRate,
                        distScaled: dist,
                        sliderScoringPointDistance: sliderScoringPointDistance,
                        taikoVelocity: taikoVelocity,
                        taikoDuration: taikoDuration,
                        osuVelocity: osuVelocity,
                        tickSpacing: tickSpacing,
                        lhs: lhs,
                        rhs: rhs,
                        shouldConvertToHits: shouldConvertToHits
                    );
                }

                // 分解する Slider のみこの先の処理を行う
                bool shouldSplit = shouldConvertToHits;
                if (!shouldSplit)
                {
                    outLines.Add(raw);
                    continue;
                }


                // end time
                double end = startTime + taikoDuration;

                // endInt は taikoDuration(int切り捨て) から決める。roundはしない。
                int startTimeInt = (int)startTime; // 入力が整数ms前提（.osu）
                int endInt = startTimeInt + Math.Max(0, taikoDuration);

                // 末尾 extras を維持（無ければ既定）
                string extras = parts.Length >= 11 ? string.Join(",", parts.Skip(10)) : "0:0:0:0:";


                // ======== 量子化責務を1箇所に集約 ========
                // まずは “生成結果（double）” を全部ここに貯める。出力は最後にまとめて行う。
                var rawHits = new List<(double t, int hs)>();
                int addCount = 0;

                // 生成層：公式互換。ここでは一切 int 化しない / outLines に書かない。
                void AddHitRaw(double t, int hs)
                {
                    rawHits.Add((t, hs));
                }

                // 量子化層：double -> int(trunc) をここで一括実施。
                void FinalizeAndWriteHits()
                {
                    // 合成しない：同一 ms が出ても、そのまま複数行出力（公式寄せ）
                    var finalized = new List<(int timeOut, int hs, int order)>();
                    int order = 0;

                    foreach (var (t0, hs0) in rawHits)
                    {
                        double t = t0;

                        // 下限のみ clamp（上限は clamp しない）
                        if (t < startTime) t = startTime;

                        int timeOut = (int)Math.Round((double)t, 0, MidpointRounding.AwayFromZero);
                        if (timeOut < startTime) timeOut = startTime;

                        finalized.Add((timeOut, hs0, order++));
                    }

                    // 安定化：timeOut 昇順、同一時刻は生成順を維持
                    finalized.Sort((a, b) =>
                    {
                        int c = a.timeOut.CompareTo(b.timeOut);
                        return c != 0 ? c : a.order.CompareTo(b.order);
                    });

                    addCount = finalized.Count;

                    foreach (var (timeOut, hs, _) in finalized)
                        outLines.Add($"{x},{y},{timeOut},1,{hs},{extras}");

                }
                // ==================================================

                // fullEndInt を作る
                int fullEndInt = (int)Math.Round((double)startTime + (double)taikoDuration, 0, MidpointRounding.AwayFromZero);

                // repeatTimeToIndex は fullEndInt で作る
                var repeatTimeToIndex = new Dictionary<int, int>();
                if (repeats > 1)
                {
                    double total = fullEndInt - startTime;
                    for (int k = 1; k <= repeats - 1; k++)
                    {
                        double off = total * k / repeats;
                        int rt = (int)Math.Round(startTime + off, MidpointRounding.AwayFromZero);
                        repeatTimeToIndex[rt] = k;
                    }
                }


                // ---- placement rules (TaikoBeatmapConverter.cs / lazer互換) ----
                // Split decision has already been made. Now place hitobjects.
                //
                // lazer は NodeSamples を i=(i+1)%Count で巡回しつつ、
                // j = StartTime..StartTime+taikoDuration を tickSpacing 間隔で配置する。
                // 取りこぼし防止に上限へ tickSpacing/8 の余裕を持たせる。
                //
                // NOTE: この分岐では repeats 境界特例などは入れず、lazer の挙動へ寄せる。
                int nodeCount = (edgeHitSounds.Length > 0) ? edgeHitSounds.Length : 1;
                int nodeIndex = 0;

                double tickSpacing2 = tickSpacing; // 公式互換の tickSpacing
                double endTime = startTime + taikoDuration;
                double limit = endTime + (tickSpacing2 / 8.0);  // 取りこぼし防止

                // 通常：tick 生成ループ（生成は常に double のまま蓄積）
                for (double j = startTime; j <= limit; j += tickSpacing2)
                {
                    int hs = (edgeHitSounds.Length > 0)
                        ? edgeHitSounds[nodeIndex]
                        : baseHitSound;

                    AddHitRaw(j, hs);

                    nodeIndex = (nodeIndex + 1) % nodeCount;

                    if (Math.Abs(tickSpacing2) < 1e-12)
                        break;
                }

                // ★ split するはずだったのに、何も生成できなかった場合は元sliderを残す（消失防止）
                if (shouldSplit && rawHits.Count == 0)
                {
                    outLines.Add(raw);
                    Console.WriteLine($"[SplitEmpty->KeepSlider] t={startTime} (no hits generated, keep raw slider)");
                    continue;
                }

                // 一括量子化＋衝突処理＋出力
                FinalizeAndWriteHits();

            }

            // 最後に改行を整えて文字列に戻す
            return string.Join("\n", outLines).Replace("\n", "\r\n");
        }
    }
}
