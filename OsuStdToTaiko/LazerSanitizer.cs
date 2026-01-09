using System.Globalization;

namespace OsuStdToTaiko
{
    internal static class LazerSanitizer
    {
        // lazer-safe 用：TimingPoints の「致命的に読めない値」だけを最小限で正規化する
        // ※ stable 向け（ギミック保持）とは目的が異なるため、LAZER_SAFE_OUTPUT==true のときだけ適用する
        internal static string SanitizeTimingPointsForLazer(string timingText)
        {
            var inv = CultureInfo.InvariantCulture;

            var lines = timingText.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
            var outLines = new List<string>(lines.Length);

            bool inTiming = false;

            foreach (var raw in lines)
            {
                string line = raw;
                string t = line.Trim();

                if (t.Equals("[TimingPoints]", StringComparison.OrdinalIgnoreCase))
                {
                    inTiming = true;
                    outLines.Add(line);
                    continue;
                }

                if (inTiming && t.StartsWith("[") && t.EndsWith("]"))
                    inTiming = false;

                if (!inTiming || t.Length == 0 || t.StartsWith("//"))
                {
                    outLines.Add(line);
                    continue;
                }

                var parts = t.Split(',');
                if (parts.Length < 8)
                {
                    outLines.Add(line);
                    continue;
                }

                // meter（列3）は lazer では 1 以上必須。Aspire では 0/負があり得るので出力時のみ最低限正規化する
                if (int.TryParse(parts[2], NumberStyles.Integer, inv, out int meter) && meter <= 0)
                    parts[2] = "4";

                // uninherited（列7）
                if (!int.TryParse(parts[6], NumberStyles.Integer, inv, out int uninherited))
                {
                    outLines.Add(line);
                    continue;
                }

                // beatLength（列2）
                if (!double.TryParse(parts[1], NumberStyles.Float, inv, out double beatLen))
                {
                    outLines.Add(line);
                    continue;
                }

                // lazer が確実に読めない NaN / ±Infinity は削除
                if (double.IsNaN(beatLen) || double.IsInfinity(beatLen))
                    continue;

                if (uninherited == 1)
                {
                    // ■ lazer-safe: 赤線は「有限・正」で [6,60000] に収める
                    //   ※ stable 出力では極端値を維持するが、lazer は巨大値で落ちるため別モードで救済する
                    beatLen = Math.Abs(beatLen);

                    if (beatLen < 6.0) beatLen = 6.0;
                    if (beatLen > 60000.0) beatLen = 60000.0;

                    parts[1] = beatLen.ToString("G17", inv);
                    outLines.Add(string.Join(",", parts));
                    continue;
                }

                // 緑線は既存の SV クランプ（ClampSvInTimingPoints）で処理済みを前提に、ここでは追加の強制変換はしない
                outLines.Add(string.Join(",", parts));
            }

            return string.Join("\n", outLines).Replace("\n", "\r\n");
        }

        // lazer-safe 用：HitObjects の「極端な slider length」だけを最小限で救済する
        // ※ taiko 変換後は slider が残らないのが基本だが、Aspire などで残った場合に備える
        internal static string SanitizeHitObjectsForLazer(string osuText)
        {
            const double MAX_SLIDER_LENGTH = 65536.0; // lazer 側で上限に引っかかりやすい値（Overflow/Value too high 対策）

            var inv = CultureInfo.InvariantCulture;
            var lines = osuText.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n').ToList();
            int idxHit = lines.FindIndex(l => l.Trim().Equals("[HitObjects]", StringComparison.OrdinalIgnoreCase));
            if (idxHit < 0) return osuText;

            for (int i = idxHit + 1; i < lines.Count; i++)
            {
                var raw = lines[i];
                var t = raw.Trim();
                if (t.StartsWith("[") && t.EndsWith("]")) break;
                if (t.Length == 0 || t.StartsWith("//")) continue;

                var parts = t.Split(',');
                if (parts.Length < 8) continue;

                // type のビット 2 が立っていればスライダー
                if (!int.TryParse(parts[3], NumberStyles.Integer, inv, out int type))
                    continue;

                bool isSlider = (type & 2) != 0;
                if (!isSlider) continue;

                // standard slider: ... , repeatCount, pixelLength, ...
                // parts[7] が pixelLength
                if (!double.TryParse(parts[7], NumberStyles.Float, inv, out double px))
                    continue;

                if (double.IsNaN(px) || double.IsInfinity(px))
                {
                    // 壊れ値は slider 自体を削除（lazer が読めないため）
                    lines[i] = "";
                    continue;
                }

                if (px > MAX_SLIDER_LENGTH)
                {
                    px = MAX_SLIDER_LENGTH;
                    parts[7] = px.ToString("G17", inv);
                    lines[i] = string.Join(",", parts);
                }
            }

            return string.Join("\n", lines).Replace("\n", "\r\n");
        }

    }
}
