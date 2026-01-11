namespace OsuStdToTaiko
{
    public static class OsuTextStats
    {
        // Slider 数のカウント
        public static int CountSliders(string osuText)
        {
            int count = 0;
            bool inHitObjects = false;

            foreach (var raw in osuText.Replace("\r\n", "\n").Split('\n'))
            {
                var line = raw.Trim();
                if (line.Equals("[HitObjects]", StringComparison.OrdinalIgnoreCase))
                {
                    inHitObjects = true;
                    continue;
                }
                if (!inHitObjects) continue;
                if (line.StartsWith("[") && line.EndsWith("]")) break;
                if (line.Length == 0 || line.StartsWith("//")) continue;

                var parts = line.Split(',');
                if (parts.Length < 4) continue;
                if (!int.TryParse(parts[3], out int type)) continue;
                if ((type & 2) != 0) count++; // slider bit
            }

            return count;
        }
    }
}
