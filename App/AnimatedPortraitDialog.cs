namespace JixModMaker;

public sealed class AnimatedPortraitDialog : Form
{
    private readonly string _root, _texture, _initialFile;
    private readonly Size _targetSize;
    private readonly string _work = Path.Combine(Path.GetTempPath(), "JixDragVideo-" + Guid.NewGuid().ToString("N"));
    private readonly PictureBox _preview = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(48, 48, 52) };
    private readonly Label _status = new() { Dock = DockStyle.Fill, AutoSize = true, Text = "尚未替换", Padding = new Padding(0, 8, 0, 8) };
    private readonly CheckBox _removeGreen = new() { Text = "去绿幕", AutoSize = true, Checked = false };
    private readonly Button _choose = Theme.FlatButton("选择视频");
    private readonly Button _replace = Theme.FlatButton("替换");
    private readonly Button _restore = Theme.FlatButton("恢复原资源");
    private readonly Button _export = Theme.FlatButton("导出 ZIP");
    private readonly Button _cancel = Theme.FlatButton("关闭");
    private readonly VideoCropControl _crop = new() { Dock = DockStyle.Fill };
    private readonly PortraitViewTabs _views = new() { Dock = DockStyle.Top };
    private readonly Panel _viewHost = new() { Dock = DockStyle.Fill };
    private readonly Panel _cropPage = new() { Dock = DockStyle.Fill };
    private readonly TrackBar _zoom = new() { Minimum = 100, Maximum = 400, Value = 100, TickStyle = TickStyle.None, Width = 160, Height = 30, AccessibleName = "取景缩放" };
    private readonly ToolTip _tooltips = new();
    private readonly Label _dimensions = new() { AutoSize = true, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(8) };
    private Image _sourceImage;
    private MemoryStream _imageStream;
    private CancellationTokenSource _operation;
    private bool _writing, _previewReady, _animationCurrent;
    private string _lastInput;
    public string ResultMessage { get; private set; } = "尚未替换";

    public AnimatedPortraitDialog(string root, string texture, string initialFile = null, Size? targetSize = null)
    {
        _root = root;
        _texture = texture;
        _initialFile = initialFile;
        _targetSize = targetSize ?? new Size(880, 1205);
        if (_targetSize.Width <= 0 || _targetSize.Height <= 0) throw new ArgumentOutOfRangeException(nameof(targetSize));
        Text = "替换为视频 / GIF";
        Font = Theme.UI(9f);
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(800, 640);
        MinimumSize = new Size(600, 440);
        StartPosition = FormStartPosition.CenterParent;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), RowCount = 4, ColumnCount = 1 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label { Text = texture, AutoSize = true, Padding = new Padding(0, 0, 0, 12) }, 0, 0);
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
            _animationCurrent = false;
            if (_sourceImage != null)
                _dimensions.Text = $"{(int)Math.Round(_crop.Crop.Width * _sourceImage.Width)} × {(int)Math.Round(_crop.Crop.Height * _sourceImage.Height)}  /  {_targetSize.Width}:{_targetSize.Height}";
        };
        _views.SelectedIndexChanged += async (_, _) =>
        {
            _cropPage.Visible = _views.SelectedIndex == 0;
            _preview.Visible = _views.SelectedIndex == 1;
            if (_views.SelectedIndex == 1 && _previewReady && !_animationCurrent && _operation == null) await UpdateAnimationAsync();
        };
        layout.Controls.Add(_status, 0, 2);
        var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
        footer.Controls.AddRange(new Control[] { _cancel, _replace, _choose, _removeGreen, _export, _restore });
        layout.Controls.Add(footer, 0, 3);
        Controls.Add(layout);
        layout.SizeChanged += (_, _) => _status.MaximumSize = new Size(Math.Max(100, layout.ClientSize.Width - layout.Padding.Horizontal - 8), 0);
        _replace.Enabled = false;
        _replace.BackColor = Theme.Accent;
        _restore.Visible = _export.Visible = false;
        foreach (Control surface in new Control[] { this, _preview, _crop, layout })
        {
            surface.AllowDrop = true;
            surface.DragEnter += (_, e) => e.Effect = _operation == null && e.Data.GetData(DataFormats.FileDrop) is string[] { Length: 1 } files && IsVideo(files[0])
                ? DragDropEffects.Copy : DragDropEffects.None;
            surface.DragDrop += async (_, e) =>
            {
                if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: 1 } files) await PreviewAsync(files[0]);
            };
        }
        _choose.Click += async (_, _) =>
        {
            using var picker = new OpenFileDialog { Filter = "视频 / GIF|*.mp4;*.mov;*.webm;*.avi;*.mkv;*.gif" };
            if (picker.ShowDialog(this) == DialogResult.OK) await PreviewAsync(picker.FileName);
        };
        _replace.Click += async (_, _) => await ReplaceAsync();
        _restore.Click += async (_, _) => await RestoreAsync();
        _export.Click += async (_, _) => await ExportAsync();
        _cancel.Click += (_, _) => { if (_operation != null) _operation.Cancel(); else Close(); };
        _removeGreen.CheckedChanged += async (_, _) =>
        {
            _animationCurrent = false;
            if (_lastInput != null && _operation == null && _views.SelectedIndex == 1) await UpdateAnimationAsync();
        };
        FormClosing += (_, e) => { if (_operation != null) { e.Cancel = true; if (!_writing) _operation.Cancel(); } };
        FormClosed += (_, _) =>
        {
            var old = _preview.Image;
            _preview.Image = null;
            old?.Dispose();
            _imageStream?.Dispose();
            _sourceImage?.Dispose();
            _sourceImage = null;
            try { if (Directory.Exists(_work)) Directory.Delete(_work, true); } catch (IOException) { }
        };
        Shown += async (_, _) =>
        {
            var area = Screen.FromControl(this).WorkingArea;
            if (Height > area.Height) Height = area.Height;
            if (Width > area.Width) Width = area.Width;
            var receipt = PortraitReplacement.ReadReceipt(_root, _texture);
            if (receipt?.Texture == _texture)
            {
                bool installed = await Task.Run(() => PortraitReplacement.IsInstalled(_root, receipt));
                if (IsDisposed) return;
                _restore.Visible = _export.Visible = installed;
                _removeGreen.Checked = receipt.RemoveGreen;
                SetStatus(installed ? "已写入并校验 · " + receipt.SourceName + " · 游戏内效果待确认" : "旧替换记录已失效：资源已更新或被重置");
                if (installed && File.Exists(receipt.Preview)) { ShowPreview(receipt.Preview); _views.SelectedIndex = 1; }
            }
            if (_initialFile != null && !IsDisposed) await PreviewAsync(_initialFile);
        };
    }

    public static bool IsVideo(string path) => new[] { ".mp4", ".mov", ".webm", ".avi", ".mkv", ".gif", ".usm" }
        .Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // Stop PictureBox's animation callbacks before disposing its native handle.
            var old = _preview.Image;
            _preview.Image = null;
            old?.Dispose();
            _imageStream?.Dispose();
            _imageStream = null;
            _sourceImage?.Dispose();
            _sourceImage = null;
            _tooltips.Dispose();
        }
        base.Dispose(disposing);
    }

    private void SetStatus(string message) => _status.Text = ResultMessage = message;
    private void SetBusy(bool busy)
    {
        _choose.Enabled = _removeGreen.Enabled = _restore.Enabled = _export.Enabled = _crop.Enabled = _zoom.Enabled = !busy;
        _views.Enabled = !busy;
        _viewHost.Enabled = !busy;
        _replace.Enabled = !busy && _previewReady;
        _cancel.Text = busy ? "取消" : "关闭";
        _cancel.Enabled = true;
    }
    private void ShowPreview(string path)
    {
        var old = _preview.Image;
        _preview.Image = null;
        old?.Dispose();
        _imageStream?.Dispose();
        _imageStream = new MemoryStream(File.ReadAllBytes(path));
        _preview.Image = Image.FromStream(_imageStream);
    }

    private async Task PreviewAsync(string input)
    {
        if (_operation != null) return;
        if (!IsVideo(input) || !File.Exists(input)) { SetStatus("请选择一个视频或 GIF 文件。"); return; }
        _lastInput = input;
        _previewReady = false;
        _operation = new CancellationTokenSource();
        SetBusy(true);
        try
        {
            Directory.CreateDirectory(_work);
            if (Path.GetExtension(input).Equals(".usm", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("请拖入原始视频或 GIF，以便校验动态预览。");
            SetStatus("正在读取视频取景…");
            string framePath = Path.Combine(_work, "source.png");
            await PortraitVideoConverter.SourceFrameAsync(input, framePath, PortraitVideoSettings.Load(), _operation.Token);
            using (var frame = Image.FromFile(framePath))
            {
                _sourceImage?.Dispose();
                _sourceImage = new Bitmap(frame);
            }
            _crop.SetSource(_sourceImage, _targetSize);
            _zoom.Value = 100;
            _views.SelectedIndex = 0;
            _animationCurrent = false;
            _previewReady = true;
            SetStatus("取景已就绪 · 尚未写入本次视频");
        }
        catch (OperationCanceledException) { SetStatus("预览已取消 · 未写入本次视频"); }
        catch (Exception ex) { SetStatus("预览失败 · 未写入本次视频：" + ex.Message); }
        finally { _operation.Dispose(); _operation = null; SetBusy(false); }
    }

    private PortraitVideoConverter.Options ConversionOptions() => new(RemoveGreen: _removeGreen.Checked, Crop: _crop.Crop);

    private async Task GenerateAnimationAsync(CancellationToken token)
    {
        await PortraitVideoConverter.PreviewAnimationAsync(_lastInput, Path.Combine(_work, "preview.gif"), PortraitVideoSettings.Load(), ConversionOptions(), token);
        ShowPreview(Path.Combine(_work, "preview.gif"));
        _animationCurrent = true;
    }

    private async Task UpdateAnimationAsync()
    {
        _operation = new CancellationTokenSource();
        SetBusy(true);
        try
        {
            SetStatus("正在生成裁剪后的动态预览…");
            await GenerateAnimationAsync(_operation.Token);
            SetStatus("预览已就绪 · 前 6 秒 · 尚未写入本次视频");
        }
        catch (OperationCanceledException) { SetStatus("预览已取消"); }
        catch (Exception ex) { SetStatus("预览失败：" + ex.Message); }
        finally { _operation.Dispose(); _operation = null; SetBusy(false); }
    }

    private async Task ReplaceAsync()
    {
        if (_operation != null || !_previewReady) return;
        if (MessageBox.Show(this, "将使用游戏已有的视频播放器，并占用异画 VHandCard_13021002 的视频槽位。\n原静态贴图不会改动，操作前会备份两个资源包。\n\n继续替换？", "动态立绘替换",
            MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
        _operation = new CancellationTokenSource();
        SetBusy(true);
        try
        {
            var token = _operation.Token;
            PortraitReplacement.EnsureGameClosed();
            SetStatus("正在定位目标资源…");
            var found = await Task.Run(() => AnimatedPortraitPatch.FindBundles(_root, token), token);
            if (found.Runtime == null || found.Video == null)
                throw new InvalidDataException("缺少热更新程序集或 VHandCard_13021002 视频资源，请先让游戏完成资源下载。");
            if (!_animationCurrent) await GenerateAnimationAsync(token);
            string usm = await PortraitVideoConverter.ConvertAsync(_lastInput, _work, PortraitVideoSettings.Load(), ConversionOptions(), token,
                new Progress<string>(message => _status.Text = message));
            token.ThrowIfCancellationRequested();
            _writing = true;
            _cancel.Enabled = false;
            SetStatus("正在验证类型结构、备份并写入…");
            bool removeGreen = _removeGreen.Checked;
            await Task.Run(() => PortraitReplacement.Apply(new(_root, found.Runtime, found.Video, _texture, usm, ""),
                Path.Combine(_work, "preview.gif"), Path.GetFileName(_lastInput), removeGreen));
            _restore.Visible = _export.Visible = true;
            SetStatus("已替换原生视频槽位并回读校验 · " + Path.GetFileName(_lastInput) + "\n当前为本地动态预览，游戏内显示待确认。");
        }
        catch (OperationCanceledException) { SetStatus("已取消 · 未写入本次视频"); }
        catch (Exception ex) { SetStatus("替换未完成：" + ex.Message); }
        finally { _writing = false; _operation.Dispose(); _operation = null; SetBusy(false); }
    }

    private async Task RestoreAsync()
    {
        if (_operation != null) return;
        if (MessageBox.Show(this, "恢复到首次动态替换前的两个资源包？", "恢复原资源", MessageBoxButtons.OKCancel) != DialogResult.OK) return;
        _operation = new CancellationTokenSource();
        _writing = true;
        SetBusy(true);
        _cancel.Enabled = false;
        try
        {
            await Task.Run(() => PortraitReplacement.Restore(_root));
            _restore.Visible = _export.Visible = false;
            var old = _preview.Image;
            _preview.Image = null;
            old?.Dispose();
            _imageStream?.Dispose();
            _imageStream = null;
            _previewReady = false;
            SetStatus("已恢复到首次动态替换前的资源");
        }
        catch (Exception ex) { SetStatus("恢复未完成：" + ex.Message); }
        finally { _writing = false; _operation.Dispose(); _operation = null; SetBusy(false); }
    }

    private async Task ExportAsync()
    {
        if (_operation != null) return;
        _operation = new CancellationTokenSource();
        SetBusy(true);
        try
        {
            var receipt = PortraitReplacement.ReadReceipt(_root);
            if (!await Task.Run(() => PortraitReplacement.IsInstalled(_root, receipt)))
                throw new IOException("资源已改变，不能导出旧的替换包。");
            using var save = new SaveFileDialog { Filter = "直接替换 ZIP|*.zip", FileName = _texture + "-动态替换.zip" };
            if (save.ShowDialog(this) != DialogResult.OK) return;
            string history = Path.GetFullPath(Path.Combine(_root, "_原始备份", "动态立绘")) + Path.DirectorySeparatorChar;
            if (Path.GetFullPath(save.FileName).StartsWith(history, StringComparison.OrdinalIgnoreCase))
                throw new IOException("请导出到备份目录之外，避免覆盖恢复文件。");
            _operation.Token.ThrowIfCancellationRequested();
            _writing = true;
            _cancel.Enabled = false;
            await Task.Run(() => File.Copy(receipt.ReplacementZip, save.FileName, true));
            SetStatus("已导出直接替换 ZIP：" + Path.GetFileName(save.FileName));
        }
        catch (OperationCanceledException) { SetStatus("已取消导出"); }
        catch (Exception ex) { SetStatus("导出失败：" + ex.Message); }
        finally { _writing = false; _operation.Dispose(); _operation = null; SetBusy(false); }
    }
}
