namespace OsuStdToTaiko
{
    internal sealed class CliOptions
    {
        public string InputPath { get; private set; }
        public string OutputPath { get; private set; }
        public OutputMode OutputMode { get; private set; }
        public bool LazerSafe { get; private set; }

        private CliOptions() { }

        public static bool TryParse(
            string[] args,
            out CliOptions opt,
            out int exitCode)
        {
            opt = null;
            exitCode = 2;

            if (args.Length < 2 || args.Length > 4)
            {
                PrintUsage();
                return false;
            }

            string inputPath = args[0];
            string outputPath = args[1];

            OutputMode outputMode = OutputMode.Lazer;  // default

            // lazer で確実に読み込めるように、出力を安全側に正規化する（Aspire 等の壊れ値対策）
            bool lazerSafe = false;

            // 第3引数: 出力モード（lazer/stable/original）もしくは "lazer-safe"
            if (args.Length >= 3)
            {
                string a2 = args[2].ToLowerInvariant();
                if (a2 == "lazer-safe")
                {
                    lazerSafe = true;  // モード省略で lazer-safe のみ指定された場合
                }
                else
                {
                    switch (a2)
                    {
                        case "lazer": outputMode = OutputMode.Lazer; break;
                        case "stable": outputMode = OutputMode.Stable; break;
                        case "original": outputMode = OutputMode.Original; break;
                        default:
                            Console.Error.WriteLine("OutputMode は lazer / stable / original");
                            Console.Error.WriteLine("または第3引数に lazer-safe を指定できます");
                            return false;
                    }
                }
            }

            // 第4引数: "lazer-safe"（任意）
            if (args.Length >= 4 && args[3].ToLowerInvariant() == "lazer-safe")
                lazerSafe = true;

            if (!File.Exists(inputPath))
            {
                Console.Error.WriteLine($"入力ファイルが見つかりません: {inputPath}");
                return false;
            }

            opt = new CliOptions
            {
                InputPath = inputPath,
                OutputPath = outputPath,
                OutputMode = outputMode,
                LazerSafe = lazerSafe
            };

            exitCode = 0;
            return true;
        }

        private static void PrintUsage()
        {
            Console.Error.WriteLine("使い方: dotnet run -- <input.osu> <output.osu> [lazer|stable|original] [lazer-safe]");
            Console.Error.WriteLine("例: dotnet run -- input.osu output_taiko.osu stable lazer-safe");
        }

    }
}
