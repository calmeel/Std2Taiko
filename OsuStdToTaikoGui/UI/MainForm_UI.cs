using OsuStdToTaiko;

namespace OsuStdToTaikoGui
{
    public partial class MainForm : Form
    {
        // rootPanel
        private Panel? rootPanel;

        // UI を作る共通メソッド
        private Panel BuildRootPanel()
        {
            var panel = new Panel
            {
                Dock = DockStyle.Fill
            };

            var list = new List<Control>
            {
                lblInput, txtIn,
                pnlDrop,
                lblMode, cmbMode,
                pnlLang,
                chkLazerSafe,
                chkConstantSpeed,
                chkSva,
                rbJa, rbEn,          // 言語はとりあえず外のままでもOK
                btnRun,
                pnlLogFrame          // ★ログ枠
            };

            // chkSva は存在するときだけ追加（null/二重追加を防ぐ）
            if (chkSva != null)
                list.Add(chkSva);

            panel.Controls.AddRange(list.ToArray());

            // Log frame
            pnlLogFrame.Controls.Clear();
            pnlLogFrame.Controls.Add(txtLog);

            // Drop hint
            pnlDrop.Controls.Clear();
            pnlDrop.Controls.Add(lblDropHint);

            // Language
            pnlLang.Controls.Clear();
            pnlLang.Controls.Add(rbJa);
            pnlLang.Controls.Add(rbEn);

            return panel;
        }
        // UI 再構築用の安全メソッド
        private void RebuildUi()
        {
            // 既存の rootPanel だけを外す
            if (rootPanel != null)
            {
                Controls.Remove(rootPanel);
                rootPanel = null;
            }

            // 新しく作る
            rootPanel = BuildRootPanel();
            rootPanel.Dock = DockStyle.Fill;
            Controls.Add(rootPanel);

            // フォントを反映
            ApplyUIFont();
            // テーマを反映
            ApplyTheme();
        }


        Label lblInput = new Label { Left = 12, Top = 15, AutoSize = true, Text = T("InputFile") };
        Label lblMode = new Label { Left = 12, Top = 113, AutoSize = true, Text = T("OutputMode") };

        TextBox txtIn = new TextBox { Left = 120, Top = 12, Width = 540 };

        ComboBox cmbMode = new ComboBox { Left = 120, Top = 110, Width = 160, DropDownStyle = ComboBoxStyle.DropDownList };

        // lazer-safe モード
        CheckBox chkLazerSafe = new CheckBox { Left = 300, Top = 112, AutoSize = true, Text = T("LazerSafe") };

        // 等速モード
        CheckBox chkConstantSpeed = new CheckBox { Left = 420, Top = 150, AutoSize = true };

        // SVA
        private CheckBox? chkSva;

        // 変換ボタン
        Button btnRun = new Button { Left = 12, Top = 150, Height = 45, Width = 390, Text = T("Convert"), TextAlign = ContentAlignment.MiddleCenter };

        // 言語選択ボタン
        RadioButton rbJa = new RadioButton { Left = 6, Top = 6, Text = "JP", AutoSize = true };
        RadioButton rbEn = new RadioButton { Left = 60, Top = 6, Text = "EN", AutoSize = true };

        // ログ枠パネル
        Panel pnlLogFrame = new Panel
        {
            Left = 12,
            Top = 205,
            Width = 660,
            Height = 260,
            Padding = new Padding(2)
        };

        // 言語選択パネル
        Panel pnlLang = new Panel
        {
            Left = 550,
            Top = 105,
            Width = 120,
            Height = 36,
            Padding = new Padding(6)
        };

        // ログ
        RichTextBox txtLog = new RichTextBox
        {
            Left = 12,
            Top = 205,
            Width = 660,
            Height = 260,
            ReadOnly = true,
            Multiline = true,
            WordWrap = true,
            ScrollBars = RichTextBoxScrollBars.Vertical,
            BorderStyle = BorderStyle.None,
            Dock = DockStyle.Fill
        };

        // 出力モードのプルダウン
        ComboBox cmbLang = new ComboBox
        {
            Left = 360,
            Top = 78,
            Width = 140,
            DropDownStyle = ComboBoxStyle.DropDownList
        };

        // ドラッグ&ドロップエリア
        Panel pnlDrop = new Panel { Left = 12, Top = 44, Width = 648, Height = 56, Padding = new Padding(6) };
        Label lblDropHint = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            AutoSize = false,
            BackColor = Color.Transparent
        };
        bool isDragOver = false;

        private void EnsureSvaToggle()
        {
            if (chkSva != null) return;

            chkSva = new CheckBox
            {
                AutoSize = true,
                Left = 420,
                Top = 175
            };

            chkSva.CheckedChanged -= OnSvaCheckedChanged;
            chkSva.CheckedChanged += OnSvaCheckedChanged;
        }

        private void OnSvaCheckedChanged(object? sender, EventArgs e)
        {
            if (chkSva == null) return;

            appSettings.EnableSva = chkSva.Checked;
            SaveSettings(appSettings);
        }


        // UI構築
        void BuildUI()
        {
            StartupLog("Enter Form1()");

            EnsureLangCombo();

            // 言語切替でサイズが変わらないようにする
            AutoScaleMode = AutoScaleMode.None;

            // 表示を空にしない：現在言語に合わせて選択を同期
            suppressLangEvent = true;
            cmbLang.SelectedIndex = (currentCulture == "ja") ? 0 : 1;
            suppressLangEvent = false;

            Width = 700;
            Height = 540;

            Text = T("AppTitle");

            lblInput.Text = T("InputFile");
            lblMode.Text = T("OutputMode");

            btnRun.Text = T("Convert");
            chkLazerSafe.Text = T("LazerSafe");

            cmbMode.Items.Clear();
            cmbMode.Items.Add(new ModeItem(OutputMode.Stable, T("ModeStable")));
            cmbMode.Items.Add(new ModeItem(OutputMode.Lazer, T("ModeLazer")));
            cmbMode.Items.Add(new ModeItem(OutputMode.Original, T("ModeOriginal")));
            cmbMode.SelectedIndex = 0;

            cmbLang.Items.Clear();
            cmbLang.Items.Add(T("LangJapanese"));
            cmbLang.Items.Add(T("LangEnglish"));

            // SVA（BPM調整許可）トグル：1回だけ生成
            EnsureSvaToggle();

            // 文言・状態
            chkSva.Text = T("UiSvaEnable");
            chkSva.Checked = appSettings.EnableSva;
            UpdateSvaAvailability();

            // 等速モード
            chkConstantSpeed.Text = T("ConstantSpeed");

            // イベントは増殖しないように付け直し
            chkConstantSpeed.CheckedChanged -= OnConstantSpeedChanged;
            chkConstantSpeed.CheckedChanged += OnConstantSpeedChanged;

            // いったん表示を同期（イベント発火を抑止）
            suppressLangEvent = true;
            rbJa.Checked = (currentCulture == "ja");
            rbEn.Checked = (currentCulture == "en");
            suppressLangEvent = false;

            // イベントは増殖しないように付け直し
            rbJa.CheckedChanged -= OnLangRadioChanged;
            rbEn.CheckedChanged -= OnLangRadioChanged;
            rbJa.CheckedChanged += OnLangRadioChanged;
            rbEn.CheckedChanged += OnLangRadioChanged;

            // ---- Drag & Drop ----
            AllowDrop = true;

            // Form 自体
            DragEnter -= OnDragEnter;
            DragDrop -= OnDragDrop;
            DragEnter += OnDragEnter;
            DragDrop += OnDragDrop;

            // 入力欄（txtIn）に直接ドロップしても動くように
            txtIn.AllowDrop = true;
            txtIn.DragEnter -= OnDragEnter;
            txtIn.DragDrop -= OnDragDrop;
            txtIn.DragEnter += OnDragEnter;
            txtIn.DragDrop += OnDragDrop;

            lblDropHint.Text = T("DropHint"); // 例: "Drag & drop .osu here"
            pnlDrop.BackColor = CSurface2;   // 薄緑
            lblDropHint.ForeColor = CText;
            pnlDrop.Cursor = Cursors.Hand;

            // 枠（点線）を描く
            pnlDrop.Paint -= PnlDrop_Paint;
            pnlDrop.Paint += PnlDrop_Paint;

            // クリックで参照を開く（導線統一）
            pnlDrop.Click -= OnBrowseClick;
            pnlDrop.Click += OnBrowseClick;
            lblDropHint.Click -= OnBrowseClick;
            lblDropHint.Click += OnBrowseClick;

            // Drag&Drop（Panelにも付ける）
            pnlDrop.AllowDrop = true;
            pnlDrop.DragEnter -= OnDragEnter;
            pnlDrop.DragDrop -= OnDragDrop;
            pnlDrop.DragLeave -= OnDragLeave;
            pnlDrop.DragEnter += OnDragEnter;
            pnlDrop.DragDrop += OnDragDrop;
            pnlDrop.DragLeave += OnDragLeave;

            cmbMode.SelectedIndexChanged -= OnModeChanged;
            cmbMode.SelectedIndexChanged += OnModeChanged;
            UpdateSafeModeAvailability();

            btnRun.Click -= OnRunClick;
            btnRun.Click += OnRunClick;

            btnRun.FlatStyle = FlatStyle.Flat;
            btnRun.FlatAppearance.BorderSize = 1;

            btnRun.MouseEnter -= BtnRun_MouseEnter;
            btnRun.MouseLeave -= BtnRun_MouseLeave;
            btnRun.MouseDown -= BtnRun_MouseDown;
            btnRun.MouseUp -= BtnRun_MouseUp;

            btnRun.MouseEnter += BtnRun_MouseEnter;
            btnRun.MouseLeave += BtnRun_MouseLeave;
            btnRun.MouseDown += BtnRun_MouseDown;
            btnRun.MouseUp += BtnRun_MouseUp;

            // --- Resize 対応（Anchor 設定）---
            // 入力欄：横に伸びる
            txtIn.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

            // ドラッグ＆ドロップ領域：横に伸びる
            pnlDrop.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            pnlDrop.SizeChanged -= PnlDrop_SizeChanged;
            pnlDrop.SizeChanged += PnlDrop_SizeChanged;

            // ログ：上下左右に伸びる
            pnlLogFrame.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;

            // 変換ボタン
            btnRun.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;

            // ボタン類：位置固定
            btnRun.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            cmbMode.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            chkLazerSafe.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            chkConstantSpeed.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            rbJa.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            rbEn.Anchor = AnchorStyles.Top | AnchorStyles.Left;

            // 変換開始ボタンのフォントを少し強調
            btnRun.Font = new Font(
                btnRun.Font.FontFamily,
                btnRun.Font.Size + 0.5f,
                FontStyle.Bold
            );

            // SVA（存在する場合）
            if (chkSva != null)
                chkSva.Anchor = AnchorStyles.Top | AnchorStyles.Left;

            this.SizeChanged -= OnWindowSizeChanged;
            this.SizeChanged += OnWindowSizeChanged;

            this.Resize -= OnFormResize;
            this.Resize += OnFormResize;

            // アイコン設定
            string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
            if (File.Exists(iconPath))
            {
                this.Icon = new Icon(iconPath);
            }

        }

        // 等速モード変更ハンドラ
        private void OnConstantSpeedChanged(object? sender, EventArgs e)
        {
            appSettings.ExportConstantSpeed = chkConstantSpeed.Checked;
            SaveSettings(appSettings);

            UpdateSvaAvailability();
        }

        // Dropパネルの枠線描画ハンドラ
        void PnlDrop_SizeChanged(object? sender, EventArgs e)
        {
            // 枠線の崩れを防ぐため、サイズ変更時に再描画
            pnlDrop.Invalidate();
            pnlDrop.Update(); // すぐ反映したい場合（重ければ消してOK）
        }

        // ComboBoxに「表示名 + 実値(OutputMode)」を入れるための小クラス
        sealed class ModeItem
        {
            public OutputMode Mode { get; }
            public string Text { get; }

            public ModeItem(OutputMode mode, string text)
            {
                Mode = mode;
                Text = text;
            }

            public override string ToString() => Text;
        }

        // モード選択に応じて safe mode を制御するハンドラ
        void UpdateSafeModeAvailability()
        {
            var item = cmbMode.SelectedItem as ModeItem;
            bool isLazer = item?.Mode == OutputMode.Lazer;

            // lazer-safe は lazer のときだけ
            chkLazerSafe.Enabled = isLazer;
            if (!isLazer)
                chkLazerSafe.Checked = false;

            // ConstantSpeed は全モードで使えるので常に有効（チェック状態も維持）
            chkConstantSpeed.Enabled = true;
        }

        // モード変更ハンドラ
        void OnModeChanged(object? sender, EventArgs e)
        {
            UpdateSafeModeAvailability();
        }

        // SVA は constant speed モード限定にする
        void UpdateSvaAvailability()
        {
            bool cs = chkConstantSpeed.Checked;
            if (chkSva != null)
            {
                // SVA は constant speed ON のときだけ「操作可能」にする。
                // ただしチェック状態そのものは appSettings で保持しておきたいので、
                // ここでは Checked はいじらず、Enabled だけ切り替える。
                chkSva.Enabled = cs;
            }
        }

    }
}
