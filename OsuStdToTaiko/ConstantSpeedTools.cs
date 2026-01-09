using System.Globalization;

namespace OsuStdToTaiko
{
    internal static class ConstantSpeedTools
    {
        // デフォルト値
        private const double SV_MIN = 0.01;  // とりあえず 0.01 に設定（これより低い値を要求する beatmap があれば修正予定：RiraN - Unshakable）　⇒ BPM修正案件... 今後の to do
        private const double SV_MAX = 10.0;
        private const string CLAMP_LOG_PREFIX = "// [ConstantSpeed] SV clamped";

        static bool TryClampSv(
            double rawSv,
            double svMin,
            double svMax,
            out double clampedSv)
        {
            clampedSv = rawSv;

            if (!(rawSv > 0) || double.IsNaN(rawSv) || double.IsInfinity(rawSv))
                return false;

            if (rawSv < svMin)
            {
                clampedSv = svMin;
                return true;
            }

            if (!double.IsInfinity(svMax) && rawSv > svMax)
            {
                clampedSv = svMax;
                return true;
            }

            return false;
        }
        static string FormatClampLog(string pass, double timeMs, double bpm, double rawSv, double clampedSv)
        {
            var inv = CultureInfo.InvariantCulture;
            return string.Format(
                inv,
                "{0} pass={1} t={2} bpm={3:G17} rawSv={4:G17} -> {5:G17}",
                CLAMP_LOG_PREFIX,
                pass,
                (long)Math.Round(timeMs),
                bpm,
                rawSv,
                clampedSv
            );
        }

        // most common BPM を計算するヘルパー
        internal static double GetMostCommonBpm(string osuText)
        {
            var inv = CultureInfo.InvariantCulture;
            var lines = osuText.Replace("\r\n", "\n").Split('\n').ToList();

            // TimingPoints 範囲
            int tpIdx = lines.FindIndex(l => l.Trim().Equals("[TimingPoints]", StringComparison.OrdinalIgnoreCase));
            if (tpIdx < 0) return double.NaN;

            int tpStart = tpIdx + 1;
            int tpEnd = tpStart;
            for (; tpEnd < lines.Count; tpEnd++)
            {
                var t = lines[tpEnd].Trim();
                if (t.StartsWith("[") && t.EndsWith("]"))
                    break;
            }

            // 赤線（uninherited=1, beatLen>0）区間の滞在時間で最頻を取る
            var acc = new Dictionary<double, double>(); // key: beatLen(round) -> duration(ms)

            double lastBeatLen = double.NaN;
            double lastTime = 0;

            for (int i = tpStart; i < tpEnd; i++)
            {
                var line = lines[i].Trim();
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("//"))
                    continue;

                var p = line.Split(',');
                if (p.Length < 7) continue;

                if (!double.TryParse(p[0], NumberStyles.Float, inv, out var time))
                    continue;
                if (!double.TryParse(p[1], NumberStyles.Float, inv, out var beatLen))
                    continue;
                if (!int.TryParse(p[6], NumberStyles.Integer, inv, out var uninherited))
                    continue;

                // 赤線のみ
                if (uninherited == 1 && beatLen > 0)
                {
                    if (!double.IsNaN(lastBeatLen))
                    {
                        double dur = time - lastTime;
                        if (dur > 0)
                        {
                            double key = Math.Round(lastBeatLen, 3); // 安定化
                            acc[key] = acc.TryGetValue(key, out var v) ? v + dur : dur;
                        }
                    }

                    lastBeatLen = beatLen;
                    lastTime = time;
                }
            }

            // 最後の赤線区間（次の赤線が無いので、末尾までの duration を推定するのは難しい）
            // → ここでは “最後区間は無視” という元の挙動を維持します。

            if (acc.Count == 0) return double.NaN;

            // 最長 duration の beatLength を採用
            double bestBeatLen = acc.OrderByDescending(kv => kv.Value).First().Key;
            return 60000.0 / bestBeatLen;
        }

        // ConstantSpeed の TimingPoints 書き換え処理
        // emitClampComments が true のときだけ行う
        internal static string ApplyConstantSpeedTimingPoints(string osuText)
            => ApplyConstantSpeedTimingPoints(osuText, SV_MIN, SV_MAX, emitClampComments: true);


        internal static string ApplyConstantSpeedTimingPoints(
            string osuText, double svMin, double svMax, bool emitClampComments)
        {
            var inv = CultureInfo.InvariantCulture;

            var lines = osuText.Replace("\r\n", "\n").Split('\n').ToList();
            int idx = lines.FindIndex(l => l.Trim().Equals("[TimingPoints]", StringComparison.OrdinalIgnoreCase));
            if (idx < 0) return osuText;

            // TimingPoints の範囲を特定
            int start = idx + 1;
            int end = start;
            for (; end < lines.Count; end++)
            {
                var t = lines[end].Trim();
                if (t.StartsWith("[") && t.EndsWith("]"))
                    break;
            }

            // TimingPoints を解析して (time, idx, p, uninherited, beatLen) を保持
            var tp = new List<(double time, int idx, string[] p, int uninherited, double beatLen)>();
            for (int i = start; i < end; i++)
            {
                string line = lines[i];
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("//")) continue;

                var p = line.Split(',');
                if (p.Length < 7) continue;

                if (!double.TryParse(p[0], NumberStyles.Float, inv, out var time)) continue;
                if (!double.TryParse(p[1], NumberStyles.Float, inv, out var beatLen)) continue;
                if (!int.TryParse(p[6], NumberStyles.Integer, inv, out var uninherited)) continue;

                tp.Add((time, i, p, uninherited, beatLen));
            }

            // ヘルパー：指定時刻までの最新の赤線 beatLen
            double RedBeatLenAt(double timeMs)
            {
                double cur = double.NaN;
                foreach (var x in tp.OrderBy(x => x.time))
                {
                    if (x.time > timeMs) break;
                    if (x.uninherited == 1 && x.beatLen > 0)
                        cur = x.beatLen;
                }
                return cur;
            }

            // ヘルパー：指定時刻までの最新のSV（緑線）を取得（SV= -100/beatLen）
            double OldSvAt(double timeMs)
            {
                double sv = 1.0;
                foreach (var x in tp.OrderBy(x => x.time))
                {
                    if (x.time > timeMs) break;
                    if (x.uninherited == 0 && x.beatLen < 0)
                    {
                        double b = x.beatLen;
                        if (Math.Abs(b) < 1e-12) continue;
                        sv = -100.0 / b;
                    }
                }
                if (!(sv > 0) || double.IsNaN(sv) || double.IsInfinity(sv)) return 1.0;
                return sv;
            }

            // refSpeed = baseBPM * 1.0
            // baseBPM は「最初に確定できた bpm×sv」方式（現行仕様）をここで再現
            // まず最頻BPM（赤線滞在時間ベース）を使う
            double baseBpm = GetMostCommonBpm(osuText);
            if (!(baseBpm > 0) || double.IsNaN(baseBpm) || double.IsInfinity(baseBpm))
            {
                // fallback: 最初の赤線から
                var firstRed = tp.Where(x => x.uninherited == 1 && x.beatLen > 0).OrderBy(x => x.time).FirstOrDefault();
                if (firstRed.beatLen > 0) baseBpm = 60000.0 / firstRed.beatLen;
            }
            if (!(baseBpm > 0)) return osuText;

            double refSpeed = baseBpm * 1.0;

            // クランプ発生ログを収集（最後に TimingPoints 末尾へまとめて書く）
            var clampLogs = new List<string>();

            // パス1：既存緑線（beatLen<0）を一定化
            {
                double currentRedBeatLen = double.NaN;

                for (int i = start; i < end; i++)
                {
                    var line = lines[i];
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("//")) continue;

                    var p = line.Split(',');
                    if (p.Length < 7) continue;

                    if (!double.TryParse(p[0], NumberStyles.Float, inv, out var timeMs)) continue;
                    if (!double.TryParse(p[1], NumberStyles.Float, inv, out var beatLen)) continue;
                    if (!int.TryParse(p[6], NumberStyles.Integer, inv, out var uninherited)) continue;

                    if (uninherited == 1 && beatLen > 0)
                    {
                        currentRedBeatLen = beatLen;
                        continue;
                    }

                    // 緑線だけを一定化する（beatLen<0 のSV線のみ）
                    if (uninherited == 0 && beatLen < 0)
                    {
                        if (double.IsNaN(currentRedBeatLen) || currentRedBeatLen <= 0)
                            continue;

                        double bpm = 60000.0 / currentRedBeatLen;

                        // SV = ref / BPM
                        double rawSv = refSpeed / bpm;
                        double newSv = rawSv;
                        if (TryClampSv(rawSv, svMin, svMax, out var clampedSv))
                        {
                            newSv = clampedSv;
                            clampLogs.Add(FormatClampLog("existing-green", timeMs, bpm, rawSv, clampedSv));
                        }

                        double newBeatLen = -100.0 / newSv;
                        p[1] = newBeatLen.ToString("G17", inv);

                        lines[i] = string.Join(",", p);
                    }
                }
            }

            // 追加パス：赤線の時刻に「同時刻の緑線」が無ければ、一定化SVの緑線を挿入
            {
                // 赤線時刻一覧
                var redTimes = tp
                    .Where(x => x.uninherited == 1 && x.beatLen > 0)
                    .Select(x => x.time)
                    .Distinct()
                    .OrderBy(x => x)
                    .ToList();

                // 緑線時刻一覧
                var greenTimes = tp
                    .Where(x => x.uninherited == 0 && x.beatLen < 0)
                    .Select(x => x.time)
                    .ToHashSet();

                foreach (var t in redTimes)
                {
                    if (greenTimes.Contains(t))
                        continue;

                    double beatLen = RedBeatLenAt(t);
                    if (!(beatLen > 0)) continue;

                    double bpm = 60000.0 / beatLen;
                    double rawSv = refSpeed / bpm;
                    double newSv = rawSv;
                    if (TryClampSv(rawSv, svMin, svMax, out var clampedSv))
                    {
                        newSv = clampedSv;
                        clampLogs.Add(FormatClampLog("insert-green", t, bpm, rawSv, clampedSv));
                    }

                    double newBeatLen = -100.0 / newSv;

                    // 既存の赤線行の直後に挿入するため、赤線の行indexを探す（最初の一致）
                    var red = tp.Where(x => x.time == t && x.uninherited == 1 && x.beatLen > 0)
                                .OrderBy(x => x.idx)
                                .FirstOrDefault();
                    if (red.p == null) continue;

                    // 緑線テンプレート（time, beatLen, meter, sampleSet, sampleIndex, volume, uninherited=0, effects）
                    // 元の緑線が無いので、赤線のメータ等をコピーして “uninherited=0” にする
                    var p = (string[])red.p.Clone();
                    p[1] = newBeatLen.ToString("G17", inv);
                    p[6] = "0";
                    lines.Insert(red.idx + 1, string.Join(",", p));

                    // 挿入でindexがズレるが、以降は使わないのでOK
                }
            }

            // slider / drumroll の length 補正（duration維持）
            // ※現行ロジック維持（ここも今は触らない）
            {
                int hoIdx = lines.FindIndex(l => l.Trim().Equals("[HitObjects]", StringComparison.OrdinalIgnoreCase));
                if (hoIdx >= 0)
                {
                    int hoStart = hoIdx + 1;
                    int hoEnd = hoStart;
                    for (; hoEnd < lines.Count; hoEnd++)
                    {
                        var t = lines[hoEnd].Trim();
                        if (t.StartsWith("[") && t.EndsWith("]"))
                            break;
                    }

                    for (int i = hoStart; i < hoEnd; i++)
                    {
                        var line = lines[i].Trim();
                        if (string.IsNullOrWhiteSpace(line) || line.StartsWith("//")) continue;

                        // slider のみ対象（type bit 2）
                        var p = line.Split(',');
                        if (p.Length < 8) continue;

                        if (!double.TryParse(p[2], NumberStyles.Float, inv, out var timeMs)) continue;
                        if (!int.TryParse(p[3], NumberStyles.Integer, inv, out var type)) continue;
                        bool isSlider = (type & 2) != 0;
                        if (!isSlider) continue;

                        if (!double.TryParse(p[7], NumberStyles.Float, inv, out var pixelLen)) continue;

                        double beatLen = RedBeatLenAt(timeMs);
                        if (!(beatLen > 0)) continue;

                        double bpm = 60000.0 / beatLen;

                        double oldSv = OldSvAt(timeMs);
                        if (!(oldSv > 0) || double.IsNaN(oldSv) || double.IsInfinity(oldSv)) continue;

                        double rawSv = refSpeed / bpm;
                        double newSv = rawSv;
                        if (TryClampSv(rawSv, svMin, svMax, out var clampedSv))
                        {
                            newSv = clampedSv;
                            clampLogs.Add(FormatClampLog("slider-scale", timeMs, bpm, rawSv, clampedSv));
                        }

                        double ratio = newSv / oldSv;

                        double newPixelLen = pixelLen * ratio;
                        p[7] = newPixelLen.ToString("G17", inv);

                        lines[i] = string.Join(",", p);
                    }
                }
            }

            // 最後に clamp ログを [TimingPoints] 末尾へまとめて挿入
            if (clampLogs.Count > 0)
            {
                // 現在の TimingPoints 範囲を再取得（途中で Insert して idx/end がズレるため）
                int tpIdx2 = lines.FindIndex(l => l.Trim().Equals("[TimingPoints]", StringComparison.OrdinalIgnoreCase));
                if (tpIdx2 >= 0)
                {
                    int tpStart2 = tpIdx2 + 1;
                    int tpEnd2 = tpStart2;
                    for (; tpEnd2 < lines.Count; tpEnd2++)
                    {
                        var t = lines[tpEnd2].Trim();
                        if (t.StartsWith("[") && t.EndsWith("]"))
                            break;
                    }

                    lines.Insert(tpEnd2, "");
                    tpEnd2++;
                    lines.Insert(tpEnd2, CLAMP_LOG_PREFIX + " (summary)");
                    tpEnd2++;
                    foreach (var s in clampLogs)
                    {
                        lines.Insert(tpEnd2, s);
                        tpEnd2++;
                    }
                }
            }

            return string.Join("\n", lines).Replace("\n", "\r\n");
        }

        /// <summary>
        /// SVA 適用後に、constant-speed 基準の「時間長さ」を維持するために
        /// slider/drumroll の pixelLength を再スケールする。
        ///
        /// baseText:
        ///   SVA 適用前（＝constant speed 完了直後）の .osu テキスト
        /// osuText:
        ///   SVA で TimingPoints を書き換えたあとの .osu テキスト
        /// </summary>
        internal static string ApplySliderDurationFixForSva(string baseText, string osuText)
        {
            var inv = CultureInfo.InvariantCulture;

            // 前後それぞれの TimingPoints をパース
            static List<(double time, double beatLen, int uninherited)> BuildTimingPoints(
                string text,
                IFormatProvider inv)
            {
                var list = new List<(double time, double beatLen, int uninherited)>();

                var lines = text.Replace("\r\n", "\n").Split('\n');
                int idx = Array.FindIndex(lines, l =>
                    l.Trim().Equals("[TimingPoints]", StringComparison.OrdinalIgnoreCase));
                if (idx < 0) return list;

                int start = idx + 1;
                for (int i = start; i < lines.Length; i++)
                {
                    var line = lines[i];
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("//"))
                        continue;

                    var p = line.Split(',');
                    if (p.Length < 7) continue;

                    if (!double.TryParse(p[0], NumberStyles.Float, inv, out var time)) continue;
                    if (!double.TryParse(p[1], NumberStyles.Float, inv, out var beatLen)) continue;
                    if (!int.TryParse(p[6], NumberStyles.Integer, inv, out var uninherited)) continue;

                    list.Add((time, beatLen, uninherited));
                }

                // 時刻順にソート
                list.Sort((a, b) => a.time.CompareTo(b.time));
                return list;
            }

            var tpBase = BuildTimingPoints(baseText, inv);
            var tpNew = BuildTimingPoints(osuText, inv);

            if (tpBase.Count == 0 || tpNew.Count == 0)
                return osuText; // TimingPoints が取れない場合は何もしない

            double RedBeatLenAt(List<(double time, double beatLen, int uninherited)> tp, double timeMs)
            {
                double cur = double.NaN;
                foreach (var x in tp)
                {
                    if (x.time > timeMs) break;
                    if (x.uninherited == 1 && x.beatLen > 0)
                        cur = x.beatLen;
                }
                return cur;
            }

            double SvAt(List<(double time, double beatLen, int uninherited)> tp, double timeMs)
            {
                double sv = 1.0;
                foreach (var x in tp)
                {
                    if (x.time > timeMs) break;
                    if (x.uninherited == 0 && x.beatLen < 0)
                    {
                        double b = x.beatLen;
                        if (Math.Abs(b) < 1e-12) continue;
                        sv = -100.0 / b;
                    }
                }

                if (!(sv > 0) || double.IsNaN(sv) || double.IsInfinity(sv))
                    return 1.0;
                return sv;
            }

            // osuText 側の HitObjects を走査して slider/drumroll の pixelLength を再スケール
            var linesNew = osuText.Replace("\r\n", "\n").Split('\n').ToList();

            int hoIdx = linesNew.FindIndex(l =>
                l.Trim().Equals("[HitObjects]", StringComparison.OrdinalIgnoreCase));
            if (hoIdx < 0)
                return osuText;

            int hoStart = hoIdx + 1;

            for (int i = hoStart; i < linesNew.Count; i++)
            {
                var line = linesNew[i];
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("//"))
                    continue;

                var p = line.Split(',');
                if (p.Length < 8) continue; // slider の pixelLength は p[7]

                if (!double.TryParse(p[2], NumberStyles.Float, inv, out var timeMs)) continue;
                if (!int.TryParse(p[3], NumberStyles.Integer, inv, out var type)) continue;

                bool isSlider = (type & 2) != 0;
                if (!isSlider) continue;

                if (!double.TryParse(p[7], NumberStyles.Float, inv, out var pixelLen)) continue;

                double beatLenBase = RedBeatLenAt(tpBase, timeMs);
                double beatLenNew = RedBeatLenAt(tpNew, timeMs);
                if (!(beatLenBase > 0) || !(beatLenNew > 0))
                    continue;

                double svBase = SvAt(tpBase, timeMs);
                double svNew = SvAt(tpNew, timeMs);
                if (!(svBase > 0) || !(svNew > 0))
                    continue;

                // duration ∝ pixelLen * beatLen / SV を維持するようにスケール
                double ratio = (beatLenBase * svNew) / (beatLenNew * svBase);
                if (!(ratio > 0) || double.IsNaN(ratio) || double.IsInfinity(ratio))
                    continue;

                double newPixelLen = pixelLen * ratio;
                p[7] = newPixelLen.ToString("G17", inv);
                linesNew[i] = string.Join(",", p);
            }

            return string.Join("\n", linesNew).Replace("\n", "\r\n");
        }
    }
}
