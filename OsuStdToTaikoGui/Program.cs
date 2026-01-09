using System;
using System.IO;
using System.Windows.Forms;

namespace OsuStdToTaikoGui
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            try
            {
                string logPath = Path.Combine(AppContext.BaseDirectory, "startup.log");
                File.AppendAllText(logPath,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - Started{Environment.NewLine}");
            }
            catch
            {
                // ƒƒO‘‚«‚İ‚É¸”s‚µ‚Ä‚àƒAƒvƒŠ‚Í—‚Æ‚³‚È‚¢
            }

            ApplicationConfiguration.Initialize();
            Application.Run(new MainForm());
        }
    }
}
