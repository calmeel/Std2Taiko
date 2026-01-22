using OsuStdToTaiko;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OsuStdToTaikoGui
{
    internal static class OsuFileHelpers
    {
        // 標準譜面判定ヘルパ
        internal static int GetGeneralModeOrDefault(string osuText)
        {
            // [General] セクション中の "Mode:" を探す。無ければ 0 扱い（standard）
            bool inGeneral = false;

            foreach (var raw in osuText.Replace("\r\n", "\n").Split('\n'))
            {
                var line = raw.Trim();

                if (line.Length == 0 || line.StartsWith("//"))
                    continue;

                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    inGeneral = line.Equals("[General]", StringComparison.OrdinalIgnoreCase);
                    continue;
                }

                if (!inGeneral) continue;

                if (line.StartsWith("Mode:", StringComparison.OrdinalIgnoreCase))
                {
                    var v = line.Substring("Mode:".Length).Trim();
                    if (int.TryParse(v, out int m))
                        return m;
                    return -1; // 壊れてる
                }
            }

            return 0;
        }

        // 命名関数
        internal static string MakeTaikoOutputFileName(string inputFileName, bool constantSpeed, bool adjusted)
        {
            var name = Path.GetFileNameWithoutExtension(inputFileName);

            // suffix の決定は Core 側へ
            string suffix = OsuStdToTaiko.OutputNaming.BuildTaikoFileNameSuffix(constantSpeed, adjusted);

            int idx = name.LastIndexOf(']');
            if (idx >= 0) name = name.Insert(idx, suffix);
            else name += suffix;

            return name + ".osu";
        }


        // Suffix ヘルパー
        internal static string? TryReadMetadataVersion(string osuText)
        {
            bool inMeta = false;

            foreach (var raw in osuText.Replace("\r\n", "\n").Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("//")) continue;

                if (line.Equals("[Metadata]", StringComparison.OrdinalIgnoreCase))
                {
                    inMeta = true;
                    continue;
                }

                if (inMeta && line.StartsWith("[") && line.EndsWith("]"))
                    break;

                if (inMeta && line.StartsWith("Version:", StringComparison.OrdinalIgnoreCase))
                    return line.Substring("Version:".Length).Trim();
            }

            return null;
        }

        // "Artist - Title (Mapper) [Diff].osu" の [Diff] 部分だけを version に置換する
        internal static string ReplaceBracketDifficulty(string inputFileName, string version)
        {
            string name = Path.GetFileNameWithoutExtension(inputFileName);
            int lb = name.LastIndexOf('[');
            int rb = name.LastIndexOf(']');

            if (lb >= 0 && rb > lb)
            {
                name = name.Substring(0, lb + 1) + version + name.Substring(rb);
            }
            else
            {
                // 角括弧が無い場合は末尾に付ける（保険）
                name = name + " [" + version + "]";
            }

            return name + ".osu";
        }


        // HitObjectsから区間ごとの slider/spinner を数えるヘルパー
        internal static List<(double start, double end, int sliderCount, int spinnerCount, int totalInSeg)>
        CountRollAndSwellInSegments(string osuText, List<StableVisualAssist.SvaSegment> segs)
        {
            var result = new List<(double, double, int, int, int)>();
            if (segs == null || segs.Count == 0) return result;

            // segごとのカウンタ
            var sliders = new int[segs.Count];
            var spinners = new int[segs.Count];
            var totals = new int[segs.Count];

            // [HitObjects] 範囲
            var lines = osuText.Replace("\r\n", "\n").Split('\n');
            int hoIdx = Array.FindIndex(lines, l => l.Trim().Equals("[HitObjects]", StringComparison.OrdinalIgnoreCase));
            if (hoIdx < 0) return result;

            for (int i = hoIdx + 1; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (line.Length == 0) continue;
                if (line.StartsWith("[")) break; // 次セクション
                if (line.StartsWith("//")) continue;

                var p = line.Split(',');
                if (p.Length < 4) continue;

                if (!double.TryParse(p[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var t))
                    continue;
                if (!int.TryParse(p[3], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var type))
                    continue;

                // t が入る seg を探す（segsは昇順前提。違っても動くように線形でOK）
                for (int s = 0; s < segs.Count; s++)
                {
                    double st = segs[s].StartTimeMs;
                    double ed = segs[s].EndTimeMs; // INF の可能性あり
                    bool inSeg = (t >= st) && (double.IsInfinity(ed) ? true : (t < ed));
                    if (!inSeg) continue;

                    totals[s]++;

                    if ((type & 2) != 0) sliders[s]++;   // slider = DrumRoll
                    if ((type & 8) != 0) spinners[s]++;  // spinner = Swell
                    break;
                }
            }

            for (int s = 0; s < segs.Count; s++)
            {
                result.Add((segs[s].StartTimeMs, segs[s].EndTimeMs, sliders[s], spinners[s], totals[s]));
            }
            return result;
        }
    }
}
