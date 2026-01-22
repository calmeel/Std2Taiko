namespace OsuStdToTaikoGui
{
    public partial class MainForm : Form
    {
        // ウィンドウサイズに合わせて伸縮するコントロールの調整
        private void FitResizableControlsToWindowOnce()
        {
            if (rootPanel == null) return;

            const int margin = 12;

            // 入力欄
            txtIn.Width = Math.Max(100, rootPanel.ClientSize.Width - txtIn.Left - margin);
            // Drop（Panelだけ）
            pnlDrop.Width = Math.Max(100, rootPanel.ClientSize.Width - pnlDrop.Left - margin);
            // Log枠
            pnlLogFrame.Width = Math.Max(100, rootPanel.ClientSize.Width - pnlLogFrame.Left - margin);
            pnlLogFrame.Height = Math.Max(100, rootPanel.ClientSize.Height - pnlLogFrame.Top - margin);
        }

        // フォームのリサイズイベントハンドラ
        private void OnFormResize(object? sender, EventArgs e)
        {
            FitResizableControlsToWindowOnce();
        }

        // ウィンドウサイズ変更ハンドラ
        void OnWindowSizeChanged(object? sender, EventArgs e)
        {
            ApplyRunButtonLayout();
        }

    }
}
