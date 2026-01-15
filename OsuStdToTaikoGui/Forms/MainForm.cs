namespace OsuStdToTaikoGui
{
    public partial class MainForm : Form
    {
        public MainForm()
        {
            InitializeComponent();
            LoadBundledFontsOnce();

            appSettings = LoadSettings();
            currentCulture = appSettings.UiCulture;

            BuildUI();      // ← UIの文言＆イベント設定
            ApplyLanguage(currentCulture);  // // その後、文言更新
            RebuildUi();    // ← rootPanel を貼る

            // UIが存在してから状態を反映
            chkConstantSpeed.Checked = appSettings.ExportConstantSpeed;
            UpdateSvaAvailability(); // SVA連動があるなら一応呼ぶ

            Shown -= MainForm_Shown;
            Shown += MainForm_Shown;
        }

        private void MainForm_Shown(object? sender, EventArgs e)
        {
            // 1回だけでOK（Shownは複数回呼ばれないが念のため解除）
            Shown -= MainForm_Shown;

            FitResizableControlsToWindowOnce();
        }


    }
}
