namespace OsuStdToTaikoGui
{
    public partial class MainForm : Form
    {
        // カラーパレット（from osu!）
        static readonly Color CBg = Color.FromArgb(18, 23, 18);   // 全体背景（かなり暗い緑黒）
        static readonly Color CSurface = Color.FromArgb(28, 38, 28);   // 面（panel）
        static readonly Color CSurface2 = Color.FromArgb(34, 48, 34);   // 面2（強調/ログ枠など）
        static readonly Color CBorder = Color.FromArgb(52, 78, 52);   // 枠線
        static readonly Color CAccent = Color.FromArgb(120, 255, 120);// 明るいアクセント緑
        static readonly Color CText = Color.FromArgb(232, 238, 232);// 基本文字（明るい）
        static readonly Color CTextDim = Color.FromArgb(170, 182, 170);// サブ文字
        static readonly Color CInputBg = Color.FromArgb(22, 28, 22);   // 入力欄背景
        static readonly Color CLogBg = Color.FromArgb(12, 16, 12);   // ログ背景（さらに暗い）
        // カラーパレット（追加）
        static readonly Color CBtn = CSurface2;
        static readonly Color CBtnHover = Color.FromArgb(40, 56, 40);
        static readonly Color CBtnDown = Color.FromArgb(55, 80, 55);
        static readonly Color CDropHoverBg = Color.FromArgb(30, 58, 30);
        static readonly Color CDropHoverText = Color.FromArgb(240, 245, 240);
        static readonly Color CFrame = Color.FromArgb(32, 46, 32);   // ★ログ枠など大きい枠用（暗め）


        // テーマ適用
        private void ApplyTheme()
        {
            // 全体
            BackColor = CBg;
            if (rootPanel != null) rootPanel.BackColor = CBg;

            // Label系（見落としがちなのでまとめて明るく）
            lblInput.ForeColor = CText;
            lblMode.ForeColor = CText;
            lblDropHint.ForeColor = CTextDim;

            // 入力
            txtIn.BackColor = CInputBg;
            txtIn.ForeColor = CText;
            txtIn.BorderStyle = BorderStyle.FixedSingle;

            // Drop
            pnlDrop.BackColor = CSurface;
            // 枠は Paint 側（すでに描いてる）で CBorder/CAccent を使う
            pnlDrop.Invalidate();

            // ComboBox（WinFormsは完全に色が効かないこともあるが、効く範囲で）
            cmbMode.BackColor = CInputBg;
            cmbMode.ForeColor = CText;

            // CheckBox/RadioButton：BackColor効きにくいので ForeColorだけ確実に
            chkLazerSafe.ForeColor = CText;
            chkConstantSpeed.ForeColor = CText;
            if (chkSva != null) chkSva.ForeColor = CText;

            rbJa.ForeColor = CText;
            rbEn.ForeColor = CText;

            // 背景をなじませたい場合（効く環境のみ）
            chkLazerSafe.BackColor = CBg;
            chkConstantSpeed.BackColor = CBg;
            if (chkSva != null) chkSva.BackColor = CBg;
            rbJa.BackColor = CBg;
            rbEn.BackColor = CBg;

            // ボタン（フラット化：osu!lazer寄せ）
            btnRun.FlatStyle = FlatStyle.Flat;
            btnRun.FlatAppearance.BorderSize = 1;
            btnRun.FlatAppearance.BorderColor = CBorder;
            btnRun.BackColor = CSurface2;
            btnRun.ForeColor = CText;

            // ログ（暗背景＋明るい文字）
            txtLog.BackColor = CLogBg;
            txtLog.ForeColor = CText;
            txtLog.BorderStyle = BorderStyle.None;

            // Log frame
            pnlLogFrame.BackColor = CFrame; // 枠色にする
            txtLog.BackColor = CLogBg;
            txtLog.ForeColor = CText;

            // 既存ログ色も暗背景向けに調整したいなら（任意）
            // LogInputColor / LogOkColor / LogWarnColor ... を明るめに寄せる

            txtIn.Invalidate();
            cmbMode.Invalidate();
            txtLog.Invalidate();
        }


        // Runボタンのマウスイベントハンドラ
        void BtnRun_MouseEnter(object? sender, EventArgs e)
        {
            btnRun.BackColor = CBtnHover;
            btnRun.FlatAppearance.BorderColor = CAccent;
        }
        void BtnRun_MouseLeave(object? sender, EventArgs e)
        {
            btnRun.BackColor = CBtn;
            btnRun.FlatAppearance.BorderColor = CBorder;
        }
        void BtnRun_MouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
                btnRun.BackColor = CBtnDown;
        }
        void BtnRun_MouseUp(object? sender, MouseEventArgs e)
        {
            if (btnRun.ClientRectangle.Contains(btnRun.PointToClient(Cursor.Position)))
                btnRun.BackColor = CBtnHover;
            else
                btnRun.BackColor = CBtn;
        }
    }
}
