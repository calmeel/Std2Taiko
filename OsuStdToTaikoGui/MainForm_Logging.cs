namespace OsuStdToTaikoGui
{
    public partial class MainForm : Form
    {
        // 色付きログ用ヘルパー関数
        void LogColored(string text, Color color)
        {
            txtLog.SelectionStart = txtLog.TextLength;
            txtLog.SelectionLength = 0;
            txtLog.SelectionColor = color;

            txtLog.AppendText(text + Environment.NewLine);

            txtLog.SelectionColor = txtLog.ForeColor;
            txtLog.ScrollToCaret();
        }
        readonly Color LogInputColor = Color.ForestGreen;
        readonly Color LogOkColor = Color.MediumSeaGreen;
        readonly Color LogOkConstantColor = Color.MediumSeaGreen;
        readonly Color LogOutputColor = Color.DimGray;
        readonly Color LogErrorColor = Color.IndianRed;
        readonly Color LogWarnColor = Color.Red;

        void Log(string s)
        {
            txtLog.AppendText(s + Environment.NewLine);
            txtLog.SelectionStart = txtLog.TextLength;
            txtLog.ScrollToCaret();
        }

        static void StartupLog(string msg)
        {
            try
            {
                string logPath = Path.Combine(AppContext.BaseDirectory, "startup.log");
                File.AppendAllText(
                    logPath,
                    DateTime.Now.ToString("s") + " " + msg + Environment.NewLine);
            }
            catch
            {
                // ログ失敗は無視（起動診断用なので）
            }
        }
    }
}
