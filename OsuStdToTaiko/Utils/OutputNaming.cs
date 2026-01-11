namespace OsuStdToTaiko
{
    public static class OutputNaming
    {
        // [Metadata] 用 suffix
        public static string BuildTaikoConvertVersionSuffix()
        {
            return " (taiko convert)";
        }

        // [Metadata] 用 suffix
        public static string BuildDifficultyVersionSuffix(bool constantSpeed, bool svaApplied)
        {
            if (!constantSpeed)
                return "";

            return svaApplied
                ? " (constant speed adjusted)"
                : " (constant speed)";
        }

        // GUI出力ファイル名用 suffix
        public static string BuildTaikoFileNameSuffix(bool constantSpeed, bool adjusted)
        {
            if (!constantSpeed)
                return " (taiko convert)";

            return adjusted
                ? " (taiko convert) (constant speed adjusted)"
                : " (taiko convert) (constant speed)";
        }
    }
}
