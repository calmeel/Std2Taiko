using System.Drawing.Text;

namespace OsuStdToTaikoGui
{
    public partial class MainForm : Form
    {
        // フォント
        static PrivateFontCollection? _privateFonts;

        static Font UiFontJa = new Font(SystemFonts.MessageBoxFont.FontFamily, 9.5f, FontStyle.Regular);
        static Font UiFontEn = new Font(SystemFonts.MessageBoxFont.FontFamily, 9.5f, FontStyle.Regular);
        static readonly Font LogFont = new Font("Consolas", 9f);

        // フォント読み込み関数
        static void LoadBundledFontsOnce()
        {
            if (_privateFonts != null) return;

            _privateFonts = new PrivateFontCollection();

            string baseDir = AppContext.BaseDirectory;
            string jpPath = Path.Combine(baseDir, "Fonts", "NotoSansJP-Medium.otf");
            string enPath = Path.Combine(baseDir, "Fonts", "Aller-Regular.ttf");

            try
            {
                if (File.Exists(jpPath)) _privateFonts.AddFontFile(jpPath);
                if (File.Exists(enPath)) _privateFonts.AddFontFile(enPath);

                // 追加された中から名前で探す（ファミリ名はフォント内部定義に依存）
                FontFamily? famJa = _privateFonts.Families.FirstOrDefault(f =>
                    f.Name.Equals("Noto Sans JP", StringComparison.OrdinalIgnoreCase) ||
                    f.Name.StartsWith("Noto Sans JP", StringComparison.OrdinalIgnoreCase));

                FontFamily? famEn = _privateFonts.Families.FirstOrDefault(f =>
                    f.Name.Equals("Aller", StringComparison.OrdinalIgnoreCase) ||
                    f.Name.StartsWith("Aller", StringComparison.OrdinalIgnoreCase));

                // 見つかったものだけ採用（見つからない場合はOSフォントへフォールバック）
                if (famJa != null)
                    UiFontJa = new Font(famJa, 9.5f, FontStyle.Regular);
                else
                    UiFontJa = new Font("Noto Sans JP", 9.5f, FontStyle.Regular);

                if (famEn != null)
                    UiFontEn = new Font(famEn, 9.5f, FontStyle.Regular);
                else
                    UiFontEn = new Font("Aller", 9.5f, FontStyle.Regular);
            }
            catch
            {
                // 最終フォールバック
                UiFontJa = new Font(SystemFonts.MessageBoxFont.FontFamily, 9.5f, FontStyle.Regular);
                UiFontEn = new Font(SystemFonts.MessageBoxFont.FontFamily, 9.5f, FontStyle.Regular);
            }
        }


        // フォント適用
        private void ApplyUIFont()
        {
            Font uiFont = (currentCulture == "en") ? UiFontEn : UiFontJa;

            if (rootPanel != null)
                ApplyFontRecursive(rootPanel, uiFont, LogFont);
            else
                ApplyFontRecursive(this, uiFont, LogFont);

            txtLog.Font = LogFont;

            // 確認ログ（必要なら）
            // bool isPrivate = _privateFonts != null && _privateFonts.Families.Any(f => f.Name == uiFont.FontFamily.Name);
            // Log($"[UIFont] culture={currentCulture} ui={uiFont.FontFamily.Name} private={isPrivate} lblInput={lblInput.Font.Name} txtLog={txtLog.Font.Name}");

            // --- Run button font override (bold + slightly larger) ---
            if (_runButtonFont == null ||
                _runButtonFont.FontFamily != btnRun.Font.FontFamily ||
                Math.Abs(_runButtonFont.Size - (btnRun.Font.Size + 0.5f)) > 0.01f)
            {
                _runButtonFont?.Dispose();
                _runButtonFont = new Font(btnRun.Font.FontFamily, btnRun.Font.Size + 0.5f, FontStyle.Bold);
            }

            btnRun.Font = _runButtonFont;
            btnRun.UseCompatibleTextRendering = true; // 太字が反映されにくい環境の保険
            btnRun.Invalidate();
        }

        // 変換ボタンのフォント
        Font? _runButtonFont;

        const int RUNBTN_MAX_WIDTH = 390; // この幅を下回ったら小さく
        const int RIGHT_PADDING = 12;   // 右余白
        const int MIN_WIDTH = 120;

        // 変換ボタンのレイアウト調整（太字＋サイズアップ）
        void ApplyRunButtonLayout()
        {
            int available =
                rootPanel.ClientSize.Width - btnRun.Left - RIGHT_PADDING;

            if (available < RUNBTN_MAX_WIDTH)
            {
                btnRun.Width = Math.Max(MIN_WIDTH, available);
            }
            else
            {
                btnRun.Width = RUNBTN_MAX_WIDTH;
            }
        }

        // フォント適用の再帰処理
        private void ApplyFontRecursive(Control parent, Font uiFont, Font logFont)
        {
            foreach (Control c in parent.Controls)
            {
                c.Font = ReferenceEquals(c, txtLog) ? logFont : uiFont;

                if (c.HasChildren)
                    ApplyFontRecursive(c, uiFont, logFont);
            }
        }
    }
}
