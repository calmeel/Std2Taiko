namespace OsuStdToTaiko
{
    public static class OsuTextUtils
    {
        // 入力ファイルの osu file format
        public static int GetInputBeatmapVersion(string osuText)
        {
            var firstLine = osuText
                .Replace("\r\n", "\n")
                .Split('\n')
                .FirstOrDefault();

            if (firstLine == null) return 14;

            var digits = new string(firstLine.Where(char.IsDigit).ToArray());
            if (int.TryParse(digits, out int v))
                return v;

            return 14;
        }

        // 出力ファイルの osu file format
        public static string RewriteOsuFileVersion(string osuText, int version)
        {
            var lines = osuText.Replace("\r\n", "\n").Split('\n').ToList();

            if (lines.Count > 0 && lines[0].StartsWith("osu file format v", StringComparison.OrdinalIgnoreCase))
                lines[0] = $"osu file format v{version}";
            else
                lines.Insert(0, $"osu file format v{version}");

            return string.Join("\n", lines).Replace("\n", "\r\n");
        }

        // [Metadata] の Version: に suffix を付与する
        public static string AppendSuffixToDifficultyVersion(string osuText, string suffix)
        {
            var lines = osuText.Replace("\r\n", "\n").Split('\n').ToList();

            int idxMeta = lines.FindIndex(l =>
                l.Trim().Equals("[Metadata]", StringComparison.OrdinalIgnoreCase));
            if (idxMeta < 0) return osuText;

            for (int i = idxMeta + 1; i < lines.Count; i++)
            {
                string raw = lines[i];
                string t = raw.Trim();

                if (t.StartsWith("[") && t.EndsWith("]"))
                    break;

                if (t.StartsWith("Version:", StringComparison.OrdinalIgnoreCase))
                {
                    string value = raw.Substring(raw.IndexOf(':') + 1).Trim();

                    if (!value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    {
                        lines[i] = $"Version:{value}{suffix}";
                    }

                    break;
                }
            }

            return string.Join("\n", lines).Replace("\n", "\r\n");
        }

        // [Metadata] の BeatmapID: を 0 に変更する（既存のマップとの重複防止）
        public static string ForceBeatmapIDZero(string osuText)
        {
            var lines = osuText.Replace("\r\n", "\n").Split('\n').ToList();

            int idxMeta = lines.FindIndex(l => l.Trim().Equals("[Metadata]", StringComparison.OrdinalIgnoreCase));
            if (idxMeta < 0) return osuText;

            for (int i = idxMeta + 1; i < lines.Count; i++)
            {
                var t = lines[i].Trim();

                if (t.StartsWith("[") && t.EndsWith("]"))
                    break;

                if (t.StartsWith("BeatmapID:", StringComparison.OrdinalIgnoreCase))
                {
                    lines[i] = "BeatmapID:0";
                    break;
                }
            }

            return string.Join("\n", lines).Replace("\n", "\r\n");
        }
    }
}
