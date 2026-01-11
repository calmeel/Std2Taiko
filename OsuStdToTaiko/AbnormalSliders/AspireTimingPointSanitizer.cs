using System.Globalization;

namespace OsuStdToTaiko
{
    internal static class AspireTimingPointSanitizer
    {
        // TimingPoints の meter(3列目) が 0以下なら 4 に補正する
        public static string SanitizeTimingPointsMeter(string osuText, out int fixedCount)
        {
            fixedCount = 0;
            if (string.IsNullOrEmpty(osuText)) return osuText ?? string.Empty;

            string nl = osuText.Contains("\r\n") ? "\r\n" : "\n";
            var lines = osuText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

            int start = -1;
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Trim() == "[TimingPoints]")
                {
                    start = i;
                    break;
                }
            }
            if (start < 0) return osuText;

            for (int i = start + 1; i < lines.Length; i++)
            {
                string raw = lines[i];
                string t = raw.Trim();
                if (t.StartsWith("[") && t.EndsWith("]")) break;
                if (string.IsNullOrWhiteSpace(t)) continue;
                if (t.StartsWith("//")) continue;

                string[] parts = raw.Split(',');
                if (parts.Length < 8) continue;

                // meter = parts[2]
                if (!int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int meter))
                    continue;

                if (meter <= 0)
                {
                    parts[2] = "4";
                    lines[i] = string.Join(",", parts);
                    fixedCount++;
                }
            }

            return string.Join(nl, lines);
        }

        public static string SanitizeTimingPointsBeatLengthFloatRange(string osuText, out int fixedCount)
        {
            fixedCount = 0;
            if (string.IsNullOrEmpty(osuText)) return osuText ?? string.Empty;

            string nl = osuText.Contains("\r\n") ? "\r\n" : "\n";
            var lines = osuText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

            int start = -1;
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Trim() == "[TimingPoints]") { start = i; break; }
            }
            if (start < 0) return osuText;

            const double FLOAT_MAX = 1.0e7;    // 1.0e20では大きすぎて通らなかったので適当に下げた
            const double FLOAT_MIN_POS = 1.0e-30; // 0や極小で壊れないよう下限

            for (int i = start + 1; i < lines.Length; i++)
            {
                string raw = lines[i];
                string t = raw.Trim();
                if (t.StartsWith("[") && t.EndsWith("]")) break;
                if (string.IsNullOrWhiteSpace(t) || t.StartsWith("//")) continue;

                string[] parts = raw.Split(',');
                if (parts.Length < 8) continue;

                // beatLength = parts[1]
                if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double bl))
                    continue;

                double abs = Math.Abs(bl);
                if (abs > FLOAT_MAX)
                {
                    parts[1] = (Math.Sign(bl) * FLOAT_MAX).ToString("G17", CultureInfo.InvariantCulture);
                    lines[i] = string.Join(",", parts);
                    fixedCount++;
                }
                else if (abs > 0 && abs < FLOAT_MIN_POS)
                {
                    // 0に近すぎると計算で爆発しやすいので少し持ち上げる
                    parts[1] = (Math.Sign(bl) * FLOAT_MIN_POS).ToString("G17", CultureInfo.InvariantCulture);
                    lines[i] = string.Join(",", parts);
                    fixedCount++;
                }
            }

            return string.Join(nl, lines);
        }

    }
}
