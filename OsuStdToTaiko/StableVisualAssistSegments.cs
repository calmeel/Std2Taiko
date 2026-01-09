using System.Globalization;

namespace OsuStdToTaiko
{
    public static partial class StableVisualAssist
    {
        /// Stable-Visual Assist (SVA)
        /// 検出専用フェーズ：
        ///  - rawSV > SV_MAX または rawSV < SV_MINとなる赤線区間を検出する
        ///  - TimingPoints は変更しない
        public struct SvaSegment  // Form1.cs から直接呼んでいるので、ここは public で OK
        {
            public double StartTimeMs;   // 赤線区間の開始
            public double EndTimeMs;     // 次の赤線まで（無ければ +inf）
            public double Bpm;           // 元の BPM
            public double RawSv;         // refSpeed / BPM
            public int Multiplier;       // 推奨 2^k
            public bool IsHighSv;        // true: rawSV > SV_MAX（BPM×2^n）, false: rawSV < SV_MIN（BPM÷2^n）
            public int InsertedGreens;
            public int MergedReds;
            public double OldBpm;
            public double NewBpm;

            public override string ToString()
            {
                string dir = IsHighSv ? "hi" : "lo";
                return $"t={StartTimeMs:0}..{(double.IsInfinity(EndTimeMs) ? -1 : EndTimeMs):0} " +
                       $"BPM={Bpm:0.###} rawSV={RawSv:0.###} {dir} x{Multiplier}";
            }
        }

        /// <summary>
        /// 指定時刻の SV 値（SliderMultiplier相当）を推定する。
        /// beatLen<0 の緑線が "SV" を決める。
        /// </summary>
        private static double GuessSvAt(double time, List<(double time, double beatLen)> greens, double baseSv = 1.0)
        {
            // 緑線が無いなら baseSv（=1.0）
            if (greens.Count == 0)
                return baseSv;

            double lastSv = baseSv;

            // 時刻順に走査して，time より前の最後の緑線を採用
            foreach (var g in greens)
            {
                if (g.time <= time)
                {
                    // SV = 100 / |beatLen|
                    lastSv = 100.0 / Math.Abs(g.beatLen);
                }
                else break;
            }

            return lastSv;
        }

        /// SVA対象区間を検出する（TimingPointsは未変更）
        /// osuText : constant speed 適用後の osu テキスト
        /// svMax : stable の SV 上限（例:10）
        /// svMin : stable の SV 下限（例:0.01）
        internal static List<SvaSegment> DetectSvaSegments(
            string osuText,
            double svMax,
            double svMin)
        {
            var inv = CultureInfo.InvariantCulture;
            var result = new List<SvaSegment>();

            var lines = osuText.Replace("\r\n", "\n").Split('\n').ToList();

            // [TimingPoints] 範囲取得
            int tpIdx = lines.FindIndex(l => l.Trim().Equals("[TimingPoints]", StringComparison.OrdinalIgnoreCase));
            if (tpIdx < 0) return result;

            int start = tpIdx + 1;
            int end = start;
            for (; end < lines.Count; end++)
            {
                var t = lines[end].Trim();
                if (t.StartsWith("[") && t.EndsWith("]"))
                    break;
            }

            // TimingPoints 解析
            var tp = new List<(double time, int uninherited, double beatLen)>();
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

                tp.Add((time, uninherited, beatLen));
            }

            // 緑線（uninherited=0, beatLen<0）を時刻順に抽出
            var greens = tp
                .Where(x => x.uninherited == 0 && x.beatLen < 0)
                .Select(x => (x.time, x.beatLen))
                .OrderBy(x => x.time)
                .ToList();

            // 赤線（uninherited=1, beatLen>0）を時刻順に抽出
            var reds = tp
                .Where(x => x.uninherited == 1 && x.beatLen > 0)
                .OrderBy(x => x.time)
                .ToList();

            if (reds.Count == 0)
                return result;

            // …（GuessSvAt / GetMostCommonBpm 等はそのまま）…

            double firstBeatLen = reds[0].beatLen;
            double firstBpm = 60000.0 / firstBeatLen;
            double firstSv = GuessSvAt(reds[0].time, greens);
            double refSpeed = firstBpm * firstSv;

            // 各赤線区間ごとに rawSV を計算
            for (int i = 0; i < reds.Count; i++)
            {
                double segStart = reds[i].time;
                double segEnd = (i + 1 < reds.Count) ? reds[i + 1].time : double.PositiveInfinity;

                double bpm = 60000.0 / reds[i].beatLen;
                if (!(bpm > 0)) continue;

                double rawSv = refSpeed / bpm;
                if (!(rawSv > 0) || double.IsNaN(rawSv) || double.IsInfinity(rawSv))
                    continue;

                // 高SV救済: rawSV > svMax
                if (rawSv > svMax)
                {
                    int mul = ComputeHighMultiplier(rawSv, svMax);
                    if (mul > 1)
                    {
                        result.Add(new SvaSegment
                        {
                            StartTimeMs = segStart,
                            EndTimeMs = segEnd,
                            Bpm = bpm,
                            RawSv = rawSv,
                            Multiplier = mul,
                            IsHighSv = true
                        });
                    }
                }
                // 低SV救済: rawSV < svMin
                else if (rawSv < svMin)
                {
                    int mul = ComputeLowMultiplier(rawSv, svMin);
                    if (mul > 1)
                    {
                        result.Add(new SvaSegment
                        {
                            StartTimeMs = segStart,
                            EndTimeMs = segEnd,
                            Bpm = bpm,
                            RawSv = rawSv,
                            Multiplier = mul,
                            IsHighSv = false
                        });
                    }
                }
            }

            return result;
        }

        /// rawSV / (2^k) <= svMax となる最小の 2^k を返す（高SV救済用）

        private static int ComputeHighMultiplier(double rawSv, double svMax)
        {
            if (!(rawSv > svMax))
                return 1;

            double need = rawSv / svMax;
            int m = 1;
            while (m < need && m < (1 << 30))
                m <<= 1;
            return m;
        }

        /// rawSV * (2^k) >= svMin となる最小の 2^k を返す（低SV救済用）
        private static int ComputeLowMultiplier(double rawSv, double svMin)
        {
            if (!(rawSv > 0) || double.IsNaN(rawSv) || double.IsInfinity(rawSv))
                return 1;

            if (!(rawSv < svMin))
                return 1;

            double need = svMin / rawSv;
            int m = 1;
            while (m < need && m < (1 << 30))
                m <<= 1;
            return m;
        }

    }
}
