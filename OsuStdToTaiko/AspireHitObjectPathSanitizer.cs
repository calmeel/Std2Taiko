using System.Globalization;

namespace OsuStdToTaiko
{
    internal static class AspireHitObjectPathSanitizer
    {
        public static string ClampSliderControlPoints(string osuText, out int fixedCount)
        {
            fixedCount = 0;
            if (string.IsNullOrEmpty(osuText)) return osuText ?? string.Empty;

            string nl = osuText.Contains("\r\n") ? "\r\n" : "\n";
            var lines = osuText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

            int hitObjectsStart = -1;
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Trim() == "[HitObjects]") { hitObjectsStart = i; break; }
            }
            if (hitObjectsStart < 0) return osuText;

            for (int i = hitObjectsStart + 1; i < lines.Length; i++)
            {
                string raw = lines[i];
                string t = raw.Trim();
                if (t.StartsWith("[") && t.EndsWith("]")) break;
                if (string.IsNullOrWhiteSpace(t) || t.StartsWith("//")) continue;

                string[] parts = raw.Split(',');
                if (parts.Length < 6) continue;

                if (!int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int type))
                    continue;

                bool isSlider = (type & 2) != 0;
                if (!isSlider) continue;

                string curve = parts[5];
                int pipe = curve.IndexOf('|');
                if (pipe < 0) continue; // 制御点なしは別処理済み

                // 形式: "B|x:y|x:y|..."
                string head = curve.Substring(0, pipe);
                string rest = curve.Substring(pipe + 1);
                var nodes = rest.Split('|');

                bool changed = false;
                for (int n = 0; n < nodes.Length; n++)
                {
                    int colon = nodes[n].IndexOf(':');
                    if (colon <= 0) continue;

                    if (!int.TryParse(nodes[n].Substring(0, colon), NumberStyles.Integer, CultureInfo.InvariantCulture, out int x)) continue;
                    if (!int.TryParse(nodes[n].Substring(colon + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int y)) continue;

                    int cx = Clamp(x, 0, 512);
                    int cy = Clamp(y, 0, 384);

                    if (cx != x || cy != y)
                    {
                        nodes[n] = $"{cx}:{cy}";
                        changed = true;
                    }
                }

                if (changed)
                {
                    parts[5] = head + "|" + string.Join("|", nodes);
                    lines[i] = string.Join(",", parts);
                    fixedCount++;
                }
            }

            return string.Join(nl, lines);
        }

        private static int Clamp(int v, int lo, int hi)
        {
            if (v < lo) return lo;
            if (v > hi) return hi;
            return v;
        }

        public static string SimplifyExtremeSlidersForDecode(string osuText, out int simplifiedCount)
        {
            simplifiedCount = 0;
            if (string.IsNullOrEmpty(osuText)) return osuText ?? string.Empty;

            string nl = osuText.Contains("\r\n") ? "\r\n" : "\n";
            var lines = osuText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

            int hitObjectsStart = -1;
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Trim() == "[HitObjects]") { hitObjectsStart = i; break; }
            }
            if (hitObjectsStart < 0) return osuText;

            for (int i = hitObjectsStart + 1; i < lines.Length; i++)
            {
                string raw = lines[i];
                string t = raw.Trim();
                if (t.StartsWith("[") && t.EndsWith("]")) break;
                if (string.IsNullOrWhiteSpace(t) || t.StartsWith("//")) continue;

                string[] parts = raw.Split(',');
                if (parts.Length < 8) continue;

                // slider?
                if (!int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int type))
                    continue;
                bool isSlider = (type & 2) != 0;
                if (!isSlider) continue;

                // pixelLength
                double pxLen = 0;
                bool pxOk = double.TryParse(parts[7], NumberStyles.Float, CultureInfo.InvariantCulture, out pxLen);

                // control point count (curve field)
                string curve = parts[5];
                int pipe = curve.IndexOf('|');
                int cpCount = 0;
                if (pipe >= 0)
                {
                    // "B|x:y|x:y|..." なので '|' の個数で概算
                    for (int k = 0; k < curve.Length; k++) if (curve[k] == '|') cpCount++;
                    // cpCount は「区切り数」なので制御点数は概ね cpCount
                }

                // ★閾値：Aspireのヤバいスライダーを潰す
                bool tooLong = (!pxOk) || (pxLen > 50000);   // 145125 を確実に捕まえる
                bool tooMany = (cpCount > 200);              // 制御点が多すぎる
                if (!tooLong && !tooMany) continue;

                // x,y（開始座標）
                int x = 0, y = 0;
                int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out x);
                int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out y);

                // 最小形状：L|x+1:y（1pxずらし） & pixelLength=1
                int x2 = (x >= 512) ? x - 1 : x + 1;
                x2 = Clamp(x2, 0, 512);
                y = Clamp(y, 0, 384);

                parts[5] = $"L|{x2}:{y}";
                parts[6] = "1"; // repeats
                parts[7] = "1"; // pixelLength

                lines[i] = string.Join(",", parts);
                simplifiedCount++;
            }

            return string.Join(nl, lines);
        }

    }
}
