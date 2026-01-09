using System.Globalization;

namespace OsuStdToTaiko
{
    public static partial class StableVisualAssist
    {
        /// <summary>
        /// SVA: 赤線倍化のみ適用する
        ///  - DetectSvaSegments() の結果を使用
        ///  - 元の赤線は残し、同時刻に「倍化した赤線」を追加する
        ///  - 緑線・HitObjects はまだ触らない
        /// </summary>
        internal static string ApplyRedlineDoublingOnly(
            string osuText,
            List<SvaSegment> segments)
        {
            Console.WriteLine("[SVA] segments:");
            foreach (var s in segments)
                Console.WriteLine($"  seg start={s.StartTimeMs:G17}");

            if (segments == null || segments.Count == 0)
                return osuText;

            var inv = CultureInfo.InvariantCulture;
            var lines = osuText.Replace("\r\n", "\n").Split('\n').ToList();

            // [TimingPoints] 範囲取得
            int tpIdx = lines.FindIndex(l => l.Trim().Equals("[TimingPoints]", StringComparison.OrdinalIgnoreCase));
            if (tpIdx < 0) return osuText;

            int start = tpIdx + 1;
            int end = start;
            for (; end < lines.Count; end++)
            {
                var t = lines[end].Trim();
                if (t.StartsWith("[") && t.EndsWith("]"))
                    break;
            }


            // 赤線候補を収集（後ろから insert するため lineIndex 付き）
            var redLines = new List<(double time, int lineIndex, string[] parts, double beatLen)>();

            for (int i = start; i < end; i++)
            {
                var line = lines[i];
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("//"))
                    continue;

                var p = line.Split(',');
                if (p.Length < 7) continue;

                if (!double.TryParse(p[0], NumberStyles.Float, inv, out var time)) continue;
                if (!double.TryParse(p[1], NumberStyles.Float, inv, out var beatLen)) continue;
                if (!int.TryParse(p[6], NumberStyles.Integer, inv, out var uninherited)) continue;

                // 赤線のみ
                if (uninherited == 1 && beatLen > 0)
                {
                    redLines.Add((time, i, p, beatLen));
                }
            }

            Console.WriteLine($"[SVA] redLines.Count={redLines.Count}");
            Console.WriteLine("[SVA] red lines:");
            foreach (var r in redLines.Take(10))
                Console.WriteLine($"  red time={r.time:G17} idx={r.lineIndex} beatLen={r.beatLen:G17}");


            if (redLines.Count == 0)
                return osuText;


            // SVA適用ログ用（segごと）
            var perSegInserted = new Dictionary<long, int>();   // segStart(round) -> inserted greenlines
            var perSegMerged = new Dictionary<long, int>();     // segStart(round) -> removed redlines count
            var perSegOldNewBpm = new Dictionary<long, (double oldBpm, double newBpm, double oldBeatLen, double newBeatLen)>();

            // 置換方式：seg.StartTimeMs と同時刻の赤線は 1 本だけ残し、その 1 本の beatLength を
            //  - 高SV救済: /Multiplier（BPM×Multiplier）
            //  - 低SV救済: *Multiplier（BPM÷Multiplier）
            // として書き換える（stable は同時刻に赤線が複数あると採用が不安定なため）
            const double TIME_EPS = 0.5; // ms

            foreach (var seg in segments.OrderByDescending(s => s.StartTimeMs))
            {
                if (seg.Multiplier <= 1)
                    continue;

                // 同時刻の赤線を全部拾う
                var sameTimeReds = redLines
                    .Where(r => Math.Abs(r.time - seg.StartTimeMs) <= TIME_EPS)
                    .OrderByDescending(r => r.lineIndex)
                    .ToList();

                if (sameTimeReds.Count == 0)
                    continue;

                // 1本だけ残す：ファイル上で一番後ろ（lineIndex 最大）を勝者にする
                var keep = sameTimeReds[0];

                long segKey = (long)Math.Round(seg.StartTimeMs);

                // --- old/new BPM & beatLength 記録 ---
                double oldBeatLen = keep.beatLen;
                double oldBpm = 60000.0 / oldBeatLen;

                // 置換後の beatLength
                //  - 高SV救済: beatLen / Multiplier  （BPM × Multiplier）
                //  - 低SV救済: beatLen * Multiplier  （BPM ÷ Multiplier）
                double factor = seg.Multiplier;
                double newBeatLen = seg.IsHighSv
                    ? keep.beatLen / factor
                    : keep.beatLen * factor;
                double newBpm = 60000.0 / newBeatLen;

                // seg ごとの BPM 置換ログを記録
                perSegOldNewBpm[segKey] = (oldBpm, newBpm, oldBeatLen, newBeatLen);

                // 同時刻赤線が複数あった場合の「削除本数」
                int removed = Math.Max(0, sameTimeReds.Count - 1);
                if (removed > 0)
                {
                    if (!perSegMerged.ContainsKey(segKey))
                        perSegMerged[segKey] = 0;

                    perSegMerged[segKey] += removed;
                }

                // --- keep 行を書き換え ---
                var keepParts = (string[])keep.parts.Clone();
                keepParts[1] = newBeatLen.ToString("G17", inv);
                lines[keep.lineIndex] = string.Join(",", keepParts);


                // 残り（同時刻の他の赤線）は削除
                for (int k = 1; k < sameTimeReds.Count; k++)
                {
                    int removeIdx = sameTimeReds[k].lineIndex;
                    lines.RemoveAt(removeIdx);
                    end--; // TimingPoints 範囲が 1 行縮む

                    // Remove により index が詰まるので、redLines 側の index も更新
                    for (int i = 0; i < redLines.Count; i++)
                    {
                        if (redLines[i].lineIndex > removeIdx)
                            redLines[i] = (redLines[i].time, redLines[i].lineIndex - 1, redLines[i].parts, redLines[i].beatLen);
                    }
                }

                // keep 自体も beatLen が変わったので redLines に反映
                for (int i = 0; i < redLines.Count; i++)
                {
                    if (redLines[i].lineIndex == keep.lineIndex)
                        redLines[i] = (redLines[i].time, redLines[i].lineIndex, keepParts, newBeatLen);
                }
            }


            // =========================
            // SVA: 緑線補完（赤線時刻に緑線が無ければ挿入）
            //  - 「追加した赤線の時刻」(seg.StartTimeMs) に緑線が無い場合
            //  - 直前で有効な緑線をコピーして同時刻に挿入する
            //  - その後の緑線補正（beatLen *= Multiplier）で整合する
            // =========================
            Console.WriteLine("[SVA] applying greenline insertion (if missing)...");

            // TimingPoints内の「緑線が存在する時刻」集合を作る（高速化）
            var greenTimes = new HashSet<long>();
            for (int i = start; i < end; i++)
            {
                var line = lines[i];
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("//"))
                    continue;

                var p = line.Split(',');
                if (p.Length < 7) continue;

                if (!double.TryParse(p[0], NumberStyles.Float, inv, out var t)) continue;
                if (!double.TryParse(p[1], NumberStyles.Float, inv, out var bl)) continue;
                if (!int.TryParse(p[6], NumberStyles.Integer, inv, out var u)) continue;

                // 緑線のみ
                if (u == 0 && bl < 0)
                    greenTimes.Add((long)Math.Round(t));
            }

            // 直前の有効緑線を探すヘルパ（segStartより前の最新緑線を返す）
            string[]? FindPrevGreenParts(double segStartTime)
            {
                string[]? best = null;
                double bestTime = double.NegativeInfinity;

                for (int i = start; i < end; i++)
                {
                    var line = lines[i];
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("//"))
                        continue;

                    var p = line.Split(',');
                    if (p.Length < 7) continue;

                    if (!double.TryParse(p[0], NumberStyles.Float, inv, out var t)) continue;
                    if (t > segStartTime) break; // TimingPointsは基本時刻昇順の前提（昇順でない譜面は稀）
                    if (!double.TryParse(p[1], NumberStyles.Float, inv, out var bl)) continue;
                    if (!int.TryParse(p[6], NumberStyles.Integer, inv, out var u)) continue;

                    if (u == 0 && bl < 0)
                    {
                        if (t >= bestTime)
                        {
                            bestTime = t;
                            best = p;
                        }
                    }
                }

                return best;
            }

            int inserted = 0;

            foreach (var seg in segments)
            {
                long tKey = (long)Math.Round(seg.StartTimeMs);

                // 既に同時刻の緑線があれば何もしない
                if (greenTimes.Contains(tKey))
                    continue;

                // 直前の緑線をコピーして挿入する
                var prev = FindPrevGreenParts(seg.StartTimeMs);
                if (prev == null)
                {
                    // 直前緑線が見つからない場合は補完不能（譜面先頭など）
                    Console.WriteLine($"[SVA] WARN: no previous greenline to copy at t={seg.StartTimeMs:0}");
                    continue;
                }

                // 時刻だけ差し替えて挿入
                var ins = (string[])prev.Clone();
                ins[0] = tKey.ToString(inv);

                // 挿入位置：seg.StartTimeMs と同時刻の赤線の直後に入れたいので、
                // TimingPoints内で「その時刻の最後の行」の次に入れる
                int insertAt = end; // fallback
                for (int i = start; i < end; i++)
                {
                    var line = lines[i];
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("//"))
                        continue;

                    var p = line.Split(',');
                    if (p.Length < 1) continue;
                    if (!double.TryParse(p[0], NumberStyles.Float, inv, out var t)) continue;

                    if (t > seg.StartTimeMs)
                    {
                        insertAt = i;
                        break;
                    }
                }

                lines.Insert(insertAt, string.Join(",", ins));
                end++; // InsertしたのでTimingPoints末尾も1つ伸びる

                greenTimes.Add(tKey);
                inserted++;

                if (!perSegInserted.ContainsKey(tKey)) perSegInserted[tKey] = 0;
                perSegInserted[tKey] += 1;

                Console.WriteLine($"[SVA] inserted greenline at t={tKey} (copied from prev)");
            }

            Console.WriteLine($"[SVA] greenline inserted count={inserted}");


            // =========================
            // SVA: 緑線補正（SV / Multiplier または SV * Multiplier）だけ
            //  - seg.StartTimeMs <= time < seg.EndTimeMs の範囲の緑線（uninherited=0, beatLen<0）を対象
            //  - 高SV: beatLength *= Multiplier（SV は /Multiplier）
            //  - 低SV: beatLength /= Multiplier（SV は *Multiplier）
            // =========================
            Console.WriteLine("[SVA] applying greenline fix...");

            foreach (var seg in segments)
            {
                if (seg.Multiplier <= 1)
                    continue;

                double segStart = seg.StartTimeMs;
                double segEnd = seg.EndTimeMs; // Detect が next red time を入れている前提（無限の可能性あり）

                int touched = 0;

                for (int i = start; i < end; i++)
                {
                    var line = lines[i];
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("//"))
                        continue;

                    var p = line.Split(',');
                    if (p.Length < 7)
                        continue;

                    if (!double.TryParse(p[0], NumberStyles.Float, inv, out var t))
                        continue;

                    // 範囲判定：segStart <= t < segEnd
                    if (t < segStart)
                        continue;
                    if (!double.IsInfinity(segEnd) && t >= segEnd)
                        continue;

                    if (!double.TryParse(p[1], NumberStyles.Float, inv, out var beatLen))
                        continue;

                    if (!int.TryParse(p[6], NumberStyles.Integer, inv, out var uninherited))
                        continue;

                    // 緑線のみ
                    if (uninherited == 0 && beatLen < 0)
                    {
                        double newBeatLen;
                        if (seg.IsHighSv)
                        {
                            // 高SV救済: beatLen *= Multiplier （SV は /Multiplier）
                            newBeatLen = beatLen * seg.Multiplier;
                        }
                        else
                        {
                            // 低SV救済: beatLen /= Multiplier （SV は *Multiplier）
                            newBeatLen = beatLen / seg.Multiplier;
                        }

                        // 置換
                        p[1] = newBeatLen.ToString("G17", inv);
                        lines[i] = string.Join(",", p);

                        touched++;
                    }
                }

                Console.WriteLine($"[SVA] greenline fixed: t={segStart:0}..{(double.IsInfinity(segEnd) ? -1 : segEnd):0} x{seg.Multiplier} count={touched}");
            }


            // SVA適用後に [TimingPoints] を「時刻順に正規化」する
            static void NormalizeTimingPointsOrder(List<string> lines, int start, int end)
            {
                var inv = CultureInfo.InvariantCulture;

                // 「コメント/空行 + 実タイミング行」をひとまとめのブロックとして扱う
                var blocks = new List<(double time, int uninherited, double beatLen, List<string> raw)>();
                var pending = new List<string>();

                void FlushPendingWithoutTiming()
                {
                    if (pending.Count > 0)
                    {
                        // timing を持たないブロックは末尾扱い（time=+inf）
                        blocks.Add((double.PositiveInfinity, 2, 0, new List<string>(pending)));
                        pending.Clear();
                    }
                }

                for (int i = start; i < end; i++)
                {
                    string line = lines[i];
                    string t = line.Trim();

                    if (t.Length == 0 || t.StartsWith("//"))
                    {
                        pending.Add(line);
                        continue;
                    }

                    // timing 行っぽいもの
                    var p = t.Split(',');
                    if (p.Length >= 7
                        && double.TryParse(p[0], NumberStyles.Float, inv, out var time)
                        && double.TryParse(p[1], NumberStyles.Float, inv, out var beatLen)
                        && int.TryParse(p[6], NumberStyles.Integer, inv, out var uninherited))
                    {
                        var raw = new List<string>(pending);
                        raw.Add(line);
                        pending.Clear();

                        blocks.Add((time, uninherited, beatLen, raw));
                    }
                    else
                    {
                        // パース不能行はコメント扱いで保持
                        pending.Add(line);
                    }
                }
                FlushPendingWithoutTiming();

                // 並べ替え
                var ordered = blocks
                    .OrderBy(b => b.time)
                    .ThenBy(b => b.uninherited == 1 ? 0 : (b.uninherited == 0 ? 1 : 2)) // red -> green -> others
                    .ThenByDescending(b => (b.uninherited == 1 ? b.beatLen : 0))         // same-time red: big beatLen first
                    .ToList();

                var outBody = new List<string>();
                foreach (var b in ordered)
                    outBody.AddRange(b.raw);

                // 置換
                for (int i = 0; i < end - start; i++)
                    lines[start + i] = outBody[i];
            }


            // --- SVA applied markers (for GUI scan) ---
            // 置換方式では「赤線ペア」が残らないため、適用区間をコメントでマーキングする
            // 見た目用：TimingPoints末尾が空行でなければ1行空ける
            if (end > start && !string.IsNullOrWhiteSpace(lines[end - 1]))
            {
                lines.Insert(end, "");
                end++;
            }

            lines.Insert(end, "// [SVA] applied markers");
            int markerAt = end + 1;

            foreach (var seg in segments.OrderBy(s => s.StartTimeMs))
            {
                long key = (long)Math.Round(seg.StartTimeMs);

                // 置換情報がない seg はスキップ（念のため）
                if (!perSegOldNewBpm.TryGetValue(key, out var bpmInfo))
                    continue;

                int merged = perSegMerged.TryGetValue(key, out var m) ? m : 0;
                int ins = perSegInserted.TryGetValue(key, out var g) ? g : 0;

                string segEndStr = double.IsInfinity(seg.EndTimeMs)
                    ? "INF"
                    : ((long)Math.Round(seg.EndTimeMs)).ToString(inv);

                // BPMは見やすく丸め（必要なら桁を増やしてOK）
                string line =
                    $"// [SVA] applied t={key}..{segEndStr} x{seg.Multiplier} " +
                    $"bpm={bpmInfo.oldBpm:0.########}->{bpmInfo.newBpm:0.########} " +
                    $"beatLen={bpmInfo.oldBeatLen:G17}->{bpmInfo.newBeatLen:G17} " +
                    $"mergedReds={merged} insertedGreens={ins}";

                lines.Insert(markerAt++, line);
            }
            lines.Insert(markerAt++, "");
            end = markerAt; // TimingPoints 範囲が伸びたので end を更新
                            // --- /SVA applied markers ---


            // ★SVA挿入後にTimingPointsの並びを正規化（時刻順）
            NormalizeTimingPointsOrder(lines, start, end);

            // 見た目のため：TimingPoints の最後と次セクションの間に必ず空行を1つ入れる
            // （Normalizeの結果、空行が消えたり位置がズレることがあるため）
            if (end > start)
            {
                // end は次セクション（[HitObjects]など）の直前
                // end-1 が TimingPoints の最終行
                if (!string.IsNullOrWhiteSpace(lines[end - 1]))
                {
                    lines.Insert(end, "");
                    end++;
                }
            }

            return string.Join("\n", lines).Replace("\n", "\r\n");
        }
    }
}
