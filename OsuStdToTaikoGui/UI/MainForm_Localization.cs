using System.Globalization;
using OsuStdToTaiko;

namespace OsuStdToTaikoGui
{
    public partial class MainForm : Form
    {
        // ResourceManager
        static readonly System.Resources.ResourceManager RM =
            new System.Resources.ResourceManager(
                "OsuStdToTaikoGui.Resources",
                System.Reflection.Assembly.GetExecutingAssembly());

        static string T(string key) => RM.GetString(key) ?? key;


        // 選択中言語
        bool langUiInitialized = false;
        string currentCulture = "ja";
        bool suppressLangEvent = false;


        // 言語コンボの初期化を “1回だけ” にする
        void EnsureLangCombo()
        {
            if (langUiInitialized) return;

            cmbLang.Items.Clear();
            // 表示名は resx にしない（言語名は固定でOK）
            cmbLang.Items.Add("日本語");
            cmbLang.Items.Add("English");

            langUiInitialized = true;
        }


        // 言語切替メソッド
        void ApplyLanguage(string culture)
        {
            currentCulture = culture;

            appSettings.UiCulture = culture;
            SaveSettings(appSettings);

            Thread.CurrentThread.CurrentUICulture = new CultureInfo(culture);
            Thread.CurrentThread.CurrentCulture = new CultureInfo(culture);

            // --- 文言更新だけ ---
            lblInput.Text = T("InputFile");
            lblMode.Text = T("OutputMode");
            btnRun.Text = T("Convert");
            chkLazerSafe.Text = T("LazerSafe");
            chkConstantSpeed.Text = T("ConstantSpeed");
            lblDropHint.Text = T("DropHint");

            if (chkSva != null)
                chkSva.Text = T("UiSvaEnable");

            Text = T("AppTitle");

            // OutputMode コンボを「作り直す」
            int selected = cmbMode.SelectedIndex;

            cmbMode.BeginUpdate();
            try
            {
                cmbMode.Items.Clear();
                cmbMode.Items.Add(new ModeItem(OutputMode.Stable, T("ModeStable")));
                cmbMode.Items.Add(new ModeItem(OutputMode.Lazer, T("ModeLazer")));
                cmbMode.Items.Add(new ModeItem(OutputMode.Original, T("ModeOriginal")));

                cmbMode.SelectedIndex = (selected >= 0 && selected < cmbMode.Items.Count) ? selected : 0;
            }
            finally
            {
                cmbMode.EndUpdate();
            }

        }


        // ハンドラ
        void OnLangRadioChanged(object? sender, EventArgs e)
        {
            if (suppressLangEvent) return;

            // CheckedChangedは両方から来るので、Checked=true側だけ処理
            if (rbJa.Checked) ApplyLanguage("ja");
            else if (rbEn.Checked) ApplyLanguage("en");

            // 見た目だけ再適用（RebuildUiは呼ばない）
            ApplyUIFont();
            ApplyTheme();
        }
    }
}
