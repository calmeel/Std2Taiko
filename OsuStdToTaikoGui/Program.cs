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
            string logPath = Path.Combine(AppContext.BaseDirectory, "startup.log");

            try
            {
                // 起動ログ
                File.AppendAllText(logPath,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - Main entered{Environment.NewLine}");

                ApplicationConfiguration.Initialize();

                File.AppendAllText(logPath,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - Before MainForm{Environment.NewLine}");

                Application.Run(new MainForm());

                File.AppendAllText(logPath,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - Main exited normally{Environment.NewLine}");
            }
            catch (Exception ex)
            {
                // 例外もログ＋メッセージボックスに出す
                try
                {
                    File.AppendAllText(logPath,
                        $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - ERROR {ex}{Environment.NewLine}");
                }
                catch { }

                MessageBox.Show(
                    ex.ToString(),
                    "Startup error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }
    }
}
