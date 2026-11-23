namespace JixModMaker;

public sealed class MobileExportDialog : Form
{
    private readonly Dictionary<string, MobileTexturePackage.ImageSource> _sources = new(StringComparer.Ordinal);
    private readonly HashSet<string> _selected = new(StringComparer.Ordinal);
    private readonly ListView _list = new() { Dock = DockStyle.Fill, View = View.Details, CheckBoxes = true, FullRowSelect = true };
    private readonly TextBox _search = new() { Width = 220, PlaceholderText = UiText.T("搜索贴图 / 角色编号") };
    private readonly TextBox _apk = new() { Dock = DockStyle.Fill, ReadOnly = true };
    private readonly TextBox _catalog = new() { Dock = DockStyle.Fill, ReadOnly = true };
    private readonly TextBox _status = new() { Dock = DockStyle.Fill, ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Vertical };
    private readonly List<Button> _buttons = new();
    private AndroidModInstaller _device;
    private AndroidModInstaller.Snapshot _snapshot;
    private string _zip;
    private bool _busy, _refreshing;

    public MobileExportDialog(IEnumerable<MobileTexturePackage.ImageSource> sources)
    {
        WindowLayout.Configure(this);
        Text = UiText.T("安卓贴图 / 导出与校验安装");
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(900, 680);
        MinimumSize = new Size(640, 480);
        Font = Theme.UI(9);
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        _list.BackColor = Theme.Card;
        _list.ForeColor = Theme.Text;
        _list.Columns.Add(UiText.T("贴图"), 340);
        _list.Columns.Add(UiText.T("图片来源"), 450);
        _list.SizeChanged += (_, _) =>
        {
            int width = Math.Max(1, _list.ClientSize.Width - SystemInformation.VerticalScrollBarWidth);
            _list.Columns[0].Width = width / 2;
            _list.Columns[1].Width = width - _list.Columns[0].Width;
        };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(10) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 75));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 25));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var paths = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 3, RowCount = 2 };
        paths.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        paths.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        paths.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        paths.Controls.Add(new Label { Text = UiText.T("游戏 APK"), AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        paths.Controls.Add(_apk, 1, 0);
        paths.Controls.Add(Button(UiText.T("选择 APK"), (_, _) => Pick(_apk, "Android APK|*.apk")), 2, 0);
        paths.Controls.Add(new Label { Text = UiText.T("当前资源表"), AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
        paths.Controls.Add(_catalog, 1, 1);
        paths.Controls.Add(Button(UiText.T("选择资源表"), (_, _) => Pick(_catalog, "Addressables catalog|catalog_*.json")), 2, 1);
        var actions = Flow();
        actions.Controls.Add(Button(UiText.T("读取手机资源"), async (_, _) => await ReadPhone()));
        actions.Controls.Add(Button(UiText.T("添加图包"), async (_, _) => await AddPack()));
        actions.Controls.Add(_search);
        actions.Controls.Add(Button(UiText.T("全选结果"), (_, _) => { foreach (ListViewItem item in _list.Items) item.Checked = true; }));
        actions.Controls.Add(Button(UiText.T("清空选择"), (_, _) => { _selected.Clear(); RefreshRows(); }));
        var footer = Flow();
        footer.Controls.Add(Button(UiText.T("导出安卓 ZIP"), async (_, _) => await Export()));
        footer.Controls.Add(Button(UiText.T("安装 ZIP 并校验"), async (_, _) => await Install()));
        footer.Controls.Add(Button(UiText.T("还原安装"), async (_, _) => await Restore()));
        layout.Controls.Add(paths, 0, 0);
        layout.Controls.Add(actions, 0, 1);
        layout.Controls.Add(_list, 0, 2);
        layout.Controls.Add(_status, 0, 3);
        layout.Controls.Add(footer, 0, 4);
        Controls.Add(layout);
        foreach (var control in new TextBox[] { _apk, _catalog, _status, _search }) { control.BackColor = Theme.Card; control.ForeColor = Theme.Text; }
        _search.TextChanged += (_, _) => RefreshRows();
        _list.ItemChecked += (_, e) =>
        {
            if (_refreshing || e.Item.Tag is not string name) return;
            if (e.Item.Checked) _selected.Add(name); else _selected.Remove(name);
        };
        FormClosing += (_, e) => { if (_busy) { e.Cancel = true; Log(UiText.T("当前操作尚未结束，请稍候再关闭。")); } };
        AddSources(sources);
        Log(UiText.F($"已载入 {_sources.Count} 张候选贴图。尚未写入手机。"));
    }

    private static FlowLayoutPanel Flow() => new() { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, Padding = new Padding(0, 5, 0, 5) };
    private Button Button(string text, EventHandler action)
    {
        var button = Theme.FlatButton(text);
        button.AutoSize = true;
        button.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        button.MinimumSize = new Size(0, 36);
        button.Margin = new Padding(0, 0, 8, 6);
        button.Click += action;
        _buttons.Add(button);
        return button;
    }

    private void Pick(TextBox target, string filter)
    {
        using var dialog = new OpenFileDialog { Filter = filter };
        if (dialog.ShowDialog(this) == DialogResult.OK) target.Text = dialog.FileName;
    }

    private async Task ReadPhone() => await Work(async () =>
    {
        var progress = new Progress<string>(Log);
        var result = await Task.Run(() =>
        {
            var device = new AndroidModInstaller { Progress = message => ((IProgress<string>)progress).Report(message) };
            return (Device: device, Snapshot: device.ReadCurrent(Path.Combine(DataRoot, "mobile-sources")));
        });
        _device = result.Device;
        _snapshot = result.Snapshot;
        _apk.Text = _snapshot.Apk;
        _catalog.Text = _snapshot.Catalog;
        Log(UiText.T("已读取手机 ") + _snapshot.Serial + UiText.T("，游戏 ") + _snapshot.Version + UiText.T("。未修改游戏。"));
    });

    private async Task AddPack()
    {
        using var dialog = new OpenFileDialog { Filter = UiText.T("贴图图包|*.jxpack"), Multiselect = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        await Work(async () =>
        {
            var sources = await Task.Run(() => dialog.FileNames.SelectMany(path => MobileTexturePackage.FromPack(path)).ToList());
            AddSources(sources);
            Log(UiText.T("已载入 ") + _sources.Count + UiText.T(" 张候选贴图。"));
        });
    }

    private async Task Export()
    {
        if (!File.Exists(_apk.Text) || !File.Exists(_catalog.Text)) { Log(UiText.T("请先读取手机资源，或选择匹配的 APK 和资源表。")); return; }
        var images = _selected.Select(name => _sources[name]).ToArray();
        if (images.Length == 0) { Log(UiText.T("尚未选择贴图。")); return; }
        using var dialog = new SaveFileDialog { Filter = UiText.T("安卓直接替换 ZIP|*.zip"), FileName = UiText.T("安卓贴图-") + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".zip", OverwritePrompt = false };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        string apk = _apk.Text, catalog = _catalog.Text, destination = dialog.FileName;
        await Work(async () =>
        {
            var progress = new Progress<string>(Log);
            var result = await Task.Run(() =>
            {
                if (_device != null && _snapshot != null && _device.Version() != _snapshot.Version)
                    throw new InvalidDataException(UiText.T("手机游戏已更新，请重新读取手机资源。"));
                return MobileTexturePackage.Build(apk, catalog, images, destination,
                    location => _device != null && _snapshot != null ? _device.ReadCache(_snapshot, location) : null,
                    message => ((IProgress<string>)progress).Report(message));
            });
            _zip = result.Zip;
            Log(UiText.F($"ZIP 回读校验通过：{result.Manifest.Textures.Count} 张贴图，{result.Manifest.Files.Count} 个文件。尚未安装。\r\n{_zip}"));
            if (result.Manifest.Missing.Count > 0) Log(UiText.T("当前版本没有这些贴图，未导出：") + string.Join("、", result.Manifest.Missing));
            if (result.Manifest.RequiredFiles?.Count > 0) Log(UiText.F($"这是增量包，保留并依赖手机已有的 {result.Manifest.RequiredFiles.Count} 个独立贴图文件；安装前会逐项核验，不能覆盖到没有这些前置资源的手机。"));
        });
    }

    private async Task Install()
    {
        using var dialog = new OpenFileDialog { Filter = UiText.T("安卓直接替换 ZIP|*.zip"), FileName = _zip };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        if (MessageBox.Show(this, UiText.T("请先退出手机上的吉星派对。将备份当前资源表，写入独立贴图，并读回校验。\n不修改 APK、DLL、系统权限或代理。"), UiText.T("安装安卓贴图"), MessageBoxButtons.OKCancel) != DialogResult.OK) return;
        await Work(async () =>
        {
            var progress = new Progress<string>(Log);
            var receipt = await Task.Run(() =>
            {
                var device = new AndroidModInstaller { Progress = message => ((IProgress<string>)progress).Report(message) };
                return device.Install(dialog.FileName, Path.Combine(DataRoot, "mobile-backups"));
            });
            Log(UiText.T("手机文件全部通过 SHA256 读回校验。游戏内显示仍需进入游戏确认。\r\n备份：") + receipt.BackupDirectory);
        });
    }

    private async Task Restore()
    {
        using var dialog = new OpenFileDialog { Filter = UiText.T("安卓安装记录|installation.json"), InitialDirectory = Path.Combine(DataRoot, "mobile-backups") };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        if (MessageBox.Show(this, UiText.T("请退出手机游戏。将按所选记录恢复资源表；版本或内容已变化时会停止。"), UiText.T("还原安卓安装"), MessageBoxButtons.OKCancel) != DialogResult.OK) return;
        await Work(async () =>
        {
            await Task.Run(() => new AndroidModInstaller().Restore(dialog.FileName));
            Log(UiText.T("资源表已还原并通过读回校验；其它 Mod 和系统设置未改动。"));
        });
    }

    private async Task Work(Func<Task> operation)
    {
        if (_busy) return;
        _busy = true;
        foreach (var button in _buttons) button.Enabled = false;
        _list.Enabled = false;
        try { await operation(); }
        catch (Exception error) { Log(UiText.T("操作未完成：") + (error is AggregateException aggregate ? aggregate.Flatten().InnerExceptions[0].Message : error.Message)); }
        finally { _busy = false; foreach (var button in _buttons) button.Enabled = true; _list.Enabled = true; }
    }

    private void AddSources(IEnumerable<MobileTexturePackage.ImageSource> sources)
    {
        foreach (var source in sources) { _sources[source.Name] = source; _selected.Add(source.Name); }
        RefreshRows();
    }

    private void RefreshRows()
    {
        _refreshing = true;
        _list.BeginUpdate();
        try
        {
            _list.Items.Clear();
            foreach (var source in _sources.Values.Where(source => source.Name.Contains(_search.Text.Trim(), StringComparison.OrdinalIgnoreCase)).OrderBy(source => source.Name))
            {
                var item = new ListViewItem(source.Name) { Tag = source.Name, Checked = _selected.Contains(source.Name) };
                item.SubItems.Add(source.Origin);
                _list.Items.Add(item);
            }
        }
        finally { _list.EndUpdate(); _refreshing = false; }
    }
    private void Log(string message) { if (!IsDisposed) _status.AppendText(message + "\r\n"); }
    private static string DataRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JixModMaker");
}
