using System.Globalization;

namespace OsuStdToTaiko
{
    public static class TimingPointParser
    {
        // .osu ファイルの TimingPoints を読み込む
        public static List<(double time, double beatLen, int uninherited)> ParseTimingPointsFromOsuText(string osuText)
        {
            var lines = osuText.Replace("\r\n", "\n").Split('\n').ToList();
            int idxTiming = lines.FindIndex(l => l.Trim().Equals("[TimingPoints]", StringComparison.OrdinalIgnoreCase));
            if (idxTiming < 0) return new List<(double time, double beatLen, int uninherited)>();

            var timing = new List<(double time, double beatLen, int uninherited)>();

            for (int i = idxTiming + 1; i < lines.Count; i++)
            {
                var t = lines[i].Trim();
                if (t.StartsWith("[") && t.EndsWith("]")) break;
                if (t.Length == 0 || t.StartsWith("//")) continue;

                var p = t.Split(',');
                if (p.Length < 2) continue;

                // time
                if (!double.TryParse(p[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var tm))
                    continue;

                // beatLength
                if (!double.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var bl))
                    continue;

                // ★ NaN/Infinity は破棄（stableはだいたい無視する方向なので合わせる）
                if (!double.IsFinite(bl))
                    continue;

                // uninherited（列7）...
                int uninherited = 1;
                if (p.Length >= 7)
                {
                    if (!int.TryParse(p[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out uninherited))
                        uninherited = 1;
                }

                // ★ 用途別の最低限バリデーション
                // 赤線(1): beatLength は正であるべき
                if (uninherited == 1)
                {
                    if (bl <= 0)
                        continue;

                    // ★ “9.8E+304”級だけは実務上無視（計算が壊れる）
                    // 653617ms みたいな値は通したいので閾値はかなり上に置く
                    if (bl > 1e12) // ←ここは好みだが、653kは余裕で通る
                        continue;
                }
                else
                {
                    // 緑線(0): 通常は負（SV）。0や正は壊れ扱いで無視
                    if (bl >= 0)
                        continue;
                }

                timing.Add((tm, bl, uninherited));
            }

            // time順。同時刻なら 赤線(uninherited=1) を先に
            timing.Sort((a, b) =>
            {
                int c = a.time.CompareTo(b.time);
                if (c != 0) return c;

                // 赤線優先
                if (a.uninherited == 1 && b.uninherited == 0) return -1;
                if (a.uninherited == 0 && b.uninherited == 1) return 1;
                return 0;
            });

            return timing;
        }
    }
}
