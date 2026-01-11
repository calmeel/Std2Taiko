using System.Globalization;

namespace OsuStdToTaiko
{
    public static partial class StableVisualAssist
    {
        // SVA適用済み譜面から区間を復元する検出関数
        /// <summary>
        /// SVA適用済みのTimingPointsから「倍化赤線ペア」を検出し、SvaSegment（Start/End/Multiplier）を復元する。
        ///  - 同時刻に正のbeatLengthが2本以上ある時刻を候補とする
        ///  - 最大beatLen / 最小beatLen から multiplier を推定（2^nに丸め）
        ///  - EndTimeは「次の赤線（uninherited=1）の時刻」
        /// </summary>
        public static List<SvaSegment> DetectAppliedSvaSegments(string osuText)  // Form1.cs から直接呼んでいるので、ここは public で OK
        {
            var inv = CultureInfo.InvariantCulture;
            var lines = osuText.Replace("\r\n", "\n").Split('\n').ToList();

            int tpIdx = lines.FindIndex(l => l.Trim().Equals("[TimingPoints]", StringComparison.OrdinalIgnoreCase));
            if (tpIdx < 0) return new List<SvaSegment>();

            int start = tpIdx + 1;
            int end = start;
            for (; end < lines.Count; end++)
            {
                var t = lines[end].Trim();
                if (t.StartsWith("[") && t.EndsWith("]"))
                    break;
            }

            // ============================================================
            // 1) New: marker-based detection (preferred)
            //   // [SVA] applied t=96348..99279 x16 bpm=1.359->21.75 ... mergedReds=1 insertedGreens=0
            // ============================================================
            long ParseTimeToken(string s)
            {
                s = s.Trim();
                if (s.Equals("INF", StringComparison.OrdinalIgnoreCase))
                    return long.MaxValue;

                // double/floatも来うるのでまずdoubleで受ける
                if (double.TryParse(s, NumberStyles.Float, inv, out var dv))
                    return (long)Math.Round(dv);

                if (long.TryParse(s, NumberStyles.Integer, inv, out var lv))
                    return lv;

                return long.MinValue;
            }

            int ParseIntToken(string s)
            {
                if (int.TryParse(s.Trim(), NumberStyles.Integer, inv, out var v))
                    return v;
                return 0;
            }

            double ParseDoubleToken(string s)
            {
                if (double.TryParse(s.Trim(), NumberStyles.Float, inv, out var v))
                    return v;
                return 0;
            }

            var markerSegs = new List<SvaSegment>();

            for (int i = start; i < end; i++)
            {
                var line = lines[i].Trim();
                if (!line.StartsWith("//")) continue;
                if (!line.StartsWith("// [SVA] applied", StringComparison.Ordinal)) continue;

                // 簡易パーサ： "key=value" を拾う
                // 例: t=96348..99279 x16 bpm=1.359->21.75 mergedReds=1 insertedGreens=0
                // 先頭の "// [SVA] applied" を落として分割
                var body = line.Substring("// [SVA] applied".Length).Trim();
                var tokens = body.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

                long segStart = long.MinValue;
                long segEnd = long.MaxValue;
                int mul = 1;
                int mergedReds = 0;
                int insertedGreens = 0;
                double oldBpm = 0, newBpm = 0;

                foreach (var tok in tokens)
                {
                    // x16 形式
                    if (tok.Length >= 2 && (tok[0] == 'x' || tok[0] == 'X'))
                    {
                        mul = ParseIntToken(tok.Substring(1));
                        continue;
                    }

                    int eq = tok.IndexOf('=');
                    if (eq < 0) continue;

                    var key = tok.Substring(0, eq).Trim();
                    var val = tok.Substring(eq + 1).Trim();

                    if (key.Equals("t", StringComparison.OrdinalIgnoreCase))
                    {
                        var parts = val.Split(new[] { ".." }, StringSplitOptions.None);
                        if (parts.Length == 2)
                        {
                            segStart = ParseTimeToken(parts[0]);
                            segEnd = ParseTimeToken(parts[1]);
                        }
                    }
                    else if (key.Equals("bpm", StringComparison.OrdinalIgnoreCase))
                    {
                        var parts = val.Split(new[] { "->" }, StringSplitOptions.None);
                        if (parts.Length == 2)
                        {
                            oldBpm = ParseDoubleToken(parts[0]);
                            newBpm = ParseDoubleToken(parts[1]);
                        }
                    }
                    else if (key.Equals("mergedReds", StringComparison.OrdinalIgnoreCase))
                    {
                        mergedReds = ParseIntToken(val);
                    }
                    else if (key.Equals("insertedGreens", StringComparison.OrdinalIgnoreCase))
                    {
                        insertedGreens = ParseIntToken(val);
                    }
                }

                if (segStart == long.MinValue || mul <= 1)
                    continue;

                markerSegs.Add(new SvaSegment
                {
                    StartTimeMs = segStart,
                    EndTimeMs = (segEnd == long.MaxValue) ? double.PositiveInfinity : segEnd,
                    Multiplier = mul,

                    // 既存フィールド互換（型に必須なら0でOK）
                    Bpm = 0,
                    RawSv = 0,

                    // new fields
                    InsertedGreens = insertedGreens,
                    MergedReds = mergedReds,
                    OldBpm = oldBpm,
                    NewBpm = newBpm,
                });
            }

            if (markerSegs.Count > 0)
            {
                // 重複除去・整列
                return markerSegs
                    .GroupBy(s => (long)Math.Round(s.StartTimeMs))
                    .Select(g => g.First())
                    .OrderBy(s => s.StartTimeMs)
                    .ToList();
            }

            // ============================================================
            // 2) Fallback: old pair-based detection (for older outputs)
            // ============================================================

            // 赤線（uninherited=1, beatLen>0）のみ収集
            var red = new List<(double time, double beatLen)>();
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

                if (u == 1 && bl > 0)
                    red.Add((t, bl));
            }

            if (red.Count == 0) return new List<SvaSegment>();

            // 同時刻グループ（旧SVAは同時刻に「元赤線＋倍化赤線」が入る前提）
            var byTime = red
                .GroupBy(r => (long)Math.Round(r.time))
                .OrderBy(g => g.Key)
                .ToList();

            var uniqueRedTimes = byTime.Select(g => (double)g.Key).OrderBy(x => x).ToList();

            int NearestPow2Multiplier(double ratio)
            {
                if (ratio < 1.0000001) return 1;
                int best = 1;
                double bestErr = double.MaxValue;

                for (int k = 1; k <= 30; k++)
                {
                    int m = 1 << k;
                    double err = Math.Abs(ratio - m) / m;
                    if (err < bestErr)
                    {
                        bestErr = err;
                        best = m;
                    }
                }
                if (bestErr > 1e-6) return 1;
                return best;
            }

            var segs = new List<SvaSegment>();

            foreach (var g in byTime)
            {
                var beats = g.Select(x => x.beatLen).ToList();
                if (beats.Count < 2) continue;

                double max = beats.Max();
                double min = beats.Min();
                if (min <= 0) continue;

                double ratio = max / min;
                int mul = NearestPow2Multiplier(ratio);
                if (mul <= 1) continue;

                double segStart = g.Key;

                double segEnd = double.PositiveInfinity;
                for (int i = 0; i < uniqueRedTimes.Count; i++)
                {
                    if (uniqueRedTimes[i] <= segStart) continue;
                    segEnd = uniqueRedTimes[i];
                    break;
                }

                segs.Add(new SvaSegment
                {
                    StartTimeMs = segStart,
                    EndTimeMs = segEnd,
                    Multiplier = mul,

                    Bpm = 0,
                    RawSv = 0,

                    // markerが無いので分からない（0）
                    InsertedGreens = 0,
                    MergedReds = 0,
                    OldBpm = 0,
                    NewBpm = 0,
                });
            }

            return segs
                .GroupBy(s => s.StartTimeMs)
                .Select(x => x.First())
                .OrderBy(s => s.StartTimeMs)
                .ToList();
        }
    }
}
