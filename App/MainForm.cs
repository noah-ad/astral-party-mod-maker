using System.Drawing;
using System.Diagnostics;
using System.IO.Compression;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Windows.Forms;

namespace JixModMaker;

public class MainForm : Form
{
    private readonly ModEngine _engine = new();
    private readonly IndexService _indexSvc = new();
    private readonly NamingService _naming = new();

    private GameIndex _index;
    private string _folder;
    private string _backupDir;
    private bool _includeHotCache;
    private bool _recursiveFolder;
    private ModManifest _ws = new();
    private TexRef _selectedAsset;
    private int _loadSeq;
    private int _previewSeq;
    private MemoryStream _animationPreviewStream;
    private Button _selectedCategoryButton;
    private readonly List<Button> _navButtons = new();
    private string _activePage = PageDashboard;
    private string _selectedCategoryId;
    private string _selectedHeroGroupKey;
    private string _selectedHeroSkin;
    private bool _characterTreeExpanded = true;
    private readonly HashSet<string> _expandedHeroGroups = new(StringComparer.OrdinalIgnoreCase);
    private string _browseLoadKey = "";
    private int _browseLoadedCount;
    private int _browseTotalCount;
    private int _lastBrowseBatchSize;
    private string _lastSectionTitle = "";
    private bool _appendingRows;
    private bool _loadMoreCheckQueued;
    private bool _loadingIndex;
    private bool _advancedCategories;
    private GameIndex _sortedIndex;
    private string _sortedKey;
    private List<TexIndexEntry> _sortedRows;

    public const string Version = "v2.3.0-preview.9";
    private const string PageDashboard = "dashboard";
    private const string PageBrowse = "browse";
    private const string PagePack = "pack";
    private const string PageTools = "tools";
    private const int MinThumb = 72, MaxThumb = 220, SrcThumb = 300, Pad = 9, ThumbDef = 116;
    private const int ThumbDecodeConcurrency = 6;
    private readonly SemaphoreSlim _thumbnailDecodeGate = new(ThumbDecodeConcurrency, ThumbDecodeConcurrency);
    private int _thumb = ThumbDef;
    private int Sc(int v) => v;

    private const string GameDefault =
        "D:/steam/steamapps/common/Astral Party/8vJXn6CN/AstralParty_CN_Data/StreamingAssets/aa/StandaloneWindows64";

    private readonly BufferedPanel _leftPanel = new() { Dock = DockStyle.Left, Width = 252, BackColor = Theme.Bg };
    private readonly BufferedPanel _rightPanel = new() { Dock = DockStyle.Right, Width = 320, BackColor = Theme.Bar };
    private readonly BufferedPanel _centerPanel = new() { Dock = DockStyle.Fill, BackColor = Theme.FlowBg };
    private readonly GradientPanel _topBar = new()
    {
        Dock = DockStyle.Top,
        Height = 62,
        Padding = new Padding(12, 9, 12, 9),
        GradientStart = Theme.Bar,
        GradientEnd = Color.FromArgb(31, 38, 82),
        GradientAngle = 0f
    };
    private readonly SideListFlow _categoryList = new() { Dock = DockStyle.Fill, BackColor = Theme.Bg, Padding = new Padding(8) };
    private readonly GridFlow _flow = new() { Dock = DockStyle.Fill, BackColor = Theme.FlowBg, Padding = new Padding(12) };
    private readonly ListView _resourceList = new()
    {
        Dock = DockStyle.Fill,
        View = View.Details,
        FullRowSelect = true,
        MultiSelect = false,
        HideSelection = false,
        BorderStyle = BorderStyle.None,
        HeaderStyle = ColumnHeaderStyle.Nonclickable,
        ShowItemToolTips = true,
        UseCompatibleStateImageBehavior = false,
        OwnerDraw = false
    };
    private readonly Label _sideTitle = Theme.Caption("资源分类", true);
    private readonly Label _pageTitle = Theme.Caption("仪表盘", true);
    private readonly BufferedFlowPanel _browseFilters = new()
    {
        Dock = DockStyle.Right,
        Width = 560,
        BackColor = Color.Transparent,
        FlowDirection = FlowDirection.LeftToRight,
        WrapContents = false
    };
    private readonly Label _status = new()
    {
        Dock = DockStyle.Fill,
        ForeColor = Theme.SubText,
        BackColor = Theme.Bar,
        TextAlign = ContentAlignment.MiddleLeft,
        Padding = new Padding(12, 0, 0, 0),
        Text = "就绪"
    };

    private readonly ComboBox _kindBox = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _searchBox = new() { BorderStyle = BorderStyle.FixedSingle, PlaceholderText = "搜索资源名 / bundle" };
    private Button _refreshIndexBtn;
    private readonly Label _metricLabel = Theme.Caption("", true);
    private readonly Label _pathLabel = Theme.Caption("");
    private readonly PictureBox _preview = new() { Dock = DockStyle.Top, Height = 210, BackColor = Theme.PicBg, SizeMode = PictureBoxSizeMode.Zoom };
    private readonly Label _detailTitle = Theme.Caption("未选择资源", true);
    private readonly Label _detailMeta = Theme.Caption("");
    private readonly Label _detailPath = Theme.Caption("");
    private readonly Label _detailHint = Theme.Caption("");
    private readonly Button _replaceTextureBtn = Theme.FlatButton("替换贴图");
    private readonly Button _replaceAnimationBtn = Theme.FlatButton("替换动态立绘");
    private readonly Button _exportPngBtn = Theme.FlatButton("导出PNG");
    private readonly Button _exportBundleZipBtn = Theme.FlatButton("导出当前Bundle ZIP");
    private readonly Button _replaceBundleBtn = Theme.FlatButton("替换所在资源包");
    private readonly Button _locateBtn = Theme.FlatButton("定位文件");

    public MainForm(string initFolder = null)
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        DoubleBuffered = true;
        AutoScaleMode = AutoScaleMode.Font;
        Text = "吉星派对 Mod 助手  " + Version;
        Width = 1400;
        Height = 860;
        MinimumSize = new Size(1100, 640);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Bg;
        Font = Theme.UI(9f);
        ApplyAppIcon();

        BuildLeftPanel();
        BuildCenterPanel();
        BuildRightPanel();

        var bottomBar = new GradientPanel
        {
            Dock = DockStyle.Bottom,
            Height = 30,
            GradientStart = Theme.Bar,
            GradientEnd = Theme.Bg,
            GradientAngle = 0f
        };
        bottomBar.Controls.Add(_status);

        Controls.Add(_centerPanel);
        Controls.Add(_rightPanel);
        Controls.Add(_leftPanel);
        Controls.Add(bottomBar);

        _flow.MouseWheel += OnFlowWheel;
        _flow.Scroll += (_, _) => QueueLoadMoreCheck();
        _flow.Resize += (_, _) => QueueLoadMoreCheck();

        if (!string.IsNullOrWhiteSpace(initFolder) && Directory.Exists(initFolder))
        {
            var normalized = initFolder.Replace('\\', '/');
            SetFolder(initFolder, normalized.Contains("StandaloneWindows64", StringComparison.OrdinalIgnoreCase), recursive: !normalized.Contains("StandaloneWindows64", StringComparison.OrdinalIgnoreCase));
        }
        Navigate(PageDashboard);
    }

    private void ApplyAppIcon()
    {
        var p = ResolveAssetPath("app_icon.ico");
        if (!string.IsNullOrWhiteSpace(p))
        {
            try { Icon = new Icon(p); } catch { }
        }
    }

    private static string ResolveAssetPath(string fileName)
    {
        foreach (var p in new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Assets", fileName),
            Path.Combine(Environment.CurrentDirectory, "App", "Assets", fileName),
            Path.Combine(Environment.CurrentDirectory, "Assets", fileName)
        })
        {
            if (File.Exists(p)) return p;
        }
        return "";
    }

    private static Image LoadAssetImage(string fileName)
    {
        var p = ResolveAssetPath(fileName);
        if (string.IsNullOrWhiteSpace(p)) return null;
        try
        {
            using var src = Image.FromFile(p);
            return new Bitmap(src);
        }
        catch { return null; }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyDpiScale(DeviceDpi);
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        ApplyDpiScale(e.DeviceDpiNew);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        NoticeDialog.ShowIfFirst(this);
        if (_folder != null)
        {
            LoadIndexAsync(false);
            return;
        }

        var install = GameLocator.Find();
        if (install != null)
        {
            SetFolder(install.AaDir, includeHotCache: true, recursive: false);
            _status.Text = "已检测到游戏，正在加载资源索引...";
            LoadIndexAsync(false);
        }
        else if (Directory.Exists(GameDefault))
        {
            SetFolder(GameDefault, includeHotCache: true, recursive: false);
            LoadIndexAsync(false);
        }
        else
        {
            UpdateDashboard();
            _status.Text = "未检测到游戏。点“检测游戏”或“打开游戏目录”。";
        }
    }

    private void BuildLeftPanel()
    {
        var title = new GradientPanel
        {
            Dock = DockStyle.Top,
            Height = 92,
            Padding = new Padding(12, 12, 12, 8),
            GradientStart = Theme.Bg,
            GradientEnd = Color.FromArgb(24, 27, 61),
            GradientAngle = 0f
        };
        var logoFrame = new RoundedCard
        {
            Location = new Point(12, 12),
            Size = new Size(64, 64),
            Padding = new Padding(3),
            Radius = 17,
            Fill = Theme.PicBg,
            GradientFill = Theme.CardAlt,
            BorderColor = Theme.Cyan,
            BackColor = Theme.Bg
        };
        var logo = new PictureBox
        {
            Dock = DockStyle.Fill,
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.Transparent,
            Image = LoadAssetImage("app_icon.png")
        };
        logo.Paint += (_, e) =>
        {
            if (logo.Image != null) return;
            using var brush = new SolidBrush(Theme.Accent);
            using var font = Theme.UI(18f, true);
            TextRenderer.DrawText(e.Graphics, "吉", font, logo.ClientRectangle, Theme.Accent,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        };
        var appName = new Label
        {
            Location = new Point(88, 16),
            Size = new Size(150, 30),
            Text = "吉星派对",
            ForeColor = Theme.Text,
            BackColor = Color.Transparent,
            Font = Theme.UI(16f, true),
            TextAlign = ContentAlignment.MiddleLeft
        };
        var appSub = new Label
        {
            Location = new Point(89, 49),
            Size = new Size(145, 22),
            Text = "MOD 工坊",
            ForeColor = Theme.SubText,
            BackColor = Color.Transparent,
            Font = Theme.UI(9f, true),
            TextAlign = ContentAlignment.MiddleLeft
        };
        logoFrame.Controls.Add(logo);
        title.Controls.Add(appSub);
        title.Controls.Add(appName);
        title.Controls.Add(logoFrame);

        var nav = new BufferedFlowPanel
        {
            Dock = DockStyle.Top,
            Height = 186,
            BackColor = Theme.Bg,
            Padding = new Padding(10, 4, 10, 10),
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false
        };
        AddNav(nav, PageDashboard, "仪表盘", "检测、资源数量、快捷入口");
        AddNav(nav, PageBrowse, "资源浏览", "按类型、角色、皮肤筛资源");
        AddNav(nav, PagePack, "作品 / 导出", "图包、bundle 压缩包");
        AddNav(nav, PageTools, "工具 / 维护", "路径、索引、还原、迁移");

        var metrics = new GradientPanel
        {
            Dock = DockStyle.Top,
            Height = 106,
            Padding = new Padding(12, 8, 12, 8),
            GradientStart = Theme.Bar,
            GradientEnd = Theme.Bg,
            GradientAngle = 90f
        };
        metrics.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 1, BackColor = Color.FromArgb(100, Theme.Cyan) });
        _metricLabel.Dock = DockStyle.Top;
        _metricLabel.Height = 46;
        _pathLabel.Dock = DockStyle.Fill;
        _pathLabel.AutoEllipsis = true;
        metrics.Controls.Add(_pathLabel);
        metrics.Controls.Add(_metricLabel);

        _sideTitle.Dock = DockStyle.Top;
        _sideTitle.Height = 34;
        _sideTitle.Text = "资源分类";
        _sideTitle.Padding = new Padding(16, 0, 8, 0);
        _sideTitle.ForeColor = Theme.Accent;
        _sideTitle.BackColor = Theme.Bg;
        _sideTitle.Font = Theme.UI(10f, true);

        _leftPanel.Controls.Add(_categoryList);
        _leftPanel.Controls.Add(_sideTitle);
        _leftPanel.Controls.Add(metrics);
        _leftPanel.Controls.Add(nav);
        _leftPanel.Controls.Add(title);
    }

    private void AddNav(FlowLayoutPanel panel, string page, string text, string tip)
    {
        var btn = Theme.FlatButton(text, 224);
        btn.Height = 36;
        btn.Tag = page;
        btn.Margin = new Padding(0, 4, 0, 0);
        btn.TextAlign = ContentAlignment.MiddleLeft;
        btn.Font = Theme.UI(9.5f, true);
        btn.BackColor = Theme.Bg;
        btn.ForeColor = Theme.SubText;
        if (btn is RoundButton rb)
        {
            rb.Radius = 11;
            rb.BorderColor = Color.Transparent;
            rb.HoverBackColor = Theme.Card;
            rb.DownBackColor = Theme.Accent;
            rb.IndicatorColor = Color.Transparent;
        }
        btn.Click += (_, _) => Navigate(page);
        btn.MouseEnter += (_, _) => _status.Text = tip;
        _navButtons.Add(btn);
        panel.Controls.Add(btn);
    }

    private static void AddAction(FlowLayoutPanel panel, string text, EventHandler handler)
    {
        var btn = Theme.FlatButton(text, 224);
        btn.Height = 30;
        btn.Margin = new Padding(0, 3, 0, 0);
        btn.TextAlign = ContentAlignment.MiddleLeft;
        btn.Click += handler;
        panel.Controls.Add(btn);
    }

    private void BuildCenterPanel()
    {
        ConfigureResourceList();

        _pageTitle.Dock = DockStyle.None;
        _pageTitle.Text = "仪表盘";
        _pageTitle.ForeColor = Theme.Accent;
        _pageTitle.BackColor = Color.Transparent;
        _pageTitle.Font = Theme.UI(17f, true);
        _pageTitle.TextAlign = ContentAlignment.MiddleLeft;

        _kindBox.Width = 150;
        _kindBox.Height = 30;
        _kindBox.BackColor = Theme.Card;
        _kindBox.ForeColor = Theme.Text;
        _kindBox.FlatStyle = FlatStyle.Flat;
        foreach (var k in ResourceKinds.All) _kindBox.Items.Add(new KindOption(k.Id, k.Label));
        _kindBox.SelectedIndexChanged += (_, _) =>
        {
            if (_kindBox.SelectedItem is KindOption opt)
            {
                _selectedCategoryButton = null;
                _selectedCategoryId = null;
                _selectedHeroGroupKey = null;
                _selectedHeroSkin = null;
                ResetBrowseState();
                if (_activePage == PageBrowse)
                {
                    BuildCategoryList(opt.Id);
                    ShowCurrentAssets();
                }
            }
        };
        _kindBox.SelectedIndex = 0;

        _searchBox.Width = 280;
        _searchBox.Height = 30;
        _searchBox.BackColor = Theme.PicBg;
        _searchBox.ForeColor = Theme.Text;
        _searchBox.TextChanged += (_, _) => { ResetBrowseState(); if (_activePage == PageBrowse) ShowCurrentAssets(); };

        var refresh = Theme.FlatButton("刷新索引", 120);
        _refreshIndexBtn = refresh;
        refresh.Height = 30;
        refresh.Margin = new Padding(8, 0, 0, 0);
        refresh.Click += (_, _) => LoadIndexAsync(true);

        _browseFilters.Controls.Add(_kindBox);
        _browseFilters.Controls.Add(_searchBox);
        _browseFilters.Controls.Add(refresh);

        _browseFilters.Dock = DockStyle.None;
        _topBar.Controls.Add(_pageTitle);
        _topBar.Controls.Add(_browseFilters);
        _topBar.Resize += (_, _) => LayoutBrowseFilters(_topBar, refresh);
        LayoutBrowseFilters(_topBar, refresh);

        _centerPanel.Controls.Add(_resourceList);
        _centerPanel.Controls.Add(_flow);
        _centerPanel.Controls.Add(_topBar);
    }

    private void LayoutBrowseFilters(Control topBar, Button refresh)
    {
        bool stacked = _activePage == PageBrowse && topBar.ClientSize.Width < Sc(760);
        int targetHeight = stacked ? Sc(104) : Sc(62);
        if (topBar.Height != targetHeight) topBar.Height = targetHeight;

        int width = stacked
            ? Math.Max(Sc(320), topBar.ClientSize.Width - Sc(24))
            : Math.Min(Sc(580), Math.Max(Sc(380), topBar.ClientSize.Width - Sc(250)));
        _pageTitle.Location = new Point(Sc(14), Sc(8));
        _pageTitle.Size = new Size(stacked ? topBar.ClientSize.Width - Sc(28) : Math.Max(Sc(170), topBar.ClientSize.Width - width - Sc(34)), Sc(42));
        _browseFilters.Width = width;
        _browseFilters.Height = Sc(40);
        _browseFilters.Location = stacked
            ? new Point(Sc(12), Sc(55))
            : new Point(topBar.ClientSize.Width - width - Sc(12), Sc(10));
        _kindBox.Width = Sc(138);
        int refreshWidth = refresh?.Width ?? Sc(120);
        if (refresh != null) refresh.Width = Sc(120);
        _searchBox.Width = Math.Max(Sc(150), width - _kindBox.Width - refreshWidth - Sc(44));
    }

    private void ConfigureResourceList()
    {
        _resourceList.BackColor = Theme.FlowBg;
        _resourceList.ForeColor = Theme.Text;
        _resourceList.Font = Theme.UI(9.5f);
        _resourceList.Columns.Add("资源名", 360);
        _resourceList.Columns.Add("分组", 210);
        _resourceList.Columns.Add("尺寸/类型", 100);
        _resourceList.Columns.Add("Bundle", 260);
        _resourceList.DrawColumnHeader += DrawResourceHeader;
        _resourceList.DrawItem += (_, _) => { };
        _resourceList.DrawSubItem += DrawResourceSubItem;
        _resourceList.SelectedIndexChanged += (_, _) =>
        {
            if (_resourceList.SelectedItems.Count == 0) return;
            if (_resourceList.SelectedItems[0].Tag is TexRef asset)
                SelectAsset(asset, null);
        };
        _resourceList.Resize += (_, _) => LayoutResourceListColumns();
        _resourceList.MouseDoubleClick += (_, _) =>
        {
            if (_selectedAsset is { IsTexture: true }) PickAndReplaceTexture(_selectedAsset);
        };
        _resourceList.MouseUp += (_, e) =>
        {
            if (e.Button != MouseButtons.Right) return;
            var item = _resourceList.GetItemAt(e.X, e.Y);
            if (item != null)
            {
                item.Selected = true;
                item.Focused = true;
            }
        };
        var menu = new ContextMenuStrip();
        menu.Items.Add("替换贴图...", null, (_, _) => { if (_selectedAsset is { IsTexture: true }) PickAndReplaceTexture(_selectedAsset); });
        menu.Items.Add("导出 PNG...", null, (_, _) => { if (_selectedAsset is { IsTexture: true }) ExportSingle(_selectedAsset); });
        menu.Items.Add("导出所在 Bundle ZIP...", null, (_, _) => { if (_selectedAsset != null) ExportBundleZip(new[] { _selectedAsset }, _selectedAsset.Name ?? "bundle"); });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("定位文件", null, (_, _) => { if (_selectedAsset != null) LocateBundle(_selectedAsset); });
        var animationItem = menu.Items.Add("替换动态立绘...", null, (_, _) => OpenAnimatedPortrait(_selectedAsset));
        menu.Opening += (_, e) =>
        {
            bool has = _selectedAsset != null;
            bool tex = _selectedAsset is { IsTexture: true };
            menu.Items[0].Enabled = tex;
            menu.Items[1].Enabled = tex;
            menu.Items[2].Enabled = has;
            menu.Items[4].Enabled = has;
            animationItem.Visible = IsAnimatedPortraitCandidate(_selectedAsset);
            e.Cancel = !has;
        };
        _resourceList.ContextMenuStrip = menu;
    }

    private void LayoutResourceListColumns()
    {
        if (_resourceList.Columns.Count < 4) return;
        int w = Math.Max(420, _resourceList.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 8);
        _resourceList.Columns[0].Width = Math.Max(220, (int)(w * 0.36));
        _resourceList.Columns[1].Width = Math.Max(150, (int)(w * 0.24));
        _resourceList.Columns[2].Width = Math.Max(90, (int)(w * 0.12));
        _resourceList.Columns[3].Width = Math.Max(180, w - _resourceList.Columns[0].Width - _resourceList.Columns[1].Width - _resourceList.Columns[2].Width);
    }

    private void DrawResourceHeader(object sender, DrawListViewColumnHeaderEventArgs e)
    {
        using var bg = new SolidBrush(Theme.Bar);
        using var line = new Pen(Theme.Muted);
        e.Graphics.FillRectangle(bg, e.Bounds);
        e.Graphics.DrawLine(line, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
        var bounds = Rectangle.Inflate(e.Bounds, -10, 0);
        TextRenderer.DrawText(e.Graphics, e.Header.Text, Theme.UI(9f, true), bounds, Theme.SubText,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    private void DrawResourceSubItem(object sender, DrawListViewSubItemEventArgs e)
    {
        var asset = e.Item.Tag as TexRef;
        bool selected = e.Item.Selected;
        var rowBg = selected
            ? Theme.AccentDim
            : asset?.Modded == true
                ? Theme.CardMod
                : e.ItemIndex % 2 == 0 ? Theme.FlowBg : Theme.Card;
        var fore = selected ? Color.White : e.ColumnIndex == 0 ? Theme.Text : Theme.SubText;
        using var bg = new SolidBrush(rowBg);
        using var line = new Pen(Theme.Muted);
        e.Graphics.FillRectangle(bg, e.Bounds);
        e.Graphics.DrawLine(line, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);

        var bounds = Rectangle.Inflate(e.Bounds, -10, 0);
        TextRenderer.DrawText(e.Graphics, e.SubItem.Text, _resourceList.Font, bounds, fore,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    private void BuildRightPanel()
    {
        _rightPanel.Padding = new Padding(14, 14, 14, 14);
        _rightPanel.AutoScroll = true;
        var details = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Margin = Padding.Empty
        };
        details.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (var label in new[] { _detailTitle, _detailMeta, _detailPath, _detailHint })
        {
            label.Dock = DockStyle.Top;
            label.AutoSize = true;
            label.TextAlign = ContentAlignment.TopLeft;
            label.Margin = new Padding(0, 0, 0, 12);
        }
        _detailTitle.Font = Theme.UI(10.5f, true);
        _detailMeta.Font = Theme.UI(9f);
        _detailPath.Font = Theme.UI(8.5f);
        _detailHint.Font = Theme.UI(8.5f);

        var buttons = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Theme.Bar,
            ColumnCount = 1,
            Margin = new Padding(0, 0, 0, 10)
        };
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (var b in new[] { _replaceTextureBtn, _replaceAnimationBtn, _exportPngBtn, _exportBundleZipBtn, _replaceBundleBtn, _locateBtn })
        {
            b.AutoSize = false;
            b.Dock = DockStyle.Top;
            b.Height = 32;
            b.Margin = new Padding(0, 0, 0, 5);
            b.TextAlign = ContentAlignment.MiddleLeft;
            StyleSecondaryButton(b);
            buttons.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            buttons.Controls.Add(b, 0, buttons.Controls.Count);
        }
        StylePrimaryButton(_replaceTextureBtn);
        _replaceAnimationBtn.ForeColor = Theme.Cyan;

        _replaceTextureBtn.Click += (_, _) => { if (_selectedAsset != null) PickAndReplaceTexture(_selectedAsset); };
        _replaceAnimationBtn.Click += (_, _) => OpenAnimatedPortrait(_selectedAsset);
        _exportPngBtn.Click += (_, _) => { if (_selectedAsset != null) ExportSingle(_selectedAsset); };
        _exportBundleZipBtn.Click += (_, _) => { if (_selectedAsset != null) ExportBundleZip(new[] { _selectedAsset }, "当前资源包"); };
        _replaceBundleBtn.Click += (_, _) => { if (_selectedAsset != null) ReplaceBundleFile(_selectedAsset); };
        _locateBtn.Click += (_, _) => { if (_selectedAsset != null) LocateBundle(_selectedAsset); };
        SetDetailButtons(false, false);
        ConfigurePreviewDragDrop();

        _preview.Margin = new Padding(0, 0, 0, 12);
        foreach (var control in new Control[] { _preview, _detailTitle, buttons, _detailMeta, _detailPath, _detailHint })
        {
            details.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            details.Controls.Add(control, 0, details.Controls.Count);
        }
        _rightPanel.Controls.Add(details);
    }

    private void ConfigurePreviewDragDrop()
    {
        _preview.AllowDrop = true;
        _preview.DragEnter += (_, e) =>
        {
            e.Effect = _selectedAsset is { IsTexture: true } && e.Data.GetDataPresent(DataFormats.FileDrop)
                ? DragDropEffects.Copy
                : DragDropEffects.None;
        };
        _preview.DragDrop += (_, e) =>
        {
            if (_selectedAsset is not { IsTexture: true }) return;
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files is { Length: > 0 })
                DoReplace(null, files[0], explicitAsset: _selectedAsset);
        };
        AttachDragExport(_preview, () => _selectedAsset);
    }

    private void ApplyDpiScale(int deviceDpi)
    {
        _leftPanel.Width = Sc(252);
        _rightPanel.Width = Sc(320);
        _flow.Padding = new Padding(Sc(12));
        _categoryList.Padding = new Padding(Sc(8));
        _preview.Height = Sc(210);
        _thumb = Math.Clamp(Sc(ThumbDef), Sc(MinThumb), Sc(MaxThumb));

        foreach (Control c in _flow.Controls)
        {
            if (c is RoundedCard { Tag: TexRef } card) LayoutCard(card);
            else if ((c.Tag as string) == "header") c.Height = Sc(34);
        }
    }

    private void Navigate(string page)
    {
        _centerPanel.SuspendLayout();
        _leftPanel.SuspendLayout();
        _flow.Visible = false;
        try
        {
            _activePage = page;
            foreach (var btn in _navButtons)
            {
                bool on = string.Equals(btn.Tag as string, page, StringComparison.OrdinalIgnoreCase);
                btn.BackColor = on ? Theme.Accent : Theme.Bg;
                btn.ForeColor = on ? Color.White : Theme.SubText;
                if (btn is RoundButton rb)
                {
                    rb.BorderColor = Color.Transparent;
                    rb.GradientColor = on ? Theme.AccentDim : Color.Transparent;
                    rb.IndicatorColor = on ? Theme.Cyan : Color.Transparent;
                }
                btn.Invalidate();
            }

            _pageTitle.Text = page switch
            {
                PageBrowse => "资源浏览",
                PagePack => "作品 / 导出",
                PageTools => "工具 / 维护",
                _ => "仪表盘"
            };
            bool browsing = page == PageBrowse;
            _browseFilters.Visible = browsing;
            _resourceList.Visible = false;
            _rightPanel.Visible = browsing;
            _sideTitle.Visible = browsing;
            _categoryList.Visible = browsing;
            _sideTitle.Text = "资源分类";
            LayoutBrowseFilters(_topBar, _refreshIndexBtn);
            if (!browsing)
            {
                _loadSeq++;
                ClearDetails();
                ClearSideList();
            }

            ShowCurrentPage();
        }
        finally
        {
            _flow.Visible = true;
            _leftPanel.ResumeLayout(true);
            _centerPanel.ResumeLayout(true);
            _leftPanel.Invalidate(true);
            _centerPanel.Invalidate(true);
        }
    }

    private void ShowCurrentPage()
    {
        if (_activePage == PageBrowse)
        {
            BuildCategoryList(CurrentKind);
            ShowCurrentAssets();
        }
        else if (_activePage == PagePack) ShowPackPage();
        else if (_activePage == PageTools) ShowToolsPage();
        else ShowDashboardPage();
    }

    private void ClearFlow()
    {
        _flow.SuspendLayout();
        try
        {
            var oldControls = _flow.Controls.Cast<Control>().ToList();
            foreach (Control c in oldControls)
                DisposeControlImages(c);
            _flow.Controls.Clear();
            foreach (Control c in oldControls)
                c.Dispose();
        }
        finally
        {
            _flow.AutoScrollPosition = Point.Empty;
            _flow.ResumeLayout(true);
            _flow.PerformLayout();
            _flow.Invalidate(true);
        }
    }

    private static void DisposeControlImages(Control control)
    {
        if (control is PictureBox pic)
        {
            pic.Image?.Dispose();
            pic.Image = null;
        }
        foreach (Control child in control.Controls)
            DisposeControlImages(child);
    }

    private void ShowDashboardPage()
    {
        ClearFlow();
        ClearSideList();

        int total = _index?.AssetCount ?? 0;
        int bundleCount = _index?.BundleCount ?? 0;
        int textureCount = _index?.CountKind(ResourceKinds.Texture) ?? 0;
        int modified = _ws?.Entries.Count ?? 0;
        var cacheState = _index == null ? "未加载" : $"{(_index.Lightweight ? "轻量索引" : "完整索引")} / {_index.BuiltAt}";

        AddPageCard("状态概览",
            $"游戏目录: {(_folder ?? "未检测")}\nBundle: {bundleCount}\n资源: {total}，贴图: {textureCount}\n已改贴图: {modified}\n索引: {cacheState}",
            MakeButton("检测游戏", (_, _) => DetectGame()),
            MakeButton("打开游戏目录", (_, _) => OpenGameDir()),
            MakeButton("启动游戏", (_, _) => LaunchGame()),
            MakeButton("刷新索引", (_, _) => LoadIndexAsync(true)));

        AddPageCard("常用工作流",
            "浏览资源按类型和角色皮肤分组；选中资源后右侧预览、替换、导出 PNG 或导出所在 bundle ZIP。",
            MakeButton("进入资源浏览", (_, _) => Navigate(PageBrowse)),
            MakeButton("导入图包", (_, _) => ImportPack()),
            MakeButton("导出图包", (_, _) => ExportPack()));

        AddPageCard("作品与 bundle",
            "ZIP 保留资源原始路径，解压到目标设备对应资源目录即可覆盖。",
            MakeButton("打开作品页", (_, _) => Navigate(PagePack)),
            MakeButton("导出已改Bundle ZIP", (_, _) => ExportModifiedBundlesZip()),
            MakeButton("全部还原", (_, _) => RestoreAll()));
    }

    private void ShowPackPage()
    {
        ClearFlow();
        ClearSideList();

        AddPageCard("作品 / 导出",
            $"当前作品集记录 {_ws?.Entries.Count ?? 0} 项。导出图包会重新解码当前贴图；导出 bundle ZIP 会直接压缩被修改过的资源包。",
            MakeButton("导入图包", (_, _) => ImportPack()),
            MakeButton("导出图包", (_, _) => ExportPack()),
            MakeButton("导出已改Bundle ZIP", (_, _) => ExportModifiedBundlesZip()),
            MakeButton("打开资源目录", (_, _) => OpenFolderInExplorer()));

        if (_ws == null || _ws.Entries.Count == 0)
        {
            AddPageCard("空作品集", "还没有通过本工具替换过贴图。到资源浏览页选择卡片，替换后会自动写入这里。", MakeButton("去资源浏览", (_, _) => Navigate(PageBrowse)));
            return;
        }

        var rows = _ws.Entries
            .OrderBy(e => e.Label)
            .ThenBy(e => e.TextureName)
            .Take(24)
            .ToList();
        AddHeader($"已记录替换 {rows.Count}/{_ws.Entries.Count}");
        foreach (var entry in rows)
        {
            var asset = FindAssetForEntry(entry);
            string bundlePath = asset?.BundlePath ?? Path.Combine(_folder ?? "", entry.Bundle ?? "");
            AddEntryCard(entry, asset, bundlePath);
        }
    }

    private void ShowToolsPage()
    {
        ClearFlow();
        ClearSideList();

        AddPageCard("路径",
            $"自动定位会检查 Steam appmanifest_{GameLocator.AppId}.acf、常见 SteamLibrary，以及国服 8vJXn6CN 目录。\n当前: {(_folder ?? "未连接")}",
            MakeButton("检测游戏", (_, _) => DetectGame()),
            MakeButton("打开游戏目录", (_, _) => OpenGameDir()),
            MakeButton("打开任意资源文件夹", (_, _) => OpenFolder()),
            MakeButton("启动游戏", (_, _) => LaunchGame()));

        AddPageCard("索引",
            _index == null
                ? "索引未加载。刷新索引会重新扫描 bundle；未变更时下次启动直接命中缓存。"
                : $"BuiltAt: {_index.BuiltAt}\n模式: {(_index.Lightweight ? "轻量贴图索引" : "完整索引")}\nStamp: {ShortHash(_index.SourceStamp)}\n资源: {_index.AssetCount} / Bundle: {_index.BundleCount}",
            MakeButton("刷新索引", (_, _) => LoadIndexAsync(true)),
            MakeButton("完整扫描", (_, _) => LoadIndexAsync(true, includeAdvancedTypes: true)),
            MakeButton("回到资源浏览", (_, _) => Navigate(PageBrowse)));

        AddPageCard("维护",
            "还原使用 _原始备份；迁移旧 Mod 会另写 _迁移结果，不覆盖原目录。",
            MakeButton("导出已改Bundle ZIP", (_, _) => ExportModifiedBundlesZip()),
            MakeButton("迁移旧Mod", (_, _) => MigrateOldMods()),
            MakeButton("全部还原", (_, _) => RestoreAll()));
    }

    private Button MakeButton(string text, EventHandler handler, int width = 142)
    {
        var b = Theme.FlatButton(text, Sc(width));
        int textWidth = TextRenderer.MeasureText(text, b.Font, Size.Empty, TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine).Width;
        b.Width = Math.Max(Sc(width), textWidth + Sc(46));
        b.Height = Sc(32);
        b.Margin = new Padding(0, 0, Sc(8), Sc(6));
        StyleSecondaryButton(b);
        b.Click += handler;
        return b;
    }

    private static void StylePrimaryButton(Button button)
    {
        button.BackColor = Theme.Accent;
        button.ForeColor = Color.White;
        button.Font = Theme.UI(9.2f, true);
        if (button is RoundButton rb)
        {
            rb.BorderColor = Color.Transparent;
            rb.GradientColor = Theme.AccentDim;
            rb.HoverBackColor = Color.FromArgb(235, 70, 145);
            rb.DownBackColor = Theme.AccentDim;
            rb.IndicatorColor = Color.Transparent;
        }
        button.Invalidate();
    }

    private static void StyleSecondaryButton(Button button)
    {
        button.BackColor = Theme.Surface;
        button.ForeColor = Theme.Text;
        if (button is RoundButton rb)
        {
            rb.BorderColor = Color.FromArgb(95, Theme.Line);
            rb.GradientColor = Color.Transparent;
            rb.HoverBackColor = Theme.CyanDim;
            rb.DownBackColor = Theme.AccentDim;
            rb.IndicatorColor = Color.Transparent;
        }
        button.Invalidate();
    }

    private void AddPageCard(string title, string body, params Button[] buttons)
    {
        int width = Math.Max(Sc(380), HeaderWidth());
        var card = new RoundedCard
        {
            Tag = "page_card",
            Width = width,
            Height = Sc(170),
            Margin = new Padding(Sc(6)),
            BackColor = Theme.FlowBg
        };
        card.SetFill(Theme.Card);
        card.GradientFill = Theme.CardAlt;
        card.Radius = Sc(14);
        card.BorderColor = Color.FromArgb(95, Theme.Line);

        var titleMark = new Panel { BackColor = Theme.Cyan };
        var titleLabel = Theme.Caption(title, true);
        titleLabel.Font = Theme.UI(11f, true);
        titleLabel.ForeColor = Theme.Accent;

        var bodyLabel = Theme.Caption(body);
        bodyLabel.TextAlign = ContentAlignment.TopLeft;
        bodyLabel.Font = Theme.UI(9.2f);

        var actions = new BufferedFlowPanel
        {
            BackColor = Color.Transparent,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Padding = new Padding(0, Sc(2), 0, 0)
        };
        for (int i = 0; i < buttons.Length; i++)
        {
            if (i == 0) StylePrimaryButton(buttons[i]);
            else StyleSecondaryButton(buttons[i]);
            actions.Controls.Add(buttons[i]);
        }

        card.Controls.Add(titleMark);
        card.Controls.Add(titleLabel);
        card.Controls.Add(bodyLabel);
        card.Controls.Add(actions);
        _flow.Controls.Add(card);

        bool layingOut = false;
        void LayoutContents()
        {
            if (layingOut || card.IsDisposed) return;
            layingOut = true;
            try
            {
                int pad = Sc(16);
                int inner = Math.Max(Sc(240), card.ClientSize.Width - pad * 2);
                int titleHeight = Sc(30);
                titleMark.SetBounds(pad, Sc(18), Sc(3), Sc(16));
                titleLabel.SetBounds(pad + Sc(11), Sc(12), inner - Sc(11), titleHeight);

                var textSize = TextRenderer.MeasureText(body ?? "", bodyLabel.Font,
                    new Size(inner, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.TextBoxControl);
                int bodyHeight = Math.Max(Sc(36), textSize.Height + Sc(8));
                bodyLabel.SetBounds(pad, titleLabel.Bottom + Sc(2), inner, bodyHeight);

                int rows = 0;
                int rowWidth = 0;
                int rowHeight = buttons.Length == 0 ? 0 : buttons.Max(b => b.Height + b.Margin.Vertical);
                foreach (var button in buttons)
                {
                    int itemWidth = button.Width + button.Margin.Horizontal;
                    if (rowWidth > 0 && rowWidth + itemWidth > inner)
                    {
                        rows++;
                        rowWidth = 0;
                    }
                    rowWidth += itemWidth;
                }
                if (buttons.Length > 0) rows++;
                int actionHeight = rows * rowHeight + (buttons.Length > 0 ? Sc(4) : 0);
                actions.Visible = buttons.Length > 0;
                actions.SetBounds(pad, bodyLabel.Bottom + Sc(6), inner, actionHeight);
                int neededHeight = buttons.Length > 0 ? actions.Bottom + Sc(10) : bodyLabel.Bottom + Sc(12);
                if (card.Height != neededHeight) card.Height = neededHeight;
            }
            finally
            {
                layingOut = false;
            }
        }

        card.SizeChanged += (_, _) => LayoutContents();
        LayoutContents();
    }

    private void AddEntryCard(ModEntry entry, TexRef asset, string bundlePath)
    {
        int width = Math.Max(Sc(360), _flow.ClientSize.Width - _flow.Padding.Horizontal - Sc(24));
        var card = new RoundedCard
        {
            Width = width,
            Height = Sc(86),
            Margin = new Padding(Sc(6), Sc(4), Sc(6), Sc(4)),
            Padding = new Padding(Sc(12)),
            BackColor = Theme.FlowBg
        };
        card.SetFill(Theme.CardAlt);

        var actions = new BufferedFlowPanel
        {
            Dock = DockStyle.Right,
            Width = Sc(315),
            BackColor = Color.Transparent,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };
        actions.Controls.Add(MakeButton("定位", (_, _) =>
        {
            if (asset != null) LocateBundle(asset);
            else LocatePath(bundlePath);
        }, 82));
        actions.Controls.Add(MakeButton("导出Bundle ZIP", (_, _) =>
        {
            var target = asset ?? new TexRef
            {
                BundlePath = bundlePath,
                BundleName = entry.Bundle,
                Name = entry.TextureName,
                Source = "作品集",
                PathId = entry.PathId,
                Kind = ResourceKinds.Texture
            };
            ExportBundleZip(new[] { target }, entry.TextureName ?? entry.Bundle ?? "bundle");
        }, 140));

        var main = Theme.Caption($"{entry.TextureName}\n{entry.Label} · {entry.Bundle} · PathId {entry.PathId}");
        main.Dock = DockStyle.Fill;
        main.TextAlign = ContentAlignment.MiddleLeft;
        main.AutoEllipsis = true;

        card.Controls.Add(main);
        card.Controls.Add(actions);
        _flow.Controls.Add(card);
    }

    private string CurrentKind => (_kindBox.SelectedItem as KindOption)?.Id ?? ResourceKinds.Texture;

    private void SetFolder(string path, bool includeHotCache, bool recursive)
    {
        _folder = ResourceLocator.NormalizeGameDirectory(path);
        _includeHotCache = includeHotCache;
        _recursiveFolder = recursive;
        _backupDir = Path.Combine(_folder, "_原始备份");
        _ws = PackService.LoadWorkspace(_folder);
        Text = $"吉星派对 Mod 助手 {Version}  [{path}]";
        UpdateDashboard();
    }

    private void DetectGame()
    {
        var install = GameLocator.Find();
        if (install == null)
        {
            _status.Text = "未检测到 Steam 游戏安装。";
            return;
        }

        SetFolder(install.AaDir, includeHotCache: true, recursive: false);
        _status.Text = "已连接游戏：" + (!string.IsNullOrWhiteSpace(install.ExePath) ? install.ExePath : install.AaDir);
        LoadIndexAsync(false);
    }

    private void OpenGameDir()
    {
        using var dlg = new FolderBrowserDialog { Description = "选择游戏 StandaloneWindows64 目录" };
        if (Directory.Exists(GameDefault)) dlg.SelectedPath = GameDefault;
        if (dlg.ShowDialog() != DialogResult.OK) return;
        SetFolder(dlg.SelectedPath, includeHotCache: true, recursive: false);
        LoadIndexAsync(false);
    }

    private void OpenFolder()
    {
        using var dlg = new FolderBrowserDialog { Description = "选择资源文件夹（递归扫描 .bundle / __data）" };
        if (Directory.Exists("D:/clearmind/mod/JixingModMaker/演示bundle"))
            dlg.SelectedPath = "D:/clearmind/mod/JixingModMaker/演示bundle";
        if (dlg.ShowDialog() != DialogResult.OK) return;
        SetFolder(dlg.SelectedPath, includeHotCache: false, recursive: true);
        LoadIndexAsync(true);
    }

    private void OpenFolderInExplorer()
    {
        if (_folder == null || !Directory.Exists(_folder))
        {
            _status.Text = "当前没有可打开的资源目录";
            return;
        }
        try { Process.Start("explorer.exe", _folder); }
        catch (Exception ex) { _status.Text = "打开目录失败: " + ex.Message; }
    }

    private void LaunchGame()
    {
        try
        {
            var install = GameLocator.Find();
            if (!string.IsNullOrWhiteSpace(install?.ExePath) && File.Exists(install.ExePath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = install.ExePath,
                    WorkingDirectory = Path.GetDirectoryName(install.ExePath)!,
                    UseShellExecute = true
                });
                _status.Text = "已启动游戏：" + install.ExePath;
                return;
            }

            Process.Start(new ProcessStartInfo($"steam://rungameid/{GameLocator.AppId}") { UseShellExecute = true });
            _status.Text = "未找到 exe，已尝试通过 Steam 启动。";
        }
        catch (Exception ex)
        {
            MessageBox.Show("启动游戏失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            _status.Text = "启动游戏失败";
        }
    }

    private async void LoadIndexAsync(bool rebuild, bool includeAdvancedTypes = false)
    {
        if (_loadingIndex) return;
        if (_folder == null)
        {
            DetectGame();
            return;
        }

        _loadingIndex = true;
        try
        {
        ClearFlow();
        ClearSideList();
        ClearDetails();
        if (includeAdvancedTypes) IndexService.ClearHotBundleCache();

        _index = includeAdvancedTypes
            ? null
            : await Task.Run(() => _indexSvc.Load(_folder, heroOnly: false, includeHotCache: _includeHotCache, recursive: _recursiveFolder));
        if (_index == null)
        {
            string folder = _folder;
            bool includeHot = _includeHotCache;
            bool recursive = _recursiveFolder;
            _status.Text = includeHot ? "扫描基础包 + 热更缓存..." : "递归扫描资源文件夹...";
            _index = await Task.Run(() => _indexSvc.Build(folder, (done, total) =>
            {
                if (done % 100 == 0 && IsHandleCreated)
                    BeginInvoke(() => _status.Text = $"扫描资源索引... {done}/{total}");
            }, heroOnly: false, includeHotCache: includeHot, recursive: recursive, includeAdvancedTypes: includeAdvancedTypes));
            try { await Task.Run(() => _indexSvc.Save(_index)); } catch { }
        }
        else
        {
            _status.Text = _index.Lightweight
                ? $"已使用轻量贴图索引：{_index.LightweightTextureCount} 项 / {_index.BundleCount} 包"
                : $"已使用缓存索引：{_index.AssetCount} 项 / {_index.BundleCount} 包";
        }

        UpdateDashboard();
        ShowCurrentPage();
        }
        catch (Exception ex) { _status.Text = "索引加载失败：" + ex.Message; }
        finally { _loadingIndex = false; }
    }

    private void UpdateDashboard()
    {
        int total = _index?.AssetCount ?? 0;
        int bundles = _index?.BundleCount ?? 0;
        int modified = _ws?.Entries.Count ?? 0;
        _metricLabel.Text = $"BUNDLES {bundles}  ASSETS {total}  MODDED {modified}";
        _pathLabel.Text = _folder == null ? "未连接游戏目录" : _folder;
    }

    private void BuildCategoryList(string kind)
    {
        _categoryList.SuspendLayout();
        ClearSideList();

        Dictionary<string, int> counts;
        if (_index?.Lightweight == true && kind == ResourceKinds.Texture)
        {
            counts = _index.TextureCategoryCounts != null
                ? new Dictionary<string, int>(_index.TextureCategoryCounts, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            counts[ResourceCategories.AllId] = _index.LightweightTextureCount;
        }
        else if (_index?.Lightweight == true)
        {
            counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                [ResourceCategories.AllId] = 0
            };
        }
        else
        {
            counts = (_index?.Items ?? new List<TexIndexEntry>())
                .Where(i => i.Kind == kind)
                .GroupBy(i => i.CategoryId ?? ResourceCategories.AllId)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
            counts[ResourceCategories.AllId] = (_index?.Items ?? new List<TexIndexEntry>()).Count(i => i.Kind == kind);
        }

        var defs = ResourceCategories.ForKind(kind).ToList();
        if (!defs.Any(d => d.Id == ResourceCategories.AllId))
            defs.Insert(0, new ResourceCategoryDef { Id = ResourceCategories.AllId, Label = "全部" });

        var validIds = new HashSet<string>(defs.Select(d => d.Id), StringComparer.OrdinalIgnoreCase);
        foreach (var id in counts.Keys.Where(id => !validIds.Contains(id)))
            defs.Add(new ResourceCategoryDef { Id = id, Label = id });

        if (string.IsNullOrWhiteSpace(_selectedCategoryId) || !defs.Any(d => d.Id == _selectedCategoryId))
        {
            _selectedCategoryId = kind == ResourceKinds.Texture && counts.GetValueOrDefault(ResourceCategories.CharacterId, 0) > 0
                ? ResourceCategories.CharacterId
                : ResourceCategories.AllId;
            _selectedHeroGroupKey = null;
            _selectedHeroSkin = null;
        }

        _selectedCategoryButton = null;
        foreach (var def in defs)
        {
            var count = counts.GetValueOrDefault(def.Id, 0);
            if (def.Id != ResourceCategories.AllId && count == 0) continue;
            if (!_advancedCategories && (def.Advanced || def.Id == "sprite_anim")) continue;

            if (kind == ResourceKinds.Texture && def.Id == ResourceCategories.CharacterId)
            {
                AddCharacterRootButton(def, count);
                if (_characterTreeExpanded && count > 0)
                    AddCharacterTreeButtons();
                continue;
            }

            AddCategoryButton($"{def.Label}  [{count}]", def.Id, null, null, 0, count);
        }

        if (kind == ResourceKinds.Texture)
        {
            var advanced = new CheckBox { Text = "动作帧 / 特效 / 其它资源", Checked = _advancedCategories, AutoSize = true, ForeColor = Theme.SubText, Margin = new Padding(8, 14, 4, 8) };
            advanced.CheckedChanged += (_, _) => { _advancedCategories = advanced.Checked; BuildCategoryList(kind); };
            _categoryList.Controls.Add(advanced);
        }
        _categoryList.ResumeLayout();
    }

    private void ClearSideList()
    {
        var old = _categoryList.Controls.Cast<Control>().ToList();
        _categoryList.Controls.Clear();
        foreach (var c in old) c.Dispose();
    }

    private void AddCharacterRootButton(ResourceCategoryDef def, int count)
    {
        var prefix = _characterTreeExpanded ? "▾" : "▸";
        var btn = AddCategoryButton($"{prefix} {def.Label}  [{count}]", def.Id, null, null, 0, count, selectOnClick: false);
        btn.MouseClick += (_, e) =>
        {
            if (e.X < Sc(28))
            {
                _characterTreeExpanded = !_characterTreeExpanded;
                BuildCategoryList(CurrentKind);
                return;
            }
            SelectCategory(def.Id, null, null);
        };
    }

    private void AddCharacterTreeButtons()
    {
        foreach (var group in GetCharacterGroups())
        {
            bool groupExpanded = _expandedHeroGroups.Contains(group.GroupKey)
                || string.Equals(_selectedHeroGroupKey, group.GroupKey, StringComparison.OrdinalIgnoreCase);
            var prefix = groupExpanded ? "▾" : "▸";
            var groupButton = AddCategoryButton($"{prefix} {group.Display}  [{group.Count}]",
                ResourceCategories.CharacterId, group.GroupKey, null, 1, group.Count, selectOnClick: false);
            groupButton.MouseClick += (_, e) =>
            {
                if (e.X < Sc(40))
                {
                    ToggleHeroGroup(group.GroupKey);
                    return;
                }
                _expandedHeroGroups.Add(group.GroupKey);
                SelectCategory(ResourceCategories.CharacterId, group.GroupKey, null);
            };

            if (!groupExpanded) continue;
            foreach (var skin in group.Skins.OrderBy(s => s.Order).ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase))
            {
                AddCategoryButton($"{skin.Name}  [{skin.Count}]",
                    ResourceCategories.CharacterId, group.GroupKey, skin.Name, 2, skin.Count);
            }
        }
    }

    private Button AddCategoryButton(string text, string categoryId, string heroGroupKey, string skin,
        int indent, int count, bool selectOnClick = true)
    {
        var btn = Theme.FlatButton(text, Sc(224));
        btn.Tag = new CategoryButtonTag(categoryId, heroGroupKey, skin);
        btn.Height = indent == 0 ? Sc(34) : Sc(28);
        btn.Margin = new Padding(0, indent == 0 ? Sc(4) : Sc(2), 0, 0);
        btn.Padding = new Padding(Sc(8 + indent * 14), 0, Sc(6), 0);
        btn.TextAlign = ContentAlignment.MiddleLeft;
        btn.Font = Theme.UI(indent == 0 ? 9f : 8.3f, indent == 0);
        btn.Cursor = Cursors.Hand;
        if (btn is RoundButton rb)
        {
            rb.Radius = indent == 0 ? 11 : 9;
            rb.BorderColor = indent == 0 ? Color.FromArgb(70, Theme.Line) : Color.Transparent;
            rb.HoverBackColor = Theme.CardAlt;
            rb.DownBackColor = Theme.AccentDim;
        }
        StyleCategoryButton(btn, IsCategorySelected(categoryId, heroGroupKey, skin), indent);
        if (selectOnClick)
            btn.Click += (_, _) => SelectCategory(categoryId, heroGroupKey, skin);
        _categoryList.Controls.Add(btn);
        if (IsCategorySelected(categoryId, heroGroupKey, skin))
            _selectedCategoryButton = btn;
        return btn;
    }

    private void SelectCategory(string categoryId, string heroGroupKey, string skin)
    {
        _selectedCategoryId = categoryId;
        _selectedHeroGroupKey = heroGroupKey;
        _selectedHeroSkin = skin;
        if (!string.Equals(categoryId, ResourceCategories.CharacterId, StringComparison.OrdinalIgnoreCase))
        {
            _selectedHeroGroupKey = null;
            _selectedHeroSkin = null;
        }
        ResetBrowseState();
        BuildCategoryList(CurrentKind);
        ShowCurrentAssets();
    }

    private bool IsCategorySelected(string categoryId, string heroGroupKey, string skin)
        => string.Equals(_selectedCategoryId, categoryId, StringComparison.OrdinalIgnoreCase)
        && string.Equals(_selectedHeroGroupKey ?? "", heroGroupKey ?? "", StringComparison.OrdinalIgnoreCase)
        && string.Equals(_selectedHeroSkin ?? "", skin ?? "", StringComparison.OrdinalIgnoreCase);

    private static void StyleCategoryButton(Button btn, bool selected, int indent)
    {
        btn.BackColor = selected ? Theme.Accent : (indent == 0 ? Theme.Card : Theme.Bg);
        btn.ForeColor = selected ? Color.White : (indent == 0 ? Theme.Text : Theme.SubText);
        if (btn is RoundButton rb)
        {
            rb.BorderColor = selected ? Color.Transparent : indent == 0 ? Color.FromArgb(70, Theme.Line) : Color.Transparent;
            rb.GradientColor = selected ? Theme.AccentDim : Color.Transparent;
            rb.IndicatorColor = selected ? Theme.Cyan : Color.Transparent;
        }
        btn.Invalidate();
    }

    private void ToggleHeroGroup(string groupKey)
    {
        if (!_expandedHeroGroups.Remove(groupKey))
            _expandedHeroGroups.Add(groupKey);
        BuildCategoryList(CurrentKind);
    }

    private List<CharacterGroupInfo> GetCharacterGroups()
    {
        IEnumerable<string> names = Enumerable.Empty<string>();
        if (_index?.Lightweight == true)
        {
            if (_index.TextureRefsByCategory != null
                && _index.TextureRefsByCategory.TryGetValue(ResourceCategories.CharacterId, out var refs))
                names = refs.Select(r => r.Name);
        }
        else if (_index != null)
        {
            names = _index.Items
                .Where(i => i.Kind == ResourceKinds.Texture
                    && string.Equals(i.CategoryId, ResourceCategories.CharacterId, StringComparison.OrdinalIgnoreCase))
                .Select(i => i.Name);
        }

        var map = new Dictionary<string, CharacterGroupInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in names)
        {
            var parsed = NameParser.Parse(name);
            if (!parsed.IsHero) continue;
            var key = parsed.GroupKey;
            if (!map.TryGetValue(key, out var group))
            {
                group = new CharacterGroupInfo
                {
                    GroupKey = key,
                    Display = _naming.Display(key),
                    Sort = parsed.IsMonster ? 900000 : int.TryParse(parsed.HeroId, out var n) ? n : 899999
                };
                map[key] = group;
            }
            group.Count++;
            var skinName = parsed.Skin;
            if (!group.SkinMap.TryGetValue(skinName, out var skin))
            {
                skin = new CharacterSkinInfo { Name = skinName, Order = parsed.SkinOrder };
                group.SkinMap[skinName] = skin;
                group.Skins.Add(skin);
            }
            skin.Count++;
        }

        return map.Values
            .OrderBy(g => g.Sort)
            .ThenBy(g => g.Display, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void ShowCurrentAssets()
    {
        if (_activePage != PageBrowse) return;
        if (_index == null)
        {
            ClearFlow();
            ClearSideList();
            ClearDetails();
            AddPageCard("资源索引未加载", "先检测游戏或打开 StandaloneWindows64 目录，再刷新索引。", MakeButton("检测游戏", (_, _) => DetectGame()), MakeButton("打开游戏目录", (_, _) => OpenGameDir()));
            return;
        }

        var kind = CurrentKind;
        var cat = _selectedCategoryId ?? ResourceCategories.AllId;
        var query = (_searchBox.Text ?? "").Trim();
        var key = CurrentBrowseKey(kind, cat, query);

        if (_index.Lightweight && kind != ResourceKinds.Texture)
        {
            ClearFlow();
            ClearDetails();
            AddPageCard("需要完整扫描", "当前使用快速贴图索引。音频、文本、模型、动画需要在工具页执行一次完整扫描。", MakeButton("完整扫描", (_, _) => LoadIndexAsync(true, includeAdvancedTypes: true)));
            _status.Text = "当前是轻量贴图索引；非贴图资源请到“工具 / 维护”执行完整扫描。";
            return;
        }

        int cap = BrowseBatchSize(kind, cat, query);
        int total;
        var rows = _index.Lightweight && kind == ResourceKinds.Texture
            ? GetLightweightTextureRows(cat, query, 0, cap, out total)
            : GetFullIndexRows(kind, cat, query, 0, cap, out total);
        var shown = rows.Select(ToTexRef).ToList();
        var sections = BuildAssetSections(kind, cat, shown);

        _browseLoadKey = key;
        _browseLoadedCount = rows.Count;
        _browseTotalCount = total;
        _lastBrowseBatchSize = cap;
        _lastSectionTitle = "";
        ShowSections(sections, total, cap, 0, reset: true);
    }

    private void ResetBrowseState()
    {
        _browseLoadKey = "";
        _browseLoadedCount = 0;
        _browseTotalCount = 0;
        _lastBrowseBatchSize = 0;
        _lastSectionTitle = "";
        _appendingRows = false;
    }

    private string CurrentBrowseKey(string kind, string categoryId, string query)
        => $"{kind}\u001f{categoryId}\u001f{_selectedHeroGroupKey ?? ""}\u001f{_selectedHeroSkin ?? ""}\u001f{query}";

    private List<TexIndexEntry> GetFullIndexRows(string kind, string categoryId, string query, int offset, int cap, out int total)
    {
        var key = CurrentBrowseKey(kind, categoryId, query);
        if (ReferenceEquals(_sortedIndex, _index) && _sortedKey == key && _sortedRows != null)
        {
            total = _sortedRows.Count;
            return _sortedRows.Skip(offset).Take(cap).ToList();
        }
        var filtered = _index.Items
            .Where(i => i.Kind == kind)
            .Where(i => categoryId == ResourceCategories.AllId || string.Equals(i.CategoryId, categoryId, StringComparison.OrdinalIgnoreCase))
            .Where(i => MatchesQuery(i.Name, i.Bundle, query))
            .Where(i => MatchesHeroFilter(i.Name));

        var rows = SortAssetRows(filtered, kind, categoryId);
        _sortedIndex = _index;
        _sortedKey = key;
        _sortedRows = rows;
        total = rows.Count;
        return rows.Skip(offset).Take(cap).ToList();
    }

    private List<TexIndexEntry> GetLightweightTextureRows(string categoryId, string query, int offset, int cap, out int total)
    {
        if (categoryId == ResourceCategories.AllId && string.IsNullOrWhiteSpace(query) && !HasHeroFilter())
        {
            total = _index.LightweightTextureCount;
            return GetFirstLightweightTextureRows(offset, cap);
        }

        total = 0;
        bool needsCategory = categoryId != ResourceCategories.AllId;
        bool keepAllForGrouping = IsHeroSkinCategory(categoryId);
        var picked = new List<TexIndexEntry>(Math.Min(cap, 1000));

        if (needsCategory
            && _index.TextureRefsByCategory != null
            && _index.TextureRefsByCategory.TryGetValue(categoryId, out var refs))
        {
            foreach (var r in refs)
            {
                if (!MatchesQuery(r.Name, r.Bundle, query))
                    continue;
                if (!MatchesHeroFilter(r.Name))
                    continue;
                if (keepAllForGrouping || total >= offset && picked.Count < cap)
                    picked.Add(MakeLightweightEntry(r.Bundle, r.Name, categoryId));
                total++;
            }

            var rows = SortAssetRows(picked, ResourceKinds.Texture, categoryId);
            return keepAllForGrouping ? rows.Skip(offset).Take(cap).ToList() : rows;
        }

        foreach (var pair in _index.TextureNamesByBundle)
        {
            var bundle = pair.Key;
            if (pair.Value == null) continue;
            foreach (var name in pair.Value)
            {
                if (!MatchesQuery(name, bundle, query))
                    continue;
                if (!MatchesHeroFilter(name))
                    continue;

                string catId = null;
                if (needsCategory)
                {
                    catId = ResourceCategories.Categorize(ResourceKinds.Texture, name);
                    if (!string.Equals(catId, categoryId, StringComparison.OrdinalIgnoreCase))
                        continue;
                }

                if (keepAllForGrouping || total >= offset && picked.Count < cap)
                    picked.Add(MakeLightweightEntry(bundle, name, catId));
                total++;
            }
        }

        var sorted = SortAssetRows(picked, ResourceKinds.Texture, categoryId);
        return keepAllForGrouping ? sorted.Skip(offset).Take(cap).ToList() : sorted;
    }

    private List<TexIndexEntry> GetFirstLightweightTextureRows(int offset, int cap)
    {
        var picked = new List<TexIndexEntry>(Math.Min(cap, 1000));
        int seen = 0;
        foreach (var pair in _index.TextureNamesByBundle)
        {
            if (pair.Value == null) continue;
            foreach (var name in pair.Value)
            {
                if (seen++ < offset) continue;
                picked.Add(MakeLightweightEntry(pair.Key, name, null));
                if (picked.Count >= cap)
                    return SortAssetRows(picked, ResourceKinds.Texture, ResourceCategories.AllId);
            }
        }
        return SortAssetRows(picked, ResourceKinds.Texture, ResourceCategories.AllId);
    }

    private TexIndexEntry MakeLightweightEntry(string bundleName, string textureName, string categoryId)
    {
        categoryId ??= ResourceCategories.Categorize(ResourceKinds.Texture, textureName);
        string bundlePath = "";
        string source = "";
        _index.BundlePathsByName?.TryGetValue(bundleName, out bundlePath);
        _index.BundleSourcesByName?.TryGetValue(bundleName, out source);
        return new TexIndexEntry
        {
            Bundle = bundleName,
            BundlePath = bundlePath,
            Source = source,
            PathId = 0,
            Name = textureName,
            Kind = ResourceKinds.Texture,
            CategoryId = categoryId,
            CategoryLabel = ResourceCategories.Label(ResourceKinds.Texture, categoryId),
            Format = "点选解析"
        };
    }

    private static bool MatchesQuery(string name, string bundle, string query)
        => string.IsNullOrWhiteSpace(query)
        || (name ?? "").Contains(query, StringComparison.OrdinalIgnoreCase)
        || (bundle ?? "").Contains(query, StringComparison.OrdinalIgnoreCase);

    private bool MatchesHeroFilter(string name)
    {
        if (!HasHeroFilter()) return true;
        var parsed = NameParser.Parse(name);
        if (!parsed.IsHero) return false;
        if (!string.IsNullOrWhiteSpace(_selectedHeroGroupKey)
            && !string.Equals(parsed.GroupKey, _selectedHeroGroupKey, StringComparison.OrdinalIgnoreCase))
            return false;
        if (!string.IsNullOrWhiteSpace(_selectedHeroSkin)
            && !string.Equals(parsed.Skin, _selectedHeroSkin, StringComparison.OrdinalIgnoreCase))
            return false;
        return true;
    }

    private bool HasHeroFilter()
        => !string.IsNullOrWhiteSpace(_selectedHeroGroupKey)
        || !string.IsNullOrWhiteSpace(_selectedHeroSkin);

    private static int BrowseBatchSize(string kind, string categoryId, string query)
    {
        if (!string.IsNullOrWhiteSpace(query)) return kind == ResourceKinds.Texture ? 72 : 64;
        if (kind != ResourceKinds.Texture) return 64;
        if (categoryId is ResourceCategories.AllId or "other" or "fx" or "lightmap" or "sprite_anim") return 64;
        if (string.Equals(categoryId, ResourceCategories.CharacterId, StringComparison.OrdinalIgnoreCase)) return 48;
        return 64;
    }

    private TexRef ToTexRef(TexIndexEntry e)
    {
        return new TexRef
        {
            BundlePath = e.BundlePath,
            BundleName = e.Bundle,
            Source = e.Source,
            PathId = e.PathId,
            Name = e.Name,
            Kind = e.Kind,
            CategoryId = e.CategoryId,
            CategoryLabel = e.CategoryLabel,
            Width = e.Width,
            Height = e.Height,
            Format = e.Format,
            Display = DisplayName(e),
            Modded = e.Kind == ResourceKinds.Texture && PackService.Contains(_ws, e.Bundle, e.PathId, e.Name)
        };
    }

    private string DisplayName(TexIndexEntry e)
    {
        if (e.Kind != ResourceKinds.Texture) return e.Name;
        var parsed = NameParser.Parse(e.Name);
        if (!parsed.IsHero) return e.Name;
        if (IsHeroSkinCategory(e.CategoryId))
            return string.IsNullOrWhiteSpace(parsed.SubLabel) ? e.Name : parsed.SubLabel;
        var group = _naming.Display(parsed.GroupKey);
        return string.IsNullOrWhiteSpace(parsed.SubLabel) ? $"{group} / {parsed.Skin}" : $"{group} / {parsed.Skin} / {parsed.SubLabel}";
    }

    private List<TexIndexEntry> SortAssetRows(IEnumerable<TexIndexEntry> rows, string kind, string categoryId)
    {
        if (kind == ResourceKinds.Texture && IsHeroSkinCategory(categoryId))
        {
            return rows
                .OrderBy(HeroGroupOrder)
                .ThenBy(HeroSkinOrder)
                .ThenBy(HeroKindOrder)
                .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        return rows
            .OrderBy(i => i.CategoryLabel, StringComparer.OrdinalIgnoreCase)
            .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private List<(string title, List<TexRef> assets)> BuildAssetSections(string kind, string categoryId, List<TexRef> shown)
    {
        if (kind == ResourceKinds.Texture && IsHeroSkinCategory(categoryId))
            return shown.GroupBy(HeroSkinTitle).Select(g => (g.Key, g.ToList())).ToList();

        return categoryId == ResourceCategories.AllId
            ? shown.GroupBy(t => t.CategoryLabel ?? "其它").Select(g => (g.Key, g.ToList())).ToList()
            : new List<(string, List<TexRef>)> { (ResourceCategories.Label(kind, categoryId), shown) };
    }

    private static bool IsHeroSkinCategory(string categoryId)
        => string.Equals(categoryId, ResourceCategories.CharacterId, StringComparison.OrdinalIgnoreCase)
        || string.Equals(categoryId, "hero_card", StringComparison.OrdinalIgnoreCase)
        || string.Equals(categoryId, "hero_bust", StringComparison.OrdinalIgnoreCase)
        || string.Equals(categoryId, "hero_photo", StringComparison.OrdinalIgnoreCase);

    private string HeroSkinTitle(TexRef asset)
    {
        var parsed = NameParser.Parse(asset.Name);
        if (!parsed.IsHero) return asset.CategoryLabel ?? "其它";
        return $"{_naming.Display(parsed.GroupKey)} / {parsed.Skin}";
    }

    private static int HeroGroupOrder(TexIndexEntry e)
    {
        var parsed = NameParser.Parse(e.Name);
        if (!parsed.IsHero) return int.MaxValue;
        if (parsed.IsMonster) return 900000;
        return int.TryParse(parsed.HeroId, out var n) ? n : int.MaxValue - 1;
    }

    private static int HeroSkinOrder(TexIndexEntry e)
    {
        var parsed = NameParser.Parse(e.Name);
        return parsed.IsHero ? parsed.SkinOrder : 999;
    }

    private static int HeroKindOrder(TexIndexEntry e)
    {
        var parsed = NameParser.Parse(e.Name);
        return parsed.IsHero ? parsed.KindOrder : 999;
    }

    private async void ShowSections(List<(string title, List<TexRef> assets)> sections, int total, int cap, int offset, bool reset)
        => await RenderSectionsAsync(sections, total, cap, offset, reset);

    private async Task RenderSectionsAsync(List<(string title, List<TexRef> assets)> sections, int total, int cap, int offset, bool reset)
    {
        int seq = reset ? ++_loadSeq : _loadSeq;
        if (reset)
        {
            ClearFlow();
            ClearDetails();
            _lastSectionTitle = "";
        }
        else
        {
            RemoveLoadMoreHint();
        }

        int visibleCount = sections.Sum(s => s.assets.Count);
        if (total == 0 && reset)
        {
            AddPageCard("没有匹配的资源", "换个分类、角色、皮肤或搜索词。", MakeButton("清空搜索", (_, _) =>
            {
                _searchBox.Text = "";
                SelectCategory(ResourceCategories.AllId, null, null);
            }));
            _status.Text = "没有匹配的资源";
            return;
        }

        _status.Text = $"加载资源元数据... {Math.Min(total, offset + visibleCount)}/{total}";
        var thumbnailCards = new List<RoundedCard>();
        TexRef first = null;

        _flow.SuspendLayout();
        try
        {
            AddSectionsToFlow(sections, seq, thumbnailCards, ref first);
            AddLoadMoreHint();
        }
        finally
        {
            _flow.ResumeLayout();
        }

        if (reset && seq == _loadSeq && first != null)
            SelectAsset(first, null);

        if (seq == _loadSeq)
            _status.Text = $"已显示 {_browseLoadedCount}/{_browseTotalCount} 项，继续向下滚动自动加载";

        _ = LoadThumbnailsAsync(seq, thumbnailCards);
        if (reset)
            QueueLoadMoreCheck();
        await Task.Yield();
    }

    private void AddSectionsToFlow(List<(string title, List<TexRef> assets)> sections, int seq, List<RoundedCard> thumbnailCards, ref TexRef first)
    {
        foreach (var sec in sections)
        {
            if (!string.IsNullOrWhiteSpace(sec.title)
                && !string.Equals(_lastSectionTitle, sec.title, StringComparison.Ordinal))
            {
                AddHeader(sec.title);
                _lastSectionTitle = sec.title;
            }
            foreach (var asset in sec.assets)
            {
                if (seq != _loadSeq) return;
                first ??= asset;
                var card = MakeCard(asset, null);
                thumbnailCards.Add(card);
                _flow.Controls.Add(card);
            }
        }
    }

    private void RemoveLoadMoreHint()
    {
        var hints = _flow.Controls.Cast<Control>()
            .Where(c => string.Equals(c.Tag as string, "load_more", StringComparison.Ordinal))
            .ToList();
        foreach (var h in hints)
        {
            _flow.Controls.Remove(h);
            h.Dispose();
        }
    }

    private void AddLoadMoreHint()
    {
        RemoveLoadMoreHint();
        if (_browseLoadedCount >= _browseTotalCount) return;

        var panel = new Panel
        {
            Tag = "load_more",
            Width = HeaderWidth(),
            Height = Sc(42),
            Margin = new Padding(Sc(6), Sc(10), Sc(6), Sc(12)),
            BackColor = Theme.FlowBg,
            Cursor = Cursors.Hand
        };
        var label = Theme.Caption($"已显示 {_browseLoadedCount}/{_browseTotalCount}，继续向下滚动自动加载 · 点击立即加载");
        label.Dock = DockStyle.Fill;
        label.ForeColor = Theme.SubText;
        label.TextAlign = ContentAlignment.MiddleCenter;
        label.Cursor = Cursors.Hand;
        panel.Click += (_, _) => AppendMoreResources();
        label.Click += (_, _) => AppendMoreResources();
        panel.Controls.Add(label);
        _flow.Controls.Add(panel);
        _flow.SetFlowBreak(panel, true);
    }

    private void QueueLoadMoreCheck()
    {
        if (_loadMoreCheckQueued || !IsHandleCreated || IsDisposed) return;
        _loadMoreCheckQueued = true;
        try
        {
            BeginInvoke((Action)(() =>
            {
                _loadMoreCheckQueued = false;
                MaybeLoadMoreResources();
            }));
        }
        catch (InvalidOperationException)
        {
            _loadMoreCheckQueued = false;
        }
    }

    private void MaybeLoadMoreResources()
    {
        if (_activePage != PageBrowse || _index == null || _appendingRows) return;
        if (_browseLoadedCount <= 0 || _browseLoadedCount >= _browseTotalCount || _lastBrowseBatchSize <= 0) return;

        bool needFill = !_flow.VerticalScroll.Visible;
        int lastScrollValue = Math.Max(0,
            _flow.VerticalScroll.Maximum - _flow.VerticalScroll.LargeChange + 1);
        bool nearBottom = _flow.VerticalScroll.Value >= Math.Max(0, lastScrollValue - Sc(520));
        var hint = _flow.Controls.Cast<Control>()
            .FirstOrDefault(c => string.Equals(c.Tag as string, "load_more", StringComparison.Ordinal));
        bool hintVisible = hint != null && hint.Visible
            && hint.RectangleToScreen(hint.ClientRectangle)
                .IntersectsWith(_flow.RectangleToScreen(_flow.ClientRectangle));
        if (!needFill && !nearBottom && !hintVisible) return;

        AppendMoreResources();
    }

    private async void AppendMoreResources()
    {
        if (_appendingRows) return;
        _appendingRows = true;
        try
        {
            var kind = CurrentKind;
            var cat = _selectedCategoryId ?? ResourceCategories.AllId;
            var query = (_searchBox.Text ?? "").Trim();
            var key = CurrentBrowseKey(kind, cat, query);
            if (!string.Equals(key, _browseLoadKey, StringComparison.Ordinal)) return;

            int offset = _browseLoadedCount;
            int total;
            var rows = _index.Lightweight && kind == ResourceKinds.Texture
                ? GetLightweightTextureRows(cat, query, offset, _lastBrowseBatchSize, out total)
                : GetFullIndexRows(kind, cat, query, offset, _lastBrowseBatchSize, out total);
            if (!string.Equals(key, _browseLoadKey, StringComparison.Ordinal)) return;

            _browseTotalCount = total;
            if (rows.Count == 0)
            {
                RemoveLoadMoreHint();
                _browseLoadedCount = total;
                _status.Text = $"已显示 {_browseLoadedCount}/{_browseTotalCount} 项";
                return;
            }

            _browseLoadedCount += rows.Count;
            var shown = rows.Select(ToTexRef).ToList();
            var sections = BuildAssetSections(kind, cat, shown);
            await RenderSectionsAsync(sections, total, _lastBrowseBatchSize, offset, reset: false);
        }
        finally
        {
            _appendingRows = false;
        }

        QueueLoadMoreCheck();
    }

    private void ShowResourceList(List<(string title, List<TexRef> assets)> sections, int total, int cap)
    {
        _loadSeq++;
        _resourceList.BeginUpdate();
        _resourceList.Items.Clear();
        _resourceList.Groups.Clear();
        ClearDetails();

        foreach (var sec in sections)
        {
            var group = new ListViewGroup(sec.title);
            _resourceList.Groups.Add(group);
            foreach (var asset in sec.assets)
            {
                var item = new ListViewItem(asset.Display ?? asset.Name, group)
                {
                    Tag = asset,
                    ToolTipText = $"{asset.Name}\n{asset.BundleName}\n{asset.BundlePath}"
                };
                item.SubItems.Add(ResourceListGroupLabel(asset));
                item.SubItems.Add(asset.IsTexture
                    ? (asset.Width > 0 && asset.Height > 0 ? $"{asset.Width}x{asset.Height}" : "点选解析")
                    : ResourceKinds.Label(asset.Kind));
                item.SubItems.Add(asset.BundleName ?? "");
                if (asset.Modded)
                {
                    item.BackColor = Theme.CardMod;
                    item.ForeColor = Color.White;
                }
                _resourceList.Items.Add(item);
            }
        }

        if (total == 0)
        {
            var group = new ListViewGroup("没有匹配");
            _resourceList.Groups.Add(group);
            var item = new ListViewItem("没有匹配的资源", group);
            item.SubItems.Add("换个分类或搜索词");
            item.SubItems.Add("-");
            item.SubItems.Add("-");
            _resourceList.Items.Add(item);
        }

        _resourceList.EndUpdate();
        LayoutResourceListColumns();
        var suffix = total > cap ? $"，只列前 {cap} 项，搜索可缩小范围" : "";
        _status.Text = $"资源列表 {Math.Min(total, cap)}/{total} 项{suffix}";
    }

    private string ResourceListGroupLabel(TexRef asset)
    {
        if (asset.Kind != ResourceKinds.Texture) return asset.CategoryLabel ?? ResourceKinds.Label(asset.Kind);
        var parsed = NameParser.Parse(asset.Name);
        if (parsed.IsHero)
            return $"{_naming.Display(parsed.GroupKey)} / {parsed.Skin}";
        if (parsed.IsHandCard)
            return "手牌";
        return asset.CategoryLabel ?? "其它";
    }

    private async Task LoadThumbnailsAsync(int seq, List<RoundedCard> cards)
    {
        var candidates = cards
            .Where(c => c.Tag is TexRef a && a.IsTexture)
            .ToList();
        if (candidates.Count == 0) return;

        var groups = candidates
            .GroupBy(c => ((TexRef)c.Tag).BundlePath ?? "", StringComparer.OrdinalIgnoreCase)
            .Where(g => !string.IsNullOrWhiteSpace(g.Key) && File.Exists(g.Key))
            .Select(g => (path: g.Key, cards: g.ToList()))
            .ToList();
        int done = 0;
        var tasks = groups.Select(async group =>
        {
            if (seq != _loadSeq) return;
            await _thumbnailDecodeGate.WaitAsync();
            Dictionary<long, byte[]> previews = null;
            try
            {
                previews = await Task.Run(() =>
                {
                    var assets = group.cards.Select(c => (TexRef)c.Tag).ToList();
                    HydrateTextureGroup(assets);
                    return _engine.DecodePngBatch(group.path, assets.Select(a => a.PathId), Sc(SrcThumb));
                });
            }
            catch { }
            finally
            {
                _thumbnailDecodeGate.Release();
            }
            if (seq != _loadSeq || previews == null) return;
            foreach (var card in group.cards)
            {
                if (card.IsDisposed) continue;
                var asset = (TexRef)card.Tag;
                if (!previews.TryGetValue(asset.PathId, out var png)) continue;
                SetCardThumbnail(card, png);
                done++;
                if (done % 8 == 0) _status.Text = $"缩略图加载中... {done}/{candidates.Count}";
            }
        }).ToArray();

        await Task.WhenAll(tasks);
        if (seq == _loadSeq)
            _status.Text = $"已显示 {_browseLoadedCount}/{_browseTotalCount} 项，本批缩略图 {done}/{candidates.Count}";
    }

    private void HydrateTextureGroup(List<TexRef> assets)
    {
        var unresolved = assets.Where(a => a.PathId == 0).ToList();
        if (unresolved.Count == 0) return;

        List<TexRef> headers;
        try { headers = _engine.ListTextureHeaders(unresolved[0].BundlePath); }
        catch { return; }
        var byName = headers
            .Where(h => !string.IsNullOrWhiteSpace(h.Name))
            .GroupBy(h => h.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        foreach (var asset in unresolved)
        {
            if (!byName.TryGetValue(asset.Name ?? "", out var found)) continue;
            asset.PathId = found.PathId;
            asset.Width = found.Width;
            asset.Height = found.Height;
            asset.Format = found.Format;
            asset.Modded = PackService.Contains(_ws, asset.BundleName, asset.PathId);
        }
    }

    private static void SetCardThumbnail(RoundedCard card, byte[] png)
    {
        if (card.IsDisposed || card.Controls.Count == 0) return;
        var visual = card.Controls[0];
        if (visual.Controls.Count == 0 || visual.Controls[0] is not PictureBox pic) return;
        pic.Image?.Dispose();
        pic.Image = BytesToImage(png);
        pic.Invalidate();
    }

    private int HeaderWidth() => Math.Max(80, _flow.ClientSize.Width - _flow.Padding.Horizontal - 12);

    private void AddHeader(string title)
    {
        var p = new Panel
        {
            Tag = "header",
            Width = HeaderWidth(),
            Height = Sc(34),
            Margin = new Padding(Sc(6), Sc(12), Sc(6), Sc(2)),
            BackColor = Theme.FlowBg
        };
        p.Controls.Add(new Panel { Dock = DockStyle.Bottom, Height = Sc(1), BackColor = Theme.AccentDim });
        p.Controls.Add(new Label
        {
            Text = "  " + title,
            Dock = DockStyle.Fill,
            ForeColor = Theme.Accent,
            Font = Theme.UI(10f, true),
            TextAlign = ContentAlignment.MiddleLeft
        });
        _flow.Controls.Add(p);
        _flow.SetFlowBreak(p, true);
    }

    private RoundedCard MakeCard(TexRef asset, byte[] png)
    {
        var visual = new Panel { BackColor = Theme.PicBg };
        var pic = new PictureBox
        {
            Dock = DockStyle.Fill,
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Theme.PicBg,
            Image = png != null ? BytesToImage(png) : null,
            Tag = KindGlyph(asset.Kind)
        };
        pic.Paint += (_, e) =>
        {
            if (pic.Image != null) return;
            using var brush = new SolidBrush(Theme.Cyan);
            using var font = Theme.Mono(15f, true);
            var text = Convert.ToString(pic.Tag) ?? "RES";
            var size = e.Graphics.MeasureString(text, font);
            e.Graphics.DrawString(text, font, brush, (pic.Width - size.Width) / 2, (pic.Height - size.Height) / 2);
        };
        visual.Controls.Add(pic);

        var lbl = new Label
        {
            Text = CardText(asset),
            ForeColor = Theme.Text,
            Font = Theme.UI(8.2f),
            TextAlign = ContentAlignment.MiddleCenter,
            AutoEllipsis = true,
            BackColor = Color.Transparent
        };

        var card = new RoundedCard { Margin = new Padding(6), Tag = asset, AllowDrop = asset.IsTexture };
        var actions = new Panel
        {
            BackColor = Color.Transparent,
            Cursor = asset.IsTexture ? Cursors.Hand : Cursors.Default
        };
        actions.Paint += (_, e) => PaintCardActions(e.Graphics, actions.ClientRectangle, asset.IsTexture);
        actions.MouseClick += (_, e) =>
        {
            if (!asset.IsTexture) return;
            if (e.X < actions.Width / 2) PickAndReplaceTexture(asset, card);
            else ExportSingle(asset);
        };

        card.SetFill(asset.Modded ? Theme.CardMod : Theme.Card);
        card.GradientFill = asset.Modded ? Theme.CardAlt : Color.FromArgb(43, 42, 91);
        card.Controls.Add(visual);
        card.Controls.Add(lbl);
        card.Controls.Add(actions);
        LayoutCard(card);

        void Select(object s, EventArgs e) => SelectAsset(asset, png);
        card.Click += Select;
        visual.Click += Select;
        lbl.Click += Select;
        foreach (Control child in visual.Controls) child.Click += Select;
        AttachDragExport(card, () => asset);
        AttachDragExport(visual, () => asset);
        AttachDragExport(pic, () => asset);

        if (asset.IsTexture)
        {
            void DragEnter(object s, DragEventArgs e)
            {
                if (e.Data.GetDataPresent(DataFormats.FileDrop)) { e.Effect = DragDropEffects.Copy; card.SetBorder(Theme.Accent); }
                else e.Effect = DragDropEffects.None;
            }
            void DragLeave(object s, EventArgs e) => card.SetBorder(Color.Transparent);
            void DragDrop(object s, DragEventArgs e)
            {
                card.SetBorder(Color.Transparent);
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files is { Length: > 0 }) DoReplace(card, files[0]);
            }
            foreach (Control c in new Control[] { card, visual, lbl })
            {
                c.AllowDrop = true;
                c.DragEnter += DragEnter;
                c.DragLeave += DragLeave;
                c.DragDrop += DragDrop;
            }
            foreach (Control child in visual.Controls)
            {
                child.AllowDrop = true;
                child.DragEnter += DragEnter;
                child.DragLeave += DragLeave;
                child.DragDrop += DragDrop;
            }
        }

        var menu = new ContextMenuStrip();
        if (asset.IsTexture)
        {
            menu.Items.Add("替换贴图...", null, (_, _) => PickAndReplaceTexture(asset, card));
            menu.Items.Add("裁切替换...", null, (_, _) => PickAndReplaceTexture(asset, card, forceCrop: true));
            menu.Items.Add("导出 PNG...", null, (_, _) => ExportSingle(asset));
            menu.Items.Add(new ToolStripSeparator());
        }
        menu.Items.Add("导出所在 Bundle ZIP...", null, (_, _) => ExportBundleZip(new[] { asset }, asset.Name ?? asset.BundleName ?? "bundle"));
        menu.Items.Add("替换所在资源包...", null, (_, _) => ReplaceBundleFile(asset));
        menu.Items.Add("定位文件", null, (_, _) => LocateBundle(asset));
        if (IsAnimatedPortraitCandidate(asset))
            menu.Items.Add("替换动态立绘...", null, (_, _) => OpenAnimatedPortrait(asset));
        card.ContextMenuStrip = menu;
        visual.ContextMenuStrip = menu;
        lbl.ContextMenuStrip = menu;
        actions.ContextMenuStrip = menu;
        foreach (Control child in visual.Controls) child.ContextMenuStrip = menu;

        card.DoubleClick += (_, _) => { if (asset.IsTexture) PickAndReplaceTexture(asset, card); else LocateBundle(asset); };
        visual.DoubleClick += (_, _) => { if (asset.IsTexture) PickAndReplaceTexture(asset, card); else LocateBundle(asset); };
        lbl.DoubleClick += (_, _) => { if (asset.IsTexture) PickAndReplaceTexture(asset, card); else LocateBundle(asset); };

        return card;
    }

    private static string KindGlyph(string kind) => kind switch
    {
        ResourceKinds.Audio => "AUD",
        ResourceKinds.Text => "TXT",
        ResourceKinds.Mesh => "MESH",
        ResourceKinds.Animation => "ANIM",
        _ => "RES"
    };

    private static string CardText(TexRef asset)
    {
        var size = asset.IsTexture
            ? asset.Width > 0 && asset.Height > 0 ? $"{asset.Width}x{asset.Height}" : "点选预览"
            : asset.Kind;
        return $"{asset.Display ?? asset.Name}\n{size}";
    }

    private void PaintCardActions(Graphics g, Rectangle bounds, bool enabled)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var left = new Rectangle(0, 0, Math.Max(1, bounds.Width / 2 - 3), bounds.Height - 1);
        var right = new Rectangle(bounds.Width / 2 + 3, 0, Math.Max(1, bounds.Width - bounds.Width / 2 - 3), bounds.Height - 1);
        using var leftBrush = new SolidBrush(enabled ? Theme.Accent : Theme.Muted);
        using var rightBrush = new SolidBrush(enabled ? Theme.AccentDim : Theme.Muted);
        using var font = Theme.UI(8f, true);
        using (var p = Theme.RoundRect(left, 10)) g.FillPath(leftBrush, p);
        using (var p = Theme.RoundRect(right, 10)) g.FillPath(rightBrush, p);
        DrawCentered(g, "替换", font, enabled ? Color.White : Theme.SubText, left);
        DrawCentered(g, "导出", font, enabled ? Color.White : Theme.SubText, right);
    }

    private static void DrawCentered(Graphics g, string text, Font font, Color color, Rectangle rect)
    {
        TextRenderer.DrawText(g, text, font, rect, color,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
    }

    private void LayoutCard(RoundedCard card)
    {
        var visual = card.Controls[0];
        var lbl = (Label)card.Controls[1];
        var actions = card.Controls.Count > 2 ? card.Controls[2] : null;
        int pad = Sc(Pad), gap = Sc(5), lblH = Sc(46), actionH = Sc(28), bottom = Sc(7);
        visual.Location = new Point(pad, pad);
        visual.Size = new Size(_thumb, _thumb);
        lbl.Location = new Point(pad, pad + _thumb + gap);
        lbl.Size = new Size(_thumb, lblH);
        if (actions != null)
        {
            actions.Location = new Point(pad, lbl.Bottom + Sc(2));
            actions.Size = new Size(_thumb, actionH);
        }
        card.Size = new Size(_thumb + pad * 2, pad + _thumb + gap + lblH + actionH + bottom);
    }

    private void SelectAsset(TexRef asset, byte[] thumbnail)
    {
        _selectedAsset = asset;
        UpdateDetailText(asset);
        var oldPreview = _preview.Image;
        _preview.Image = null;
        oldPreview?.Dispose();
        _animationPreviewStream?.Dispose();
        _animationPreviewStream = null;
        _previewSeq++;

        if (asset.IsTexture && asset.PathId == 0)
        {
            SetDetailButtons(false, false);
            _exportBundleZipBtn.Enabled = true;
            _locateBtn.Enabled = true;
            _status.Text = "解析选中贴图 PathId / 尺寸...";
            Task.Run(() => HydrateTexture(asset)).ContinueWith(t =>
            {
                if (!IsHandleCreated || _selectedAsset != asset) return;
                BeginInvoke(() =>
                {
                    if (_selectedAsset != asset) return;
                    if (t.Result)
                    {
                        UpdateDetailText(asset);
                        SetDetailButtons(true, true);
                        UpdateSelectedListRow(asset);
                        LoadPreviewAsync(asset, thumbnail);
                    }
                    else
                    {
                        _detailHint.Text = "未能在 bundle 里解析到同名 Texture2D；可导出/定位整个 bundle。";
                        SetDetailButtons(true, false);
                        _status.Text = "解析失败：" + asset.Name;
                    }
                });
            });
            return;
        }

        SetDetailButtons(true, asset.IsTexture);
        LoadPreviewAsync(asset, thumbnail);
    }

    private void UpdateDetailText(TexRef asset)
    {
        _detailTitle.Text = asset.Name;
        var path = ShortDisplayPath(asset.BundlePath);
        _detailMeta.Text =
            $"类型: {ResourceKinds.Label(asset.Kind)}\n" +
            $"分类: {asset.CategoryLabel}\n" +
            $"尺寸: {(asset.IsTexture ? (asset.Width > 0 ? $"{asset.Width}x{asset.Height}" : "点选后解析") : "-")}\n" +
            $"格式: {asset.Format ?? "-"}\n" +
            $"来源: {asset.Source ?? "-"}";
        _detailPath.Text = $"Bundle: {asset.BundleName}\nPathId: {asset.PathId}\n位置: {path}";
        _detailHint.Text = asset.IsTexture
            ? "贴图支持资产级替换、裁切、导出 PNG。"
            : "非贴图当前支持索引、定位、替换整个资源包；资产级音频/动画写回仍需单独编码器。";
    }

    private static string ShortDisplayPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "-";
        var file = Path.GetFileName(path);
        var parent = Path.GetFileName(Path.GetDirectoryName(path));
        return string.IsNullOrWhiteSpace(parent) ? file : parent + "\\" + file;
    }

    private bool HydrateTexture(TexRef asset)
    {
        if (!asset.IsTexture || asset.PathId != 0) return true;
        if (string.IsNullOrWhiteSpace(asset.BundlePath) || !File.Exists(asset.BundlePath)) return false;
        var found = _engine.FindTextureByName(asset.BundlePath, asset.Name);
        if (found == null) return false;
        asset.PathId = found.PathId;
        asset.Width = found.Width;
        asset.Height = found.Height;
        asset.Format = found.Format;
        asset.Modded = PackService.Contains(_ws, asset.BundleName, asset.PathId);
        return true;
    }

    private bool EnsureTextureReady(TexRef asset)
    {
        if (asset == null || !asset.IsTexture) return false;
        if (asset.PathId != 0) return true;
        _status.Text = "正在解析选中贴图...";
        try
        {
            if (!HydrateTexture(asset))
            {
                MessageBox.Show("无法在 bundle 中解析这张贴图，不能做资产级替换/导出。", "提示");
                return false;
            }
            UpdateDetailText(asset);
            UpdateSelectedListRow(asset);
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show("解析贴图失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    private void LoadPreviewAsync(TexRef asset, byte[] thumbnail)
    {
        if (!asset.IsTexture || asset.PathId == 0) return;
        int seq = ++_previewSeq;
        string root = AnimatedPortraitRoot();
        Task.Run(() =>
        {
            string state = null;
            try
            {
                var receipt = PortraitReplacement.ReadReceipt(root, asset.Name);
                if (receipt?.Texture == asset.Name)
                {
                    if (PortraitReplacement.IsInstalled(root, receipt))
                    {
                        state = "动态立绘已写入并校验 · " + receipt.SourceName + " · 本地预览（前 6 秒），游戏内效果待确认";
                        if (File.Exists(receipt.Preview)) return (Bytes: File.ReadAllBytes(receipt.Preview), State: state, Animated: true);
                        state += " · 预览文件缺失";
                    }
                    else state = "动态立绘记录已失效：资源已更新或重置，当前显示静态贴图";
                }
                return (Bytes: _engine.DecodePng(asset.BundlePath, asset.PathId, Sc(620)), State: state, Animated: false);
            }
            catch { return (Bytes: thumbnail, State: state, Animated: false); }
        }).ContinueWith(t =>
        {
            if (!IsHandleCreated || _selectedAsset != asset || seq != _previewSeq) return;
            BeginInvoke(() =>
            {
                if (_selectedAsset != asset || t.Result.Bytes == null || seq != _previewSeq) return;
                var oldPreview = _preview.Image;
                _preview.Image = null;
                oldPreview?.Dispose();
                _animationPreviewStream?.Dispose();
                _animationPreviewStream = new MemoryStream(t.Result.Bytes);
                _preview.Image = Image.FromStream(_animationPreviewStream);
                if (t.Result.State != null) _detailHint.Text = t.Result.State;
                _status.Text = t.Result.State ?? $"已预览：{asset.Name}";
            });
        });
    }

    private void UpdateSelectedListRow(TexRef asset)
    {
        if (_resourceList.SelectedItems.Count == 0) return;
        var item = _resourceList.SelectedItems[0];
        if (!ReferenceEquals(item.Tag, asset)) return;
        if (item.SubItems.Count >= 3)
            item.SubItems[2].Text = asset.Width > 0 && asset.Height > 0 ? $"{asset.Width}x{asset.Height}" : "Texture2D";
    }

    private void SetDetailButtons(bool hasAsset, bool isTexture)
    {
        _replaceTextureBtn.Enabled = hasAsset && isTexture;
        _replaceAnimationBtn.Enabled = hasAsset && isTexture && IsAnimatedPortraitCandidate(_selectedAsset);
        _exportPngBtn.Enabled = hasAsset && isTexture;
        _exportBundleZipBtn.Enabled = hasAsset;
        _replaceBundleBtn.Enabled = hasAsset;
        _locateBtn.Enabled = hasAsset;
    }

    private void OnFlowWheel(object sender, MouseEventArgs e)
    {
        if ((ModifierKeys & Keys.Control) == 0)
        {
            QueueLoadMoreCheck();
            return;
        }
        if (e is HandledMouseEventArgs he) he.Handled = true;
        int next = Math.Clamp(_thumb + (e.Delta > 0 ? Sc(14) : -Sc(14)), Sc(MinThumb), Sc(MaxThumb));
        if (next == _thumb) return;
        _thumb = next;
        _flow.SuspendLayout();
        foreach (Control c in _flow.Controls)
            if (c is RoundedCard { Tag: TexRef } card) LayoutCard(card);
        _flow.ResumeLayout();
        _status.Text = $"缩略图大小: {_thumb}px";
    }

    private void PickAndReplaceTexture(TexRef asset, RoundedCard card = null, bool forceCrop = false)
    {
        if (!EnsureTextureReady(asset)) return;
        using var dlg = new OpenFileDialog { Filter = "图片|*.png;*.jpg;*.jpeg;*.bmp;*.webp" };
        if (dlg.ShowDialog() != DialogResult.OK) return;
        DoReplace(card, dlg.FileName, forceCrop, asset);
    }

    private void DoReplace(RoundedCard card, string imagePath, bool forceCrop = false, TexRef explicitAsset = null)
    {
        var asset = explicitAsset ?? (TexRef)card.Tag;
        if (AnimatedPortraitDialog.IsVideo(imagePath))
        {
            if (!IsAnimatedPortraitCandidate(asset)) { _status.Text = "视频替换目前仅支持角色卡面立绘。"; return; }
            OpenAnimatedPortrait(asset, imagePath);
            return;
        }
        if (!asset.IsTexture)
        {
            _status.Text = "该资源不是 Texture2D，不能做贴图级替换。";
            return;
        }
        if (!EnsureTextureReady(asset)) return;

        try
        {
            byte[] cropped = null;
            int sw, sh;
            using (var probe = Image.FromFile(imagePath)) { sw = probe.Width; sh = probe.Height; }
            bool needCrop = forceCrop ||
                Math.Abs((double)sw / sh - (double)asset.Width / asset.Height) > 0.02;
            if (needCrop)
            {
                using var crop = new CropDialog(imagePath, asset.Width, asset.Height, asset.Display ?? asset.Name);
                if (crop.ShowDialog(this) != DialogResult.OK) { _status.Text = "已取消"; return; }
                cropped = crop.ResultPng;
            }

            _status.Text = $"替换 {asset.Name} ...";
            if (cropped != null)
                _engine.ReplaceInPlaceFromBytes(asset.BundlePath, asset.PathId, cropped, _backupDir, asset.BundleName);
            else
                _engine.ReplaceInPlace(asset.BundlePath, asset.PathId, imagePath, _backupDir, asset.BundleName);

            PackService.Upsert(_ws, new ModEntry
            {
                Bundle = asset.BundleName,
                PathId = asset.PathId,
                TextureName = asset.Name,
                Width = asset.Width,
                Height = asset.Height,
                Label = asset.CategoryLabel
            });
            PackService.SaveWorkspace(_folder, _ws);

            asset.Modded = true;
            if (card != null)
            {
                var visual = card.Controls[0];
                if (visual.Controls[0] is PictureBox pic)
                {
                    pic.Image?.Dispose();
                    pic.Image = BytesToImage(_engine.DecodePng(asset.BundlePath, asset.PathId, Sc(SrcThumb)));
                }
                card.SetFill(Theme.CardMod);
            }
            SelectAsset(asset, null);
            UpdateDashboard();
            _status.Text = $"已替换 {asset.Name}，原始资源已备份。";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"替换失败：{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            _status.Text = "替换失败";
        }
    }

    private void AttachDragExport(Control control, Func<TexRef> assetProvider)
    {
        Point start = Point.Empty;
        bool armed = false;
        bool dragging = false;

        control.MouseDown += (_, e) =>
        {
            var asset = assetProvider();
            if (e.Button != MouseButtons.Left || asset is not { IsTexture: true }) return;
            start = e.Location;
            armed = true;
        };
        control.MouseUp += (_, _) => armed = false;
        control.MouseMove += (s, e) =>
        {
            if (!armed || dragging || e.Button != MouseButtons.Left) return;
            var dragSize = SystemInformation.DragSize;
            var dragRect = new Rectangle(
                start.X - dragSize.Width / 2,
                start.Y - dragSize.Height / 2,
                dragSize.Width,
                dragSize.Height);
            if (dragRect.Contains(e.Location)) return;

            var asset = assetProvider();
            if (asset is not { IsTexture: true }) return;
            dragging = true;
            try { StartDragExport((Control)s, asset); }
            finally
            {
                armed = false;
                dragging = false;
            }
        };
    }

    private void StartDragExport(Control source, TexRef asset)
    {
        if (!EnsureTextureReady(asset)) return;
        try
        {
            var dir = Path.Combine(Path.GetTempPath(), "JixModMaker", "drag-export");
            Directory.CreateDirectory(dir);
            var outPath = Path.Combine(dir, SafeFileName(asset.Name) + ".png");
            File.WriteAllBytes(outPath, _engine.DecodePng(asset.BundlePath, asset.PathId, 0));

            var data = new DataObject();
            data.SetData(DataFormats.FileDrop, new[] { outPath });
            data.SetData(DataFormats.Text, asset.Name ?? Path.GetFileName(outPath));
            _status.Text = "拖到文件夹即可导出：" + Path.GetFileName(outPath);
            source.DoDragDrop(data, DragDropEffects.Copy);
        }
        catch (Exception ex)
        {
            MessageBox.Show("拖动导出失败: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ReplaceBundleFile(TexRef asset)
    {
        using var dlg = new OpenFileDialog { Filter = "Unity资源包|*.bundle;__data|所有文件|*.*" };
        if (dlg.ShowDialog() != DialogResult.OK) return;

        if (MessageBox.Show($"将用所选文件覆盖当前资源包：\n{asset.BundlePath}\n\n会先备份原文件。继续？",
            "替换所在资源包", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;

        try
        {
            Directory.CreateDirectory(_backupDir);
            var backup = Path.Combine(_backupDir, ModEngine.SafeBackupName(asset.BundleName));
            if (!File.Exists(backup)) File.Copy(asset.BundlePath, backup);
            File.Copy(dlg.FileName, asset.BundlePath, overwrite: true);
            _status.Text = "已替换所在资源包：" + asset.BundleName;
            LoadIndexAsync(true);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"资源包替换失败：{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ExportModifiedBundlesZip()
    {
        if (_folder == null)
        {
            _status.Text = "请先打开目录";
            return;
        }
        if (_ws == null || _ws.Entries.Count == 0)
        {
            MessageBox.Show("当前作品集没有已修改记录。", "提示");
            return;
        }

        using var selection = new ExportSelectionDialog(_ws.Entries, true);
        if (selection.ShowDialog(this) != DialogResult.OK) return;
        var assets = selection.SelectedEntries
            .Select(e =>
            {
                var indexed = FindAssetForEntry(e);
                if (indexed != null) return indexed;
                var fallback = Path.Combine(_folder, e.Bundle ?? "");
                return File.Exists(fallback)
                    ? new TexRef { BundlePath = fallback, BundleName = e.Bundle, Name = e.TextureName, Source = "作品集", PathId = e.PathId, Kind = ResourceKinds.Texture }
                    : null;
            })
            .Where(a => a != null)
            .ToList();

        ExportBundleZip(assets, "已改Bundle");
    }

    private static bool IsAnimatedPortraitCandidate(TexRef asset) =>
        asset is { IsTexture: true } && asset.Name?.StartsWith("UT_Hero_Card_", StringComparison.Ordinal) == true;

    private void OpenAnimatedPortrait(TexRef asset, string videoFile = null)
    {
        if (!IsAnimatedPortraitCandidate(asset)) return;
        if (!EnsureTextureReady(asset)) return;
        string root = AnimatedPortraitRoot();
        using var dialog = new AnimatedPortraitDialog(root, asset.Name, videoFile, new Size(asset.Width, asset.Height));
        dialog.ShowDialog(this);
        _status.Text = dialog.ResultMessage.Replace('\n', ' ');
        if (_selectedAsset == asset)
        {
            UpdateDetailText(asset);
            LoadPreviewAsync(asset, null);
        }
    }

    private string AnimatedPortraitRoot() => string.Equals(Path.GetFileName(_folder), "StandaloneWindows64", StringComparison.OrdinalIgnoreCase)
        && Directory.Exists(_index?.HotDir) ? _index.HotDir : _folder;

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        if (_preview != null)
        {
            var image = _preview.Image;
            _preview.Image = null;
            image?.Dispose();
        }
        _animationPreviewStream?.Dispose();
        base.OnFormClosed(e);
    }

    private void ExportBundleZip(IEnumerable<TexRef> assets, string defaultName)
    {
        var bundles = assets
            .Where(a => a != null && !string.IsNullOrWhiteSpace(a.BundlePath) && File.Exists(a.BundlePath))
            .GroupBy(a => a.BundlePath, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
        if (bundles.Count == 0)
        {
            MessageBox.Show("没有可导出的 bundle 文件。若是旧记录，请先刷新索引。", "提示");
            return;
        }

        using var dlg = new SaveFileDialog
        {
            Filter = "Bundle 压缩包|*.zip",
            FileName = SafeFileName(defaultName) + ".zip"
        };
        if (dlg.ShowDialog() != DialogResult.OK) return;

        try
        {
            int files = ReplacementZip.Export(_folder, bundles.Select(b => b.BundlePath), dlg.FileName);
            _status.Text = $"已导出直接替换 ZIP：{files} 个文件";
            MessageBox.Show($"导出成功，共 {files} 个文件。\n解压到目标设备对应的资源根目录即可覆盖。\n请使用目标手机版本的资源制作 Mod；导出不会转换 PC / 手机资源格式。", "导出成功");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"导出 bundle ZIP 失败：{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            _status.Text = "导出 bundle ZIP 失败";
        }
    }

    private TexRef FindAssetForEntry(ModEntry entry)
    {
        var indexed = _index?.FindTextureTarget(entry.Bundle, entry.TextureName, entry.PathId);
        return indexed == null ? null : ToTexRef(indexed);
    }

    private static string UniqueZipName(HashSet<string> used, string name)
    {
        var normalized = name.Replace('\\', '/');
        if (used.Add(normalized)) return normalized;
        var dir = Path.GetDirectoryName(normalized)?.Replace('\\', '/');
        var file = Path.GetFileNameWithoutExtension(normalized);
        var ext = Path.GetExtension(normalized);
        for (int i = 2; ; i++)
        {
            var candidate = string.IsNullOrWhiteSpace(dir) ? $"{file}_{i}{ext}" : $"{dir}/{file}_{i}{ext}";
            if (used.Add(candidate)) return candidate;
        }
    }

    private void ExportPack()
    {
        if (_folder == null) { _status.Text = "请先打开目录"; return; }
        if (_ws.Entries.Count == 0)
        {
            MessageBox.Show("当前还没有改过的贴图，无法导出图包。", "提示");
            return;
        }

        using var selection = new ExportSelectionDialog(_ws.Entries, false);
        if (selection.ShowDialog(this) != DialogResult.OK) return;
        using var dlg = new SaveFileDialog { Filter = $"吉星图包|*{PackService.PackExt}", FileName = "我的资源包" + PackService.PackExt };
        if (dlg.ShowDialog() != DialogResult.OK) return;

        try
        {
            var images = new List<NamedImage>();
            foreach (var e in selection.SelectedEntries)
            {
                var asset = FindAssetForEntry(e);
                if (asset != null && asset.PathId == 0)
                    HydrateTexture(asset);
                string bundlePath = asset?.BundlePath ?? Path.Combine(_folder, e.Bundle);
                long pathId = asset?.PathId > 0 ? asset.PathId : e.PathId;
                if (!File.Exists(bundlePath) || string.IsNullOrEmpty(e.TextureName)) continue;
                try
                {
                    images.Add(new NamedImage
                    {
                        TextureName = e.TextureName,
                        Width = e.Width,
                        Height = e.Height,
                        Png = _engine.DecodePng(bundlePath, pathId, 0),
                        Label = e.Label
                    });
                }
                catch { }
            }
            PackService.ExportV2(images, dlg.FileName, Path.GetFileNameWithoutExtension(dlg.FileName), "", "PC", "资源");
            _status.Text = $"已导出图包：{images.Count} 张 -> {Path.GetFileName(dlg.FileName)}";
            MessageBox.Show($"图包导出成功。\n\n包含 {images.Count} 张贴图\n{dlg.FileName}", "导出成功");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"导出失败：{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async void ImportPack()
    {
        if (_folder == null) { MessageBox.Show("请先打开要应用到的目录。", "提示"); return; }
        using var dlg = new OpenFileDialog { Filter = $"吉星图包|*{PackService.PackExt}" };
        if (dlg.ShowDialog() != DialogResult.OK) return;

        try
        {
            _status.Text = "正在应用图包...";
            if (_index == null)
                _index = _indexSvc.Load(_folder, heroOnly: false, includeHotCache: _includeHotCache, recursive: _recursiveFolder)
                    ?? await Task.Run(() => _indexSvc.Build(_folder, null, heroOnly: false, includeHotCache: _includeHotCache, recursive: _recursiveFolder));

            PackService.ImportResult res;
            if (PackService.PeekFormat(dlg.FileName) >= 2)
                res = PackService.ImportV2(dlg.FileName, _index, _folder, _engine, _backupDir, _ws);
            else
                res = PackService.Import(dlg.FileName, _folder, _engine, _backupDir, _ws);

            PackService.SaveWorkspace(_folder, _ws);
            UpdateDashboard();
            ShowCurrentAssets();
            string msg = $"图包「{res.PackName}」应用完成：\n\n成功匹配 {res.Applied} 张，写入 {res.Targets} 个位置";
            if (res.Missing > 0) msg += $"\n{res.Missing} 张未匹配（当前目录里没有对应立绘）";
            if (res.Missing > 0) msg += "\n\n可切换到另一个资源目录，再导入同一个图包继续覆盖。";
            MessageBox.Show(msg, "导入完成");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"导入失败：{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            _status.Text = "导入失败";
        }
    }

    private async void MigrateOldMods()
    {
        string oldRoot;
        using (var dlg = new FolderBrowserDialog { Description = "选择旧 mod 所在目录（递归找 .bundle / __data）" })
        {
            if (dlg.ShowDialog() != DialogResult.OK) return;
            oldRoot = dlg.SelectedPath;
        }

        string outDir = Path.Combine(oldRoot, "_迁移结果");
        if (MessageBox.Show($"将把\n{oldRoot}\n下的旧 mod 迁移为新版图包，输出到：\n{outDir}\n\n继续？",
            "迁移旧 mod", MessageBoxButtons.OKCancel) != DialogResult.OK) return;

        try
        {
            var mig = new MigrationService();
            var rep = await Task.Run(() => mig.Migrate(oldRoot, outDir,
                (d, t, f) => { if (IsHandleCreated) BeginInvoke(() => _status.Text = $"迁移中... {d}/{t} {f}"); }));

            string groups = string.Join("\n", rep.GroupCounts.Select(k => $"  {k.Key}: {k.Value} 张"));
            _status.Text = $"迁移完成：{rep.Packs.Count} 个图包";
            MessageBox.Show($"迁移完成。\n\n处理 {rep.Files} 个旧文件，{rep.Textures} 张贴图\n\n{groups}\n\n输出：\n{outDir}", "迁移完成");
            try { System.Diagnostics.Process.Start("explorer.exe", outDir); } catch { }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"迁移失败：{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            _status.Text = "迁移失败";
        }
    }

    private void RestoreAll()
    {
        if (_folder == null || !Directory.Exists(_backupDir))
        {
            _status.Text = "没有可还原的备份";
            return;
        }
        if (MessageBox.Show("把所有备份过的资源还原为原始文件？", "确认", MessageBoxButtons.YesNo) != DialogResult.Yes) return;

        var targetByBundle = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (_index?.Lightweight == true && _index.BundlePathsByName != null)
        {
            foreach (var pair in _index.BundlePathsByName)
                targetByBundle[ModEngine.SafeBackupName(pair.Key)] = pair.Value;
        }
        else
        {
            targetByBundle = (_index?.Items ?? new List<TexIndexEntry>())
                .GroupBy(i => i.Bundle, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => ModEngine.SafeBackupName(g.Key), g => g.First().BundlePath, StringComparer.OrdinalIgnoreCase);
        }

        int n = 0;
        foreach (var backup in Directory.GetFiles(_backupDir))
        {
            var name = Path.GetFileName(backup);
            var target = targetByBundle.GetValueOrDefault(name) ?? Path.Combine(_folder, name);
            if (!File.Exists(target)) continue;
            File.Copy(backup, target, overwrite: true);
            n++;
        }

        string wrappedRoot = Path.Combine(_backupDir, "__wrapped__");
        if (Directory.Exists(wrappedRoot))
        {
            foreach (var backup in Directory.GetFiles(wrappedRoot, "__data", SearchOption.AllDirectories))
            {
                string target = Path.Combine(_folder, Path.GetRelativePath(wrappedRoot, backup));
                if (!ResourceLocator.IsWrappedData(target)) continue;
                File.Copy(backup, target, overwrite: true);
                n++;
            }
        }

        _ws = new ModManifest();
        PackService.SaveWorkspace(_folder, _ws);
        UpdateDashboard();
        LoadIndexAsync(true);
        _status.Text = $"已还原 {n} 个资源文件";
    }

    private void ClearDetails()
    {
        _selectedAsset = null;
        _preview.Image?.Dispose();
        _preview.Image = null;
        _detailTitle.Text = "未选择资源";
        _detailMeta.Text = "";
        _detailPath.Text = "";
        _detailHint.Text = "";
        SetDetailButtons(false, false);
    }

    private static string SafeFileName(string s)
    {
        string r = string.Concat((s ?? "asset").Split(Path.GetInvalidFileNameChars()));
        return string.IsNullOrEmpty(r) ? "asset" : r;
    }

    private void ExportSingle(TexRef asset)
    {
        if (!asset.IsTexture) return;
        if (!EnsureTextureReady(asset)) return;
        using var dlg = new SaveFileDialog { Filter = "PNG 图片|*.png", FileName = SafeFileName(asset.Name) + ".png" };
        if (dlg.ShowDialog() != DialogResult.OK) return;
        try
        {
            File.WriteAllBytes(dlg.FileName, _engine.DecodePng(asset.BundlePath, asset.PathId, 0));
            _status.Text = "已导出 " + Path.GetFileName(dlg.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show("导出失败: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void LocateBundle(TexRef asset)
    {
        if (!File.Exists(asset.BundlePath))
        {
            _status.Text = "找不到对应资源包文件";
            return;
        }
        LocatePath(asset.BundlePath);
    }

    private void LocatePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            _status.Text = "找不到文件";
            return;
        }
        try { Process.Start("explorer.exe", $"/select,\"{path}\""); }
        catch (Exception ex) { _status.Text = "定位失败: " + ex.Message; }
    }

    private static string ShortHash(string value)
        => string.IsNullOrWhiteSpace(value) ? "-" : value[..Math.Min(12, value.Length)];

    private static Image BytesToImage(byte[] png)
    {
        using var ms = new MemoryStream(png);
        return Image.FromStream(ms);
    }

    private sealed class CategoryButtonTag
    {
        public string CategoryId { get; }
        public string HeroGroupKey { get; }
        public string Skin { get; }

        public CategoryButtonTag(string categoryId, string heroGroupKey, string skin)
        {
            CategoryId = categoryId;
            HeroGroupKey = heroGroupKey;
            Skin = skin;
        }
    }

    private sealed class CharacterGroupInfo
    {
        public string GroupKey { get; set; }
        public string Display { get; set; }
        public int Count { get; set; }
        public int Sort { get; set; }
        public List<CharacterSkinInfo> Skins { get; } = new();
        public Dictionary<string, CharacterSkinInfo> SkinMap { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class CharacterSkinInfo
    {
        public string Name { get; set; }
        public int Count { get; set; }
        public int Order { get; set; }
    }

    private sealed class KindOption
    {
        public string Id { get; }
        public string Label { get; }
        public KindOption(string id, string label) { Id = id; Label = label; }
        public override string ToString() => Label;
    }
}
