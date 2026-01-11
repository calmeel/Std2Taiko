using System.Globalization;

namespace OsuStdToTaiko
{
    internal static class TimingDiagnostics
    {
        // [TimingPoints] の “生行” を抜き出すヘルパー
        private static List<string> ExtractTimingPointsBodyLines(string osuText)
        {
            var lines = osuText.Replace("\r\n", "\n").Split('\n').ToList();
            int idx = lines.FindIndex(l => l.Trim().Equals("[TimingPoints]", StringComparison.OrdinalIgnoreCase));
            var body = new List<string>();
            if (idx < 0) return body;

            for (int i = idx + 1; i < lines.Count; i++)
            {
                string raw = lines[i];
                string t = raw.Trim();
                if (t.StartsWith("[") && t.EndsWith("]")) break;
                if (t.Length == 0 || t.StartsWith("//")) continue;

                // 行末の空白差などを潰して比較しやすくする（内容は変えない）
                body.Add(string.Join(",", raw.Split(',').Select(s => s.Trim())));
            }
            return body;
        }

        // “入力 vs エンコード結果” を比較して差分を出すログ関数
        internal static void LogTimingPointsDiff(string tag, string inputOsuText, string encodedOsuText, int maxList = 30)
        {
            var inBody = ExtractTimingPointsBodyLines(inputOsuText);
            var encBody = ExtractTimingPointsBodyLines(encodedOsuText);

            Console.WriteLine($"[TimingDiff:{tag}] inputLines={inBody.Count} encodedLines={encBody.Count}");

            // セット比較（行が丸ごと消えた/増えた）
            var inSet = new HashSet<string>(inBody);
            var encSet = new HashSet<string>(encBody);

            var onlyIn = inSet.Except(encSet).OrderBy(x => x).ToList();
            var onlyEnc = encSet.Except(inSet).OrderBy(x => x).ToList();

            Console.WriteLine($"[TimingDiff:{tag}] onlyInInput={onlyIn.Count} onlyInEncoded={onlyEnc.Count}");

            // 赤線/緑線の数（符号で雑に分類。timingChange列はここでは見ない）
            int inRed = inBody.Count(l => TryParseBeatLen(l, out var bl) && bl > 0);
            int inGreen = inBody.Count(l => TryParseBeatLen(l, out var bl) && bl < 0);
            int encRed = encBody.Count(l => TryParseBeatLen(l, out var bl) && bl > 0);
            int encGreen = encBody.Count(l => TryParseBeatLen(l, out var bl) && bl < 0);

            Console.WriteLine($"[TimingDiff:{tag}] input red={inRed} green={inGreen} | encoded red={encRed} green={encGreen}");

            // 代表的な差分を先頭だけ表示
            if (onlyIn.Count > 0)
            {
                Console.WriteLine($"[TimingDiff:{tag}] --- lines only in INPUT (first {Math.Min(maxList, onlyIn.Count)}) ---");
                for (int i = 0; i < Math.Min(maxList, onlyIn.Count); i++)
                    Console.WriteLine($"[TimingDiff:{tag}] IN  {onlyIn[i]}");
            }

            if (onlyEnc.Count > 0)
            {
                Console.WriteLine($"[TimingDiff:{tag}] --- lines only in ENCODED (first {Math.Min(maxList, onlyEnc.Count)}) ---");
                for (int i = 0; i < Math.Min(maxList, onlyEnc.Count); i++)
                    Console.WriteLine($"[TimingDiff:{tag}] ENC {onlyEnc[i]}");
            }

            // 数値比較（同じ時刻の赤線/緑線で beatLen が微妙に変わっているか）
            // time, beatLen だけを見る簡易版（7列目timingChange等は比較対象外）
            var inPairs = ExtractTimeBeatPairs(inputOsuText);
            var encPairs = ExtractTimeBeatPairs(encodedOsuText);

            int changed = 0;
            foreach (var key in inPairs.Keys)
            {
                if (!encPairs.TryGetValue(key, out var blEnc)) continue;
                var blIn = inPairs[key];
                if (Math.Abs(blIn - blEnc) > 1e-6)
                {
                    if (changed < maxList)
                        Console.WriteLine($"[TimingDiff:{tag}] CHG t={key} bl input={blIn:R} encoded={blEnc:R}");
                    changed++;
                }
            }
            Console.WriteLine($"[TimingDiff:{tag}] numericChanged(time+sign key)={changed}");

            // --- local helper（ローカル関数は OK） ---
            static bool TryParseBeatLen(string line, out double bl)
            {
                bl = 0;
                var p = line.Split(',');
                if (p.Length < 2) return false;
                return double.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out bl);
            }
        }

        // 同じ時刻に赤と緑があるので、キーは (time, sign) にする（赤/緑別々に追える）
        private static Dictionary<(double time, int sign), double> ExtractTimeBeatPairs(string osuText)
        {
            var body = ExtractTimingPointsBodyLines(osuText);
            var dict = new Dictionary<(double time, int sign), double>();

            foreach (var l in body)
            {
                var p = l.Split(',');
                if (p.Length < 2) continue;

                if (!double.TryParse(p[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var tm)) continue;
                if (!double.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var bl)) continue;

                int sign = bl > 0 ? 1 : (bl < 0 ? -1 : 0);
                dict[(tm, sign)] = bl; // 同一キーがあれば後勝ち
            }

            return dict;
        }
    }
}
