namespace JixModMaker;

public sealed class SkillAnimationDialog : Form
{
    private readonly TexRef _asset;
    private readonly SkillAnimationInfo _info;
    private readonly string _backupDir;
    private readonly string _initialFile;
    private readonly string _work = Path.Combine(Path.GetTempPath(), "JixSkillAnimation-" + Guid.NewGuid().ToString("N"));
    private readonly PictureBox _preview = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Theme.PicBg };
    private readonly Label _status = new() { Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(0, 8, 0, 8) };
    private readonly CheckBox _removeGreen = new() { Text = "去绿幕", AutoSize = true };
    private readonly Button _choose = Theme.FlatButton("选择视频");
    private readonly Button _replace = Theme.FlatButton("替换技能动画");
    private readonly Button _restore = Theme.FlatButton("恢复当前Bundle");
    private readonly Button _close = Theme.FlatButton("关闭");
    private readonly VideoCropControl _crop = new() { Dock = DockStyle.Fill };
    private readonly PortraitViewTabs _views = new() { Dock = DockStyle.Top };
    private readonly Panel _viewHost = new() { Dock = DockStyle.Fill };
    private readonly Panel _cropPage = new() { Dock = DockStyle.Fill };
    private readonly TrackBar _zoom = new() { Minimum = 100, Maximum = 400, Value = 100, TickStyle = TickStyle.None, Width = 160, Height = 30 };
    private readonly Label _dimensions = new() { AutoSize = true, Margin = new Padding(8) };
    private readonly ToolTip _tooltips = new();
    private Image _sourceImage;
    private MemoryStream _previewStream;
    private CancellationTokenSource _operation;
    private bool _writing, _sourceReady, _previewCurrent;
    private string _input;

    public bool Replaced { get; private set; }
    public bool Restored { get; private set; }
    public string ResultMessage { get; private set; } = "尚未替换";

    public SkillAnimationDialog(TexRef asset, SkillAnimationInfo info, string backupDir, string initialFile = null)
    {
        _asset = asset ?? throw new ArgumentNullException(nameof(asset));
        _info = info ?? throw new ArgumentNullException(nameof(info));
        _backupDir = backupDir;
        _initialFile = initialFile;
        Text = "替换技能动画";
        Font = Theme.UI(9f);
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(800, 640);
        MinimumSize = new Size(620, 460);
        StartPosition = FormStartPosition.CenterParent;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), RowCount = 4, ColumnCount = 1 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label
        {
            Text = $"{asset.Display ?? asset.Name}   ·   {_info.FrameNames.Count} 帧   ·   {_info.FrameWidth}×{_info.FrameHeight}",
            AutoSize = true,
            Padding = new Padding(0, 0, 0, 12)
        }, 0, 0);

        var cropLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        cropLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        cropLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        cropLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        cropLayout.Controls.Add(_crop, 0, 0);
        var cropTools = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(0, 6, 0, 0) };
        var center = Theme.FlatButton("居中");
        cropTools.Controls.AddRange(new Control[] { center, _zoom, _dimensions });
        cropLayout.Controls.Add(cropTools, 0, 1);
        _cropPage.Controls.Add(cropLayout);
        _viewHost.Controls.Add(_preview);
        _viewHost.Controls.Add(_cropPage);
        var workspace = new Panel { Dock = DockStyle.Fill };
        workspace.Controls.Add(_viewHost);
        workspace.Controls.Add(_views);
        layout.Controls.Add(workspace, 0, 1);
        _preview.Visible = false;

        center.Click += (_, _) => _crop.CenterCrop();
        _tooltips.SetToolTip(_zoom, "取景缩放");
        _tooltips.SetToolTip(_crop, "拖动取景框调整位置");
        _zoom.ValueChanged += (_, _) => _crop.SetZoom(_zoom.Value / 100f);
        _crop.CropChanged += (_, _) =>
        {
            _previewCurrent = false;
            if (_sourceImage != null)
                _dimensions.Text = $"{(int)Math.Round(_crop.Crop.Width * _sourceImage.Width)} × {(int)Math.Round(_crop.Crop.Height * _sourceImage.Height)}  /  {_info.FrameWidth}:{_info.FrameHeight}";
        };
        _views.SelectedIndexChanged += async (_, _) =>
        {
            _cropPage.Visible = _views.SelectedIndex == 0;
            _preview.Visible = _views.SelectedIndex == 1;
            if (_views.SelectedIndex == 1 && _sourceReady && !_previewCurrent && _operation == null)
                await UpdatePreviewAsync();
        };

        layout.Controls.Add(_status, 0, 2);
        var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
        footer.Controls.AddRange(new Control[] { _close, _replace, _choose, _removeGreen, _restore });
        layout.Controls.Add(footer, 0, 3);
        Controls.Add(layout);
        layout.SizeChanged += (_, _) => _status.MaximumSize = new Size(Math.Max(100, layout.ClientSize.Width - layout.Padding.Horizontal - 8), 0);

        _replace.Enabled = false;
        _replace.BackColor = Theme.Accent;
        _restore.Visible = File.Exists(ResourceLocator.BackupPath(asset.BundlePath, backupDir, asset.BundleName));
        foreach (Control surface in new Control[] { this, _preview, _crop, layout })
        {
            surface.AllowDrop = true;
            surface.DragEnter += (_, e) => e.Effect = _operation == null &&
                e.Data.GetData(DataFormats.FileDrop) is string[] { Length: 1 } files && IsSource(files[0])
                ? DragDropEffects.Copy : DragDropEffects.None;
            surface.DragDrop += async (_, e) =>
            {
                if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: 1 } files) await LoadSourceAsync(files[0]);
            };
        }
        _choose.Click += async (_, _) =>
        {
            using var picker = new OpenFileDialog { Filter = "视频 / GIF|*.mp4;*.mov;*.webm;*.avi;*.mkv;*.gif" };
            if (picker.ShowDialog(this) == DialogResult.OK) await LoadSourceAsync(picker.FileName);
        };
        _replace.Click += async (_, _) => await ReplaceAsync();
        _restore.Click += async (_, _) => await RestoreAsync();
        _close.Click += (_, _) => { if (_operation != null) _operation.Cancel(); else Close(); };
        _removeGreen.CheckedChanged += async (_, _) =>
        {
            _previewCurrent = false;
            if (_input != null && _operation == null && _views.SelectedIndex == 1) await UpdatePreviewAsync();
        };
        FormClosing += (_, e) => { if (_operation != null) { e.Cancel = true; if (!_writing) _operation.Cancel(); } };
        FormClosed += (_, _) => Cleanup();
        Shown += async (_, _) =>
        {
            var area = Screen.FromControl(this).WorkingArea;
            if (Height > area.Height) Height = area.Height;
            if (Width > area.Width) Width = area.Width;
            SetStatus("拖入视频或 GIF，调整取景后即可替换。只会修改当前技能动画资源包。");
            if (_initialFile != null) await LoadSourceAsync(_initialFile);
        };
    }

    private static bool IsSource(string path) => new[] { ".mp4", ".mov", ".webm", ".avi", ".mkv", ".gif" }
        .Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    private PortraitVideoConverter.Options Options() => new(RemoveGreen: _removeGreen.Checked, Crop: _crop.Crop);

    private void SetStatus(string message) => _status.Text = ResultMessage = message;

    private void SetBusy(bool busy)
    {
        _choose.Enabled = _removeGreen.Enabled = _restore.Enabled = _crop.Enabled = _zoom.Enabled = !busy;
        _views.Enabled = _viewHost.Enabled = !busy;
        _replace.Enabled = !busy && _sourceReady;
        _close.Text = busy ? "取消" : "关闭";
        _close.Enabled = true;
    }

    private async Task LoadSourceAsync(string input)
    {
        if (_operation != null) return;
        if (!IsSource(input) || !File.Exists(input)) { SetStatus("请选择视频或 GIF 文件。"); return; }
        _input = input;
        _sourceReady = false;
        _operation = new CancellationTokenSource();
        SetBusy(true);
        try
        {
            Directory.CreateDirectory(_work);
            SetStatus("正在读取视频取景…");
            string framePath = Path.Combine(_work, "source.png");
            await PortraitVideoConverter.SourceFrameAsync(input, framePath, PortraitVideoSettings.Load(), _operation.Token);
            using (var frame = Image.FromFile(framePath))
            {
                _sourceImage?.Dispose();
                _sourceImage = new Bitmap(frame);
            }
            _crop.SetSource(_sourceImage, new Size(_info.FrameWidth, _info.FrameHeight));
            _zoom.Value = 100;
            _views.SelectedIndex = 0;
            _previewCurrent = false;
            _sourceReady = true;
            SetStatus($"取景已就绪 · 将采样为 {_info.FrameNames.Count} 帧 · 尚未写入");
        }
        catch (OperationCanceledException) { SetStatus("预览已取消 · 未写入"); }
        catch (Exception ex) { SetStatus("读取失败 · 未写入：" + ex.Message); }
        finally { _operation.Dispose(); _operation = null; SetBusy(false); }
    }

    private async Task GeneratePreviewAsync(CancellationToken token)
    {
        string path = Path.Combine(_work, "preview.gif");
        await PortraitVideoConverter.PreviewAnimationAsync(_input, path, PortraitVideoSettings.Load(), Options(), token);
        ShowPreview(path);
        _previewCurrent = true;
    }

    private async Task UpdatePreviewAsync()
    {
        _operation = new CancellationTokenSource();
        SetBusy(true);
        try
        {
            SetStatus("正在生成裁剪后的动态预览…");
            await GeneratePreviewAsync(_operation.Token);
            SetStatus($"预览已就绪 · 实际写入会采样为 {_info.FrameNames.Count} 帧");
        }
        catch (OperationCanceledException) { SetStatus("预览已取消"); }
        catch (Exception ex) { SetStatus("预览失败：" + ex.Message); }
        finally { _operation.Dispose(); _operation = null; SetBusy(false); }
    }

    private async Task ReplaceAsync()
    {
        if (_operation != null || !_sourceReady) return;
        if (MessageBox.Show(this,
            $"将视频采样为 {_info.FrameNames.Count} 帧并替换 {_asset.Name}。\n只修改当前技能动画 Bundle，不修改游戏 DLL。操作前会备份。\n\n继续？",
            "替换技能动画", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
        _operation = new CancellationTokenSource();
        SetBusy(true);
        try
        {
            PortraitReplacement.EnsureGameClosed();
            if (!_previewCurrent) await GeneratePreviewAsync(_operation.Token);
            _writing = true;
            _close.Enabled = false;
            var progress = new Progress<string>(message => _status.Text = message);
            await SkillAnimationEngine.ReplaceAsync(_asset.BundlePath, _asset.PathId, _asset.Name, _input,
                _backupDir, _asset.BundleName, Path.Combine(_work, "write"), Options(), _operation.Token, progress);
            Replaced = true;
            Restored = false;
            _restore.Visible = true;
            SetStatus($"已写入并回读校验 · {_info.FrameNames.Count} 帧 · {Path.GetFileName(_input)}");
        }
        catch (OperationCanceledException) { SetStatus("已取消 · 未写入本次视频"); }
        catch (Exception ex) { SetStatus("替换未完成：" + ex.Message); }
        finally { _writing = false; _operation.Dispose(); _operation = null; SetBusy(false); }
    }

    private async Task RestoreAsync()
    {
        if (_operation != null) return;
        if (MessageBox.Show(this, "将当前整个技能动画 Bundle 恢复到本工具首次备份的版本。继续？",
            "恢复当前Bundle", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
        _operation = new CancellationTokenSource();
        _writing = true;
        SetBusy(true);
        _close.Enabled = false;
        try
        {
            PortraitReplacement.EnsureGameClosed();
            bool restored = await Task.Run(() => new ModEngine().RestoreFromBackup(_asset.BundlePath, _backupDir, _asset.BundleName));
            if (!restored) throw new IOException("没有找到当前资源包的备份。");
            await Task.Run(() => SkillAnimationEngine.Inspect(_asset.BundlePath, _asset.PathId, _asset.Name));
            Replaced = false;
            Restored = true;
            SetStatus("已恢复当前技能动画资源包");
        }
        catch (Exception ex) { SetStatus("恢复未完成：" + ex.Message); }
        finally { _writing = false; _operation.Dispose(); _operation = null; SetBusy(false); }
    }

    private void ShowPreview(string path)
    {
        var old = _preview.Image;
        _preview.Image = null;
        old?.Dispose();
        _previewStream?.Dispose();
        _previewStream = new MemoryStream(File.ReadAllBytes(path));
        _preview.Image = Image.FromStream(_previewStream);
    }

    private void Cleanup()
    {
        var old = _preview.Image;
        _preview.Image = null;
        old?.Dispose();
        _previewStream?.Dispose();
        _sourceImage?.Dispose();
        _sourceImage = null;
        try { if (Directory.Exists(_work)) Directory.Delete(_work, true); } catch (IOException) { }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _tooltips.Dispose();
        base.Dispose(disposing);
    }
}
