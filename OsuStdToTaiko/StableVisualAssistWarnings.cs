using System.Globalization;

namespace OsuStdToTaiko
{
    public static partial class StableVisualAssist
    {
        internal sealed class SvaWarning
        {
            public double SegStart { get; init; }
            public double SegEnd { get; init; }
            public int Multiplier { get; init; }

            public int LongObjectCount { get; init; }
            public List<string> Samples { get; init; } = new();
        }

        /// <summary>
        /// SVA区間内に「長さ依存（保持）オブジェクト」が残っているか検出して警告を返す。
        /// ここでは .osu テキストの [HitObjects] を直接解析する（実装への依存を避ける）。
        /// </summary>
        internal static List<SvaWarning> DetectLongObjectsInSvaSegments(string osuText, List<SvaSegment> segments, int sampleLimit = 5)
        {
            var inv = CultureInfo.InvariantCulture;
            var warnings = new List<SvaWarning>();
            if (segments == null || segments.Count == 0)
                return warnings;

            var lines = osuText.Replace("\r\n", "\n").Split('\n');

            // [HitObjects] 範囲
            int hoIdx = Array.FindIndex(lines, l => l.Trim().Equals("[HitObjects]", StringComparison.OrdinalIgnoreCase));
            if (hoIdx < 0) return warnings;

            int start = hoIdx + 1;
            int end = start;
            for (; end < lines.Length; end++)
            {
                var t = lines[end].Trim();
                if (t.StartsWith("[") && t.EndsWith("]"))
                    break;
            }

            // type bit: 2=slider, 8=spinner, 128=hold (mania)
            bool IsLengthDependent(int type) => (type & 2) != 0 || (type & 8) != 0 || (type & 128) != 0;

            // HitObjects: startTime/type と（可能なら）endTime を持つ
            var objects = new List<(double startTime, double endTime, int type, string raw)>();

            for (int i = start; i < end; i++)
            {
                var line = lines[i];
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("//"))
                    continue;

                var p = line.Split(',');
                if (p.Length < 4) continue;

                if (!double.TryParse(p[2], NumberStyles.Float, inv, out var t0)) continue;
                if (!int.TryParse(p[3], NumberStyles.Integer, inv, out var type)) continue;

                if (!IsLengthDependent(type))
                    continue;

                // endTime 推定：
                // 1) スライダー/保持系で、末尾のフィールドが数値ならそれを endTime とみなす（あなたの環境で出ていた形式に対応）
                // 2) 取れなければ endTime = startTime（交差判定の精度は落ちるが見逃しは減る）
                double t1 = t0;

                // 末尾側から「数値として読めるフィールド」を探す（安全のため最大3回程度）
                // 例: "... ,1,145125" → 145125
                for (int k = p.Length - 1; k >= Math.Max(0, p.Length - 4); k--)
                {
                    if (double.TryParse(p[k], NumberStyles.Float, inv, out var cand))
                    {
                        // startTime より後なら endTime として採用
                        if (cand >= t0)
                        {
                            t1 = cand;
                            break;
                        }
                    }
                }

                objects.Add((t0, t1, type, line));
            }

            foreach (var seg in segments)
            {
                if (seg.Multiplier <= 1) continue;

                double segStart = seg.StartTimeMs;
                double segEnd = seg.EndTimeMs;

                // overlap 判定: objStart < segEnd && objEnd > segStart
                var hits = objects.Where(o =>
                    (double.IsInfinity(segEnd) || o.startTime < segEnd) &&
                    o.endTime > segStart
                ).ToList();

                if (hits.Count == 0)
                    continue;

                var w = new SvaWarning
                {
                    SegStart = segStart,
                    SegEnd = segEnd,
                    Multiplier = seg.Multiplier,
                    LongObjectCount = hits.Count
                };

                foreach (var h in hits.Take(sampleLimit))
                    w.Samples.Add(h.raw);

                warnings.Add(w);
            }

            return warnings;
        }
        internal static void PrintSvaWarningsToConsole(List<SvaWarning> warnings)
        {
            if (warnings == null || warnings.Count == 0)
                return;

            Console.WriteLine("[SVA] WARNING: length-dependent objects exist inside SVA segments.");
            foreach (var w in warnings)
            {
                string endStr = double.IsInfinity(w.SegEnd) ? "INF" : w.SegEnd.ToString("0", CultureInfo.InvariantCulture);

                Console.WriteLine($"[SVA] segment t={w.SegStart:0}..{endStr} x{w.Multiplier} longObjects={w.LongObjectCount}");
                foreach (var s in w.Samples)
                    Console.WriteLine($"[SVA]   sample: {s}");

                Console.WriteLine("[SVA]   note: In Stable-Visual Assist, BPM-doubling may change tick placement/feel for remaining sliders/drumrolls. Consider splitting or accept visual compromise (aspire).");
            }
        }


        // 緑線エフェクト警告
        public sealed class SvaEffectWarning
        {
            public double SegStart { get; init; }
            public double SegEnd { get; init; }
            public int Multiplier { get; init; }

            public int GreenlineCount { get; init; }
            public int InsertedGreenlineCount { get; init; } // 0なら未使用
            public List<string> Samples { get; init; } = new();
        }

        /// <summary>
        /// SVA区間内に「緑線が複数ある（SV演出がある）」場合に警告を返す。
        /// さらに「緑線補完が発生した」場合も通知できるようにする（insertedCountは任意）。
        /// </summary>
        public static List<SvaEffectWarning> DetectGreenlineEffectWarnings(
            string osuText,
            List<SvaSegment> segments,
            int multiplierHint,
            int insertedGreenlineCount = 0,
            int sampleLimit = 6)
        {
            var inv = CultureInfo.InvariantCulture;
            var warns = new List<SvaEffectWarning>();
            if (segments == null || segments.Count == 0)
                return warns;

            var lines = osuText.Replace("\r\n", "\n").Split('\n').ToList();

            // [TimingPoints] 範囲
            int tpIdx = lines.FindIndex(l => l.Trim().Equals("[TimingPoints]", StringComparison.OrdinalIgnoreCase));
            if (tpIdx < 0) return warns;

            int start = tpIdx + 1;
            int end = start;
            for (; end < lines.Count; end++)
            {
                var t = lines[end].Trim();
                if (t.StartsWith("[") && t.EndsWith("]"))
                    break;
            }

            // TimingPoints をパースして保持（time, beatLen, uninherited, rawline）
            var tps = new List<(double time, double beatLen, int uninherited, string raw)>();
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

                tps.Add((time, beatLen, uninherited, line));
            }

            foreach (var seg in segments)
            {
                if (seg.Multiplier <= 1) continue;

                double segStart = seg.StartTimeMs;
                double segEnd = seg.EndTimeMs;

                // 区間内の緑線（uninherited=0, beatLen<0）
                var greens = tps.Where(tp =>
                    tp.uninherited == 0 &&
                    tp.beatLen < 0 &&
                    tp.time >= segStart &&
                    (double.IsInfinity(segEnd) || tp.time < segEnd)
                ).OrderBy(tp => tp.time).ToList();

                // ★置換方式では insertedGreenlineCount 引数ではなく seg 側の値を使う
                int inserted = seg.InsertedGreens;

                // 緑線が2本以上 → SV演出がある（補正により演出が変わる）
                // または、緑線補完が発生した → 注意（見た目の演出・体感差が出うる）
                if (greens.Count >= 2 || inserted > 0)
                {
                    var w = new SvaEffectWarning
                    {
                        SegStart = segStart,
                        SegEnd = segEnd,
                        Multiplier = seg.Multiplier,
                        GreenlineCount = greens.Count,
                        InsertedGreenlineCount = inserted
                    };

                    foreach (var g in greens.Take(sampleLimit))
                        w.Samples.Add(g.raw);

                    warns.Add(w);
                }
            }

            return warns;
        }
        public static void PrintGreenlineEffectWarningsToConsole(List<SvaEffectWarning> warns)
        {
            if (warns == null || warns.Count == 0)
                return;

            Console.WriteLine("[SVA] NOTE: greenline (SV effect) warnings:");
            foreach (var w in warns)
            {
                string endStr = double.IsInfinity(w.SegEnd) ? "INF" : w.SegEnd.ToString("0", CultureInfo.InvariantCulture);

                Console.WriteLine($"[SVA] segment t={w.SegStart:0}..{endStr} x{w.Multiplier} greenlines={w.GreenlineCount} inserted={w.InsertedGreenlineCount}");
                Console.WriteLine("[SVA]   note: SVA scales greenline beatLength by xMultiplier to match the doubled redline; SV effects inside this segment will change visually.");
                foreach (var s in w.Samples)
                    Console.WriteLine($"[SVA]   green: {s}");
            }
        }
    }
}
