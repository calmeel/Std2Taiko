using System.Globalization;

namespace OsuStdToTaiko
{
    /// <summary>
    /// Aspire譜面など、osu!stableが許容してしまう「文法的に壊れた slider」を
    /// 変換コアに入る前に最小限で正規化し、stable/lazer双方で開ける形に寄せる。
    ///
    /// 対象（現状）:
    /// - curve が "C" / "L" など単体で "|" 区切りの制御点が無い slider（例: "... ,C,1,52.5"）
    /// - pixelLength <= 0（例: -1）
    ///
    /// 方針:
    /// - 制御点が無い場合は 1点だけ補う（始点から1pxずらした点）
    /// - pixelLength <= 0 は 001 にクランプ
    /// - ★ 本来の stable が何にクランプしているかわからないので、Tick 生成・Autoが叩くかどうか、あたりの挙動が stable と異なる可能性あり
    /// </summary>
    internal static class AspireSliderSanitizer
    {
        internal readonly struct Report
        {
            public readonly int LinesTouched;
            public readonly int FixedMissingControlPoints;
            public readonly int FixedNonPositivePixelLength;

            // ★ サニタイズした slider を splitter 側で分解しないためのキー
            public readonly HashSet<int> NoSplitStartTimes;

            public readonly List<string> Samples;

            public Report(
                int linesTouched,
                int fixedMissing,
                int fixedLen,
                HashSet<int> noSplitStartTimes,
                List<string> samples)
            {
                LinesTouched = linesTouched;
                FixedMissingControlPoints = fixedMissing;
                FixedNonPositivePixelLength = fixedLen;

                NoSplitStartTimes = noSplitStartTimes;
                Samples = samples;
            }

            public bool HasAnyFix => LinesTouched > 0;
        }

        /// <summary>
        /// [HitObjects] 内の「sliderのみ」最小サニタイズ。
        /// </summary>
        public static string SanitizeHitObjects(string osuText, out Report report, int sampleLimit = 8)
        {
            if (string.IsNullOrEmpty(osuText))
            {
                report = new Report(0, 0, 0, new HashSet<int>(), new List<string>());
                return osuText ?? string.Empty;
            }

            // 改行コードを保持
            string nl = osuText.Contains("\r\n") ? "\r\n" : "\n";
            string[] lines = osuText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

            int hitObjectsStart = -1;
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Trim() == "[HitObjects]")
                {
                    hitObjectsStart = i;
                    break;
                }
            }

            if (hitObjectsStart < 0)
            {
                report = new Report(0, 0, 0, new HashSet<int>(), new List<string>());
                return osuText;
            }

            int linesTouched = 0;
            int fixedMissing = 0;
            int fixedLen = 0;

            // ★追加：サニタイズした slider を splitter 側で分解しないためのキー集合
            var noSplitStartTimes = new HashSet<int>();

            var samples = new List<string>(Math.Min(sampleLimit, 8));

            // [HitObjects] の次行から、次セクションまで
            for (int i = hitObjectsStart + 1; i < lines.Length; i++)
            {
                string raw = lines[i];

                // 次セクション
                string t = raw.Trim();
                if (t.StartsWith("[") && t.EndsWith("]")) break;

                if (string.IsNullOrWhiteSpace(t)) continue;
                if (t.StartsWith("//")) continue;

                // 期待: x,y,time,type,hitSound,curve,repeats,pixelLength,...
                // slider判定: typeビット 2 (slider)
                string[] parts = raw.Split(',');
                if (parts.Length < 8) continue;

                if (!int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int type)) continue;
                bool isSlider = (type & 2) != 0;
                if (!isSlider) continue;

                bool changed = false;
                bool fixedMissingHere = false; // この slider で制御点補完をしたか
                bool fixedLenHere = false; // この slider で pixelLength を救済したか

                // 座標
                int x = 0, y = 0;
                int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out x);
                int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out y);

                // curve
                string curve = parts[5].Trim();
                bool hasPipe = curve.Contains("|");

                if (!hasPipe)
                {
                    // "C" / "L" / "B" / "P" など単体を想定
                    // 1pxずらした点を補う（0..512/384にクランプ）
                    int x2;
                    int y2 = y;

                    // 右端付近は左にずらす
                    if (x >= 512) x2 = x - 1;
                    else x2 = x + 1;

                    x2 = Clamp(x2, 0, 512);
                    y2 = Clamp(y2, 0, 384);

                    parts[5] = $"{curve}|{x2}:{y2}";
                    fixedMissing++;
                    fixedMissingHere = true;
                    changed = true;
                }

                // parts[7] が pixelLength。Aspireでは "-1" など
                if (!double.TryParse(parts[7], NumberStyles.Float, CultureInfo.InvariantCulture, out double pxLen))
                {
                    parts[7] = "0.1";
                    fixedLen++;
                    fixedLenHere = true;
                    changed = true;
                }
                else if (pxLen <= 0)
                {
                    parts[7] = "0.1";
                    fixedLen++;
                    fixedLenHere = true;
                    changed = true;
                }

                if (changed)
                {
                    linesTouched++;

                    // ★この slider を "分解禁止" にするのは pixelLength を救済した場合のみ
                    if (fixedLenHere &&
                        int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int tMs))
                    {
                        noSplitStartTimes.Add(tMs);
                    }

                    if (samples.Count < sampleLimit)
                        samples.Add($"t={parts[2]}: {raw}  ==>  {string.Join(",", parts)}");

                    lines[i] = string.Join(",", parts);
                }


            }

            report = new Report(linesTouched, fixedMissing, fixedLen, noSplitStartTimes, samples);
            return string.Join(nl, lines);
        }

        private static int Clamp(int v, int lo, int hi)
        {
            if (v < lo) return lo;
            if (v > hi) return hi;
            return v;
        }
    }
}
