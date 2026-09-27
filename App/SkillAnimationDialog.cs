namespace JixModMaker;

public sealed class SkillAnimationDialog : Form
{
    private readonly TexRef _asset;
    private readonly SkillAnimationInfo _info;
    private SkillMovieInfo _movie;
    private int TargetWidth => _movie?.Timing.Width ?? _info.FrameWidth;
    private int TargetHeight => _movie?.Timing.Height ?? _info.FrameHeight;
    private int FrameCount => _movie?.Timing.TotalFrames ?? _info.FrameNames.Count;
    private string KindLabel => _movie != null ? "技能特写" : "Q版动作";
    private readonly string _backupDir;
    private readonly string _initialFile;
    private readonly string _work = Path.Combine(Path.GetTempPath(), "JixSkillAnimation-" + Guid.NewGuid().ToString("N"));
    private readonly PictureBox _preview = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Theme.PicBg };
    private readonly Label _status = new() { Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(0, 8, 0, 8) };
    private readonly CheckBox _removeGreen = new() { Text = "去绿幕", AutoSize = true };
    private readonly Button _choose = Theme.FlatButton("选择视频");
    private readonly Button _replace = Theme.FlatButton("替换技能动画");
    private readonly Button _restore = Theme.FlatButton("还原当前资源包");
    private readonly Button _close = Theme.FlatButton("关闭");
    private readonly VideoCropControl _crop = new() { Dock = DockStyle.Fill };
    private readonly PortraitViewTabs _views = new() { Dock = DockStyle.Top };
    private readonly Panel _viewHost = new() { Dock = DockStyle.Fill };
    private readonly Panel _cropPage = new() { Dock = DockStyle.Fill };
    private readonly TrackBar _zoom = new() { Minimum = 100, Maximum = 400, Value = 100, TickStyle = TickStyle.None, Width = 160, Height = 30 };
    private readonly Label _dimensions = new() { AutoSize = true, Margin = new Padding(8) };
    private readonly ToolTip _tooltips = new();
    private readonly Label _timing = new() { AutoSize = true, Padding = new Padding(0, 0, 0, 10), ForeColor = Theme.Cyan };
    private double? _sourceDuration;
    private Image _sourceImage;
    private MemoryStream _previewStream;
    private CancellationTokenSource _operation;
    private bool _writing, _sourceReady, _previewCurrent;
    private string _input;

    public bool Replaced { get; private set; }
    public bool Restored { get; private set; }
    public string ResultMessage { get; private set; } = "尚未替换";

    public SkillAnimationDialog(TexRef asset, SkillMovieInfo movie, string backupDir, string initialFile = null)
        : this(asset, null, backupDir, initialFile, movie) { }

    public SkillAnimationDialog(TexRef asset, SkillAnimationInfo info, string backupDir, string initialFile = null, SkillMovieInfo movie = null)
    {
        _asset = asset ?? throw new ArgumentNullException(nameof(asset));
        _info = info;
        _movie = movie;
        if (info == null && movie == null) throw new ArgumentNullException(nameof(info));
        _backupDir = backupDir;
        _initialFile = initialFile;
        Text = "替换" + KindLabel + " - " + AppBuildInfo.ReleaseLabel;
        _replace.Text = "替换" + KindLabel;
        Font = Theme.UI(9f);
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(800, 640);
        MinimumSize = new Size(620, 460);
        StartPosition = FormStartPosition.CenterParent;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), RowCount = 5, ColumnCount = 1 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var header = new Label
        {
            Text = $"{asset.Display ?? asset.Name}   ·   {FrameCount} 帧   ·   {TargetWidth}×{TargetHeight}",
            AutoSize = true,
            Padding = new Padding(0, 0, 0, 12)
        };
        layout.Controls.Add(header, 0, 0);
        _timing.Visible = _movie != null;
        UpdateTimingNotice();
        layout.Controls.Add(_timing, 0, 1);

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
        layout.Controls.Add(workspace, 0, 2);
        _preview.Visible = false;

        center.Click += (_, _) => _crop.CenterCrop();
        _tooltips.SetToolTip(_zoom, "取景缩放");
        _tooltips.SetToolTip(_crop, "拖动取景框调整位置");
        _zoom.ValueChanged += (_, _) => _crop.SetZoom(_zoom.Value / 100f);
        _crop.CropChanged += (_, _) =>
        {
            _previewCurrent = false;
            if (_sourceImage != null)
                _dimensions.Text = $"{(int)Math.Round(_crop.Crop.Width * _sourceImage.Width)} × {(int)Math.Round(_crop.Crop.Height * _sourceImage.Height)}  /  {TargetWidth}:{TargetHeight}";
        };
        _views.SelectedIndexChanged += async (_, _) =>
        {
            _cropPage.Visible = _views.SelectedIndex == 0;
            _preview.Visible = _views.SelectedIndex == 1;
            if (_views.SelectedIndex == 1 && _sourceReady && !_previewCurrent && _operation == null)
                await UpdatePreviewAsync();
        };

        layout.Controls.Add(_status, 0, 3);
        var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
        footer.Controls.AddRange(new Control[] { _close, _replace, _choose, _removeGreen, _restore });
        layout.Controls.Add(footer, 0, 4);
        Controls.Add(layout);
        layout.SizeChanged += (_, _) =>
        {
            var maximum = new Size(Math.Max(100, layout.ClientSize.Width - layout.Padding.Horizontal - 8), 0);
            _status.MaximumSize = _timing.MaximumSize = header.MaximumSize = maximum;
        };

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
            SetStatus("尚未选择视频 · " + KindLabel + " · 仅修改当前资源包");
            if (_initialFile != null) await LoadSourceAsync(_initialFile);
        };
    }

    private static bool IsSource(string path) => new[] { ".mp4", ".mov", ".webm", ".avi", ".mkv", ".gif" }
        .Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    private PortraitVideoConverter.Options Options() => new(RemoveGreen: _removeGreen.Checked, Crop: _crop.Crop);

    public static bool WillTrim(NativeMovieTiming timing, double? sourceSeconds)
        => timing != null && sourceSeconds.HasValue && sourceSeconds.Value > timing.Duration + .02;

    public static string TimingNotice(NativeMovieTiming timing, double? sourceSeconds)
    {
        if (timing == null) return "";
        string original = $"原技能时长：{timing.Duration:0.###} 秒（{timing.TotalFrames} 帧，{timing.FramerateN / (double)timing.FramerateD:0.###} FPS）";
        string source = sourceSeconds.HasValue ? $"输入文件约 {sourceSeconds:0.###} 秒。" : "";
        string change = WillTrim(timing, sourceSeconds)
            ? $"将截掉约 {sourceSeconds.Value - timing.Duration:0.###} 秒；仅保留开头 {timing.Duration:0.###} 秒。"
            : $"输出固定 {timing.Duration:0.###} 秒，超出截断，不足停留末帧。";
        return original + "\n" + source + change;
    }

    private void UpdateTimingNotice()
    {
        _timing.Text = TimingNotice(_movie?.Timing, _sourceDuration);
        _timing.ForeColor = WillTrim(_movie?.Timing, _sourceDuration) ? Theme.Accent : Theme.Cyan;
    }

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
        if (!VideoRuntimeDialog.EnsureReady(this)) { SetStatus("视频组件尚未启用 · 未写入"); return; }
        _input = input;
        _sourceDuration = null;
        UpdateTimingNotice();
        _sourceReady = false;
        _operation = new CancellationTokenSource();
        SetBusy(true);
        try
        {
            Directory.CreateDirectory(_work);
            if (_movie != null) await PortraitVideoConverter.EnsureConversionAvailableAsync(PortraitVideoSettings.Load(), _operation.Token);
            SetStatus("正在读取视频取景…");
            string framePath = Path.Combine(_work, "source.png");
            await PortraitVideoConverter.SourceFrameAsync(input, framePath, PortraitVideoSettings.Load(), _operation.Token);
            if (_movie != null)
            {
                _sourceDuration = await PortraitVideoConverter.ProbeDurationAsync(input, PortraitVideoSettings.Load(), _operation.Token);
                UpdateTimingNotice();
            }
            using (var frame = Image.FromFile(framePath))
            {
                _sourceImage?.Dispose();
                _sourceImage = new Bitmap(frame);
            }
            _crop.SetSource(_sourceImage, new Size(TargetWidth, TargetHeight));
            _zoom.Value = 100;
            _views.SelectedIndex = 0;
            _previewCurrent = false;
            _sourceReady = true;
            SetStatus(_movie == null ? $"取景已就绪 · 将采样为 {FrameCount} 帧 · 尚未写入"
                : $"取景已就绪 · 保留原生 {_movie.Timing.Duration:0.##} 秒；超出截断，不足停留末帧 · 尚未写入");
        }
        catch (OperationCanceledException) { SetStatus("预览已取消 · 未写入"); }
        catch (Exception ex) { SetStatus("读取失败 · 未写入：" + ex.Message); }
        finally { _operation.Dispose(); _operation = null; SetBusy(false); }
    }

    private async Task GeneratePreviewAsync(CancellationToken token)
    {
        string path = Path.Combine(_work, "preview.gif");
        await PortraitVideoConverter.PreviewAnimationAsync(_input, path, PortraitVideoSettings.Load(), Options(), token, _movie?.Timing);
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
            SetStatus($"预览已就绪 · 实际写入 {FrameCount} 帧 · {TargetWidth}×{TargetHeight}");
        }
        catch (OperationCanceledException) { SetStatus("预览已取消"); }
        catch (Exception ex) { SetStatus("预览失败：" + ex.Message); }
        finally { _operation.Dispose(); _operation = null; SetBusy(false); }
    }

    private async Task ReplaceAsync()
    {
        if (_operation != null || !_sourceReady) return;
        if (MessageBox.Show(this,
            $"将视频写入 {_asset.Name}。\n只修改当前{KindLabel} Bundle，不修改游戏 DLL。操作前会备份。\n\n" +
            (_movie == null ? "" : TimingNotice(_movie.Timing, _sourceDuration) + "\n\n") + "继续？",
            "替换" + KindLabel, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning,
            _movie != null ? MessageBoxDefaultButton.Button2 : MessageBoxDefaultButton.Button1) != DialogResult.OK) return;
        _operation = new CancellationTokenSource();
        SetBusy(true);
        try
        {
            PortraitReplacement.EnsureGameClosed();
            if (!_previewCurrent) await GeneratePreviewAsync(_operation.Token);
            _writing = true;
            _close.Enabled = false;
            var progress = new Progress<string>(message => _status.Text = message);
            string write = Path.Combine(_work, "write-" + Guid.NewGuid().ToString("N"));
            if (_movie != null)
            {
                await SkillMovieEngine.ReplaceAsync(_asset, _movie, _input, _backupDir, write, Options(), _operation.Token, progress);
                _movie = _movie with { BundleHash = SkillMovieEngine.Hash(_asset.BundlePath) };
            }
            else await SkillAnimationEngine.ReplaceAsync(_asset.BundlePath, _asset.PathId, _asset.Name, _input,
                _backupDir, _asset.BundleName, write, Options(), _operation.Token, progress);
            Replaced = true;
            Restored = false;
            _restore.Visible = true;
            SetStatus($"已写入并回读校验 · {FrameCount} 帧 · {Path.GetFileName(_input)} · 游戏内效果待确认");
        }
        catch (OperationCanceledException) { SetStatus("已取消 · 未写入本次视频"); }
        catch (Exception ex) { SetStatus("替换未完成：" + ex.Message); }
        finally { _writing = false; _operation.Dispose(); _operation = null; SetBusy(false); }
    }

    private async Task RestoreAsync()
    {
        if (_operation != null) return;
        if (MessageBox.Show(this, "将当前整个" + KindLabel + " Bundle 恢复到本工具首次备份的版本。继续？",
            "还原当前资源包", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
        _operation = new CancellationTokenSource();
        _writing = true;
        SetBusy(true);
        _close.Enabled = false;
        try
        {
            PortraitReplacement.EnsureGameClosed();
            if (_movie != null && SkillMovieEngine.Hash(_asset.BundlePath) != _movie.BundleHash)
                throw new IOException("资源包已更新，请重新打开窗口后再恢复。");
            bool restored = _movie != null
                ? await Task.Run(() => SkillMovieEngine.Restore(_asset, _movie, _backupDir))
                : await Task.Run(() => new ModEngine().RestoreFromBackup(_asset.BundlePath, _backupDir, _asset.BundleName));
            if (!restored) throw new IOException("没有找到当前资源包的备份。");
            if (_movie != null) _movie = await SkillMovieEngine.InspectAsync(_asset.BundlePath, _asset.Name, _operation.Token);
            else await Task.Run(() => SkillAnimationEngine.Inspect(_asset.BundlePath, _asset.PathId, _asset.Name));
            Replaced = false;
            Restored = true;
            SetStatus("已恢复当前" + KindLabel + "资源包");
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
