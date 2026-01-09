namespace OsuStdToTaikoGui
{
    public partial class MainForm : Form
    {
        void PickOsuFile(TextBox target, string title)
        {
            using var dlg = new OpenFileDialog
            {
                Title = title,
                Filter = "osu beatmap (*.osu)|*.osu|All files (*.*)|*.*",
                CheckFileExists = true
            };

            if (dlg.ShowDialog() == DialogResult.OK)
                target.Text = dlg.FileName;
        }

        // ファイル選択ポップアップのハンドラ
        void OnBrowseClick(object? sender, EventArgs e) => PickOsuFile(txtIn, T("InputFile"));

        // ドラッグ＆ドロップ用ハンドラ
        void OnDragEnter(object? sender, DragEventArgs e)
        {
            bool accept = false;

            if (e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[]?)e.Data.GetData(DataFormats.FileDrop);
                if (files != null && files.Length > 0)
                {
                    // 先頭だけ使う想定なら先頭だけ判定
                    accept = files[0].EndsWith(".osu", StringComparison.OrdinalIgnoreCase);
                }
            }

            e.Effect = accept ? DragDropEffects.Copy : DragDropEffects.None;

            // 見た目は accept のときだけ変える
            isDragOver = accept;
            pnlDrop.BackColor = accept ? CDropHoverBg : CSurface;
            lblDropHint.ForeColor = accept ? CDropHoverText : CTextDim;

            lblDropHint.Text = accept ? T("DropNow") : T("DropHint");
            pnlDrop.Invalidate();
        }


        // ドラッグ＆ドロップ用ハンドラ
        void OnDragDrop(object? sender, DragEventArgs e)
        {
            if (e.Data == null || !e.Data.GetDataPresent(DataFormats.FileDrop))
                return;

            var files = (string[]?)e.Data.GetData(DataFormats.FileDrop);
            if (files == null || files.Length == 0)
                return;

            var path = files[0];

            if (!path.EndsWith(".osu", StringComparison.OrdinalIgnoreCase))
                return;

            if (!File.Exists(path))
                return;

            txtIn.Text = path;
            LogColored($"▶ Input → {Path.GetFileName(path)}", LogInputColor);

            isDragOver = false;
            pnlDrop.BackColor = CSurface;
            lblDropHint.Text = T("DropHint");
            pnlDrop.Invalidate();
        }

        // ドラッグ＆ドロップ用
        void PnlDrop_Paint(object? sender, PaintEventArgs e)
        {
            var rect = pnlDrop.ClientRectangle;
            rect.Inflate(-1, -1);

            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            bool hot = isDragOver;

            using var pen = new Pen(hot ? CAccent : CBorder, hot ? 2.5f : 1.6f);
            pen.DashStyle = hot
                ? System.Drawing.Drawing2D.DashStyle.Solid
                : System.Drawing.Drawing2D.DashStyle.Dash;

            e.Graphics.DrawRectangle(pen, rect);
        }


        // ドラッグ＆ドロップ用
        void OnDragLeave(object? sender, EventArgs e)
        {
            isDragOver = false;
            pnlDrop.BackColor = CSurface;
            lblDropHint.Text = T("DropHint");
            pnlDrop.Invalidate();
        }
    }
}
