// osu!standard マップの taiko コンバート譜面を、osu!taiko 仕様の Beatmap として出力するプログラム
// このプログラムは「TaikoBeatmapConverter.cs」を参考に、独自の検証を行い作製しました（制作者：Vanity8）
namespace OsuStdToTaiko
{
    class Program
    {
        static int Main(string[] args)
        {
            if (!CliOptions.TryParse(args, out var opt, out var exitCode))
                return exitCode;

            try
            {
                ConverterCore.ConvertFile(
                    opt.InputPath,
                    opt.OutputPath,
                    opt.OutputMode,
                    opt.LazerSafe
                );
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                return 1;
            }
        }

    }

}
