namespace JixModMaker;

public sealed class VideoRuntimeDialog : Form
{
    private readonly Label _status = new() { AutoSize = true, Dock = DockStyle.Fill, Text = "首次启用约需下载 117 MB，完成后可离线转换。" };
    private readonly Button _install = Theme.FlatButton("下载并启用", 136);
    private readonly Button _cancel = Theme.FlatButton("取消", 88);
    private readonly ProgressBar _progress = new() { Dock = DockStyle.Fill, Height = 12, Style = ProgressBarStyle.Marquee, Visible = false };
    private CancellationTokenSource _operation;

    public static bool EnsureReady(IWin32Window owner)
    {
        if (VideoRuntime.IsReady) return true;
        using var dialog = new VideoRuntimeDialog();
        return dialog.ShowDialog(owner) == DialogResult.OK;
    }

    public VideoRuntimeDialog()
    {
        Text = "视频转换组件 - " + AppBuildInfo.Version;
        Font = Theme.UI(9f);
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(510, 310);
        MinimumSize = new Size(450, 310);
        MaximizeBox = false;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, RowCount = 5 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var heading = new Label { Text = "启用视频 / GIF 转换", AutoSize = true, Font = Theme.UI(12f, true),
            ForeColor = Theme.Accent, Padding = new Padding(0, 0, 0, 12) };
        var sources = new Label { AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(0, 0, 0, 14),
            Text = "FFmpeg 8.1.1 · Gyan 官方发布页\nPython 3.11.9 · python.org\nCriCodecs 1.2.0 · PyPI\n\n下载到当前用户目录，校验 SHA256；不修改游戏或系统配置。" };
        layout.Controls.Add(heading, 0, 0);
        layout.Controls.Add(sources, 0, 1);
        layout.Controls.Add(_status, 0, 2);
        layout.Controls.Add(_progress, 0, 3);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
        actions.Controls.AddRange(new Control[] { _cancel, _install });
        layout.Controls.Add(actions, 0, 4);
        Controls.Add(layout);
        void LayoutText()
        {
            int width = Math.Max(100, layout.ClientSize.Width - layout.Padding.Horizontal - 8);
            sources.MaximumSize = _status.MaximumSize = new Size(width, 0);
            var size = TextRenderer.MeasureText(sources.Text, sources.Font, new Size(width, int.MaxValue),
                TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.TextBoxControl);
            sources.MinimumSize = new Size(0, size.Height + sources.Padding.Vertical + 4);
        }
        layout.SizeChanged += (_, _) => LayoutText();
        LayoutText();
        if (VideoRuntime.IsReady)
        {
            _status.Text = "组件已安装，可检查并重新下载。已校验的下载文件会复用。";
            _install.Text = "检查 / 修复";
        }
        _install.Click += async (_, _) => await InstallAsync();
        _cancel.Click += (_, _) => { if (_operation != null) _operation.Cancel(); else Close(); };
        FormClosing += (_, e) => { if (_operation != null) { e.Cancel = true; _operation.Cancel(); } };
    }

    private async Task InstallAsync()
    {
        if (_operation != null) return;
        _operation = new CancellationTokenSource();
        _install.Enabled = false;
        _progress.Visible = true;
        _status.Text = "正在准备下载...";
        bool installed = false;
        try
        {
            var operation = _operation;
            var progress = new Progress<string>(message => { if (!IsDisposed && ReferenceEquals(_operation, operation)) _status.Text = message; });
            await VideoRuntime.InstallAsync(progress, _operation.Token);
            installed = true;
        }
        catch (OperationCanceledException) { _status.Text = "已取消，未修改游戏；点击重试可复用已完成的下载。"; }
        catch (Exception ex) { _status.Text = "启用未完成：" + ex.Message; }
        finally
        {
            _operation.Dispose();
            _operation = null;
            _install.Enabled = true;
            _install.Text = "重新检查";
            _progress.Visible = false;
        }
        if (installed) { DialogResult = DialogResult.OK; Close(); }
    }
}
