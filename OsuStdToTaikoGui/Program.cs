namespace OsuStdToTaikoGui
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            System.IO.File.AppendAllText(
                "C:\\osu-tools\\OsuStdToTaikoGui\\startup.log",
                DateTime.Now.ToString("s") + " Main reached" + Environment.NewLine);

            try
            {
                ApplicationConfiguration.Initialize();

                var f = new MainForm();
                System.IO.File.AppendAllText(
                    "C:\\osu-tools\\OsuStdToTaikoGui\\startup.log",
                    DateTime.Now.ToString("s") + " Form created" + Environment.NewLine);

                Application.Run(f);

                System.IO.File.AppendAllText(
                    "C:\\osu-tools\\OsuStdToTaikoGui\\startup.log",
                    DateTime.Now.ToString("s") + " Run returned" + Environment.NewLine);
            }
            catch (Exception ex)
            {
                System.IO.File.AppendAllText(
                    "C:\\osu-tools\\OsuStdToTaikoGui\\startup.log",
                    DateTime.Now.ToString("s") + " EX: " + ex + Environment.NewLine);

                MessageBox.Show(ex.ToString(), "Startup error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
