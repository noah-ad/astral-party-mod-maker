using System.Drawing;
using System.Diagnostics;
using System.IO.Compression;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Windows.Forms;

namespace JixModMaker;

public partial class MainForm : Form
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
    private ModificationCatalog _modifications;
    private CancellationTokenSource _modVerification;
    private int _modVerificationSeq;
    private CancellationTokenSource _indexLoad;
    private (string Path, bool Game) _source = (null, true);
    private GameEdition? _discoverEdition;
    private GameEdition? _edition;
    private (string Path, bool Game, bool Rebuild, bool Advanced, GameEdition? Edition) _activeRequest;
    private string _loadError;
    private readonly LoadStrip _loadStrip = new();
    private Label _browseLoading, _packLoading, _toolsLoading;
    private Func<string> _verificationText = () => UiText.T("修改状态尚未核验");
    private string _modVerificationInfo => _verificationText();
    private TexRef _selectedAsset;
    private int _loadSeq;
    private int _previewSeq;
    private CancellationTokenSource _moviePreview;
    private bool _openingSkillMovie;
    private bool _restoring;
    private MemoryStream _animationPreviewStream;
    private Button _selectedCategoryButton;
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

    public static string Version => AppBuildInfo.Version;
    private const string PageDashboard = "dashboard";
    private const string PageBrowse = "browse";
    private const string PagePack = "pack";
    private const string PageTools = "tools";
    private const int MinThumb = 72, MaxThumb = 220, SrcThumb = 300, Pad = 9, ThumbDef = 116;
    private const int ThumbDecodeConcurrency = 6;
    private readonly SemaphoreSlim _thumbnailDecodeGate = new(ThumbDecodeConcurrency, ThumbDecodeConcurrency);
    private int _thumb = ThumbDef;
    private int _layoutDpi = 96;
    private int Sc(int v) => (int)Math.Round(v * _layoutDpi / 96f);
    private bool _layingOutBrowseFilters;

    private readonly BufferedPanel _leftPanel = new() { Dock = DockStyle.Left, Width = 252, BackColor = Theme.Bg };
    private readonly BufferedPanel _rightPanel = new() { Dock = DockStyle.Right, Width = 320, BackColor = Theme.Bar };
    private readonly BufferedPanel _centerPanel = new() { Dock = DockStyle.Fill, BackColor = Theme.FlowBg };
    private readonly GradientPanel _topBar = new()
    {
        Dock = DockStyle.Top,
        Height = 62,
        Padding = new Padding(12, 9, 12, 9),
        GradientStart = Theme.Bar,
        GradientEnd = Theme.Bar,
        GradientAngle = 0f
    };
    private readonly SideListFlow _categoryList = new() { Dock = DockStyle.Fill, BackColor = Theme.Bg, Padding = new Padding(8) };
    private GridFlow _flow;
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
    private readonly GameArtHeading _sideTitle = new() { BackColor = Theme.Bg, Font = Theme.UI(12, true) };
    private readonly GameArtHeading _pageTitle = new() { BackColor = Theme.Bar };
    private readonly BufferedPanel _browseFilters = new()
    {
        Dock = DockStyle.Right,
        Width = 560,
        BackColor = Theme.Bar
    };
    private readonly Label _status = new()
    {
        Dock = DockStyle.Fill,
        ForeColor = Theme.SubText,
        BackColor = Theme.Bar,
        TextAlign = ContentAlignment.MiddleLeft,
        Padding = new Padding(12, 0, 0, 0),
        Text = UiText.T("就绪")
    };

    private readonly ResourcePicker _kindBox = new();
    private readonly TextBox _searchBox = new() { BorderStyle = BorderStyle.FixedSingle, PlaceholderText = UiText.T("搜索资源名 / bundle") };
    private LibrarySearch _searchSurface;
    private Button _refreshIndexBtn;
    private readonly PictureBox _preview = new() { Dock = DockStyle.Top, Height = 210, BackColor = Theme.PicBg, SizeMode = PictureBoxSizeMode.Zoom };
    private readonly Label _detailTitle = Theme.Caption(UiText.T("未选择资源"), true);
    private readonly Label _detailState = Theme.Caption("");
    private readonly Label _detailMeta = Theme.Caption("");
    private readonly Label _detailPath = Theme.Caption("");
    private readonly Label _detailHint = Theme.Caption("");
    private readonly Button _replaceTextureBtn = Theme.FlatButton(UiText.T("替换贴图"));
    private readonly Button _replaceAnimationBtn = Theme.FlatButton(UiText.T("替换为视频 / GIF"));
    private readonly Button _restoreResourceBtn = Theme.FlatButton(UiText.T("还原当前资源包"));
    private readonly Button _exportPngBtn = Theme.FlatButton(UiText.T("导出PNG"));
    private readonly Button _exportBundleZipBtn = Theme.FlatButton(UiText.T("导出当前Bundle ZIP"));
    private readonly Button _replaceBundleBtn = Theme.FlatButton(UiText.T("替换所在资源包"));
    private readonly Button _locateBtn = Theme.FlatButton(UiText.T("定位文件"));

    public MainForm(string initFolder = null)
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        DoubleBuffered = true;
        SuspendLayout();
        WindowLayout.Configure(this);
        Text = AppBuildInfo.WindowTitle;
        Width = 1400;
        Height = 860;
        MinimumSize = new Size(580, 440);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Bg;
        Font = Theme.UI(9f);
        ApplyAppIcon();

        _fallbackArtwork = LoadAssetImage("app_icon.png");
        _navigation = new StudioNavigation(LoadAssetImage("app_icon.png"), Navigate, SwitchLanguage);
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
        bottomBar.Controls.Add(_loadStrip);
        bottomBar.Height = Math.Max(34, _loadStrip.Font.Height + 14);
        bottomBar.Resize += (_, _) => _loadStrip.Width = Math.Min(bottomBar.ClientSize.Width, Math.Max(bottomBar.ClientSize.Width / 2, Sc(360)));

        Controls.Add(_centerPanel);
        Controls.Add(_rightPanel);
        Controls.Add(_leftPanel);
        Controls.Add(_paneBar);
        Controls.Add(_navigation);
        Controls.Add(bottomBar);
        BuildPaneBar();
        RefreshChromeLanguage();
        _flow = _pages[PageBrowse];

        _flow.MouseWheel += OnFlowWheel;
        _flow.Scroll += (_, _) => QueueLoadMoreCheck();
        _flow.Resize += (_, _) => QueueLoadMoreCheck();
        _searchDelay.Tick += (_, _) =>
        {
            _searchDelay.Stop();
            if (_activePage == PageBrowse) ShowCurrentAssets();
            else _renderedPages.Remove(PageBrowse);
        };

        if (!string.IsNullOrWhiteSpace(initFolder))
        {
            _source = (initFolder, true);
            _folder = initFolder;
            _backupDir = Path.Combine(initFolder, "_原始备份");
        }
        Navigate(PageDashboard);
        ResumeLayout(true);
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

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        StudioPaint.DarkTitle(this);
        StudioPaint.DarkScroll(_rightPanel);
        ApplyDpiScale(DeviceDpi);
        WindowLayout.FitToWorkingArea(this);
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        ApplyDpiScale(e.DeviceDpiNew);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        LoadIndexAsync(false);
    }

    private void BuildCenterPanel()
    {
        ConfigureResourceList();

        _pageTitle.Dock = DockStyle.None;
        _pageTitle.Text = UiText.T("仪表盘");
        _pageTitle.ForeColor = Theme.Text;
        _pageTitle.BackColor = Theme.Bar;
        _pageTitle.Font = Theme.UI(17f, true);
        _pageTitle.TextAlign = ContentAlignment.MiddleLeft;

        _kindBox.Width = 150;
        _kindBox.Height = 30;
        _kindBox.BackColor = Theme.Card;
        _kindBox.ForeColor = Theme.Text;
        _kindBox.SelectionChanged += (_, _) =>
        {
            if (!_changingLanguage)
            {
                _selectedCategoryButton = null;
                _selectedCategoryId = null;
                _selectedHeroGroupKey = null;
                _selectedHeroSkin = null;
                ResetBrowseState();
                if (_activePage == PageBrowse)
                {
                    BuildCategoryList(CurrentKind);
                    ShowCurrentAssets();
                }
            }
        };

        _searchBox.Width = 280;
        _searchBox.Height = 30;
        _searchBox.BackColor = Theme.PicBg;
        _searchBox.ForeColor = Theme.Text;
        _searchBox.TextChanged += (_, _) => { _searchDelay.Stop(); _searchDelay.Start(); };

        var refresh = Theme.FlatButton(UiText.T("刷新索引"), 120);
        _refreshIndexBtn = refresh;
        refresh.Height = 30;
        refresh.Margin = new Padding(8, 0, 0, 0);
        refresh.Click += (_, _) => LoadIndexAsync(true);

        _browseFilters.Controls.Add(_kindBox);
        _searchSurface = new LibrarySearch(_searchBox);
        _browseFilters.Controls.Add(_searchSurface);
        _browseFilters.Controls.Add(refresh);

        _browseFilters.Dock = DockStyle.None;
        _topBar.Controls.Add(_pageTitle);
        _topBar.Controls.Add(_browseFilters);
        _topBar.Resize += (_, _) => LayoutBrowseFilters(_topBar, refresh);
        LayoutBrowseFilters(_topBar, refresh);

        _centerPanel.Controls.Add(_resourceList);
        foreach (var page in new[] { PageDashboard, PageBrowse, PagePack, PageTools })
        {
            var grid = new GridFlow { BackColor = Theme.FlowBg, Padding = new Padding(12), Visible = false };
            _pages.Add(page, grid);
            _viewport.Controls.Add(grid);
        }
        _centerPanel.Controls.Add(_viewport);
        _centerPanel.Controls.Add(_topBar);
    }

    private void LayoutBrowseFilters(Control topBar, Button refresh)
    {
        if (_layingOutBrowseFilters || refresh == null) return;
        _layingOutBrowseFilters = true;
        try
        {
            int pad = Sc(12), gap = Sc(8);
            int inner = Math.Max(1, topBar.ClientSize.Width - pad * 2);
            int titleHeight = Math.Max(Sc(42), _pageTitle.Font.Height + Sc(8));
            int titleWidth = Math.Max(Sc(170), TextRenderer.MeasureText(_pageTitle.Text, _pageTitle.Font).Width + Sc(52));
            if (_activePage != PageBrowse)
            {
                _pageTitle.SetBounds(pad, Sc(8), inner, titleHeight);
                topBar.Height = titleHeight + Sc(20);
                return;
            }

            int kindWidth = Sc(138);
            int refreshWidth = Math.Max(Sc(120), TextRenderer.MeasureText(refresh.Text, refresh.Font).Width + refresh.Padding.Horizontal + gap);
            int searchMin = Sc(150);
            int rowHeight = Math.Max(Sc(32), Math.Max(_kindBox.PreferredHeight, _searchBox.PreferredHeight));
            bool inline = inner >= titleWidth + gap + kindWidth + searchMin + refreshWidth + gap * 2;
            int width = inline ? Math.Min(Sc(580), inner - titleWidth - gap) : inner;
            bool oneRow = width >= kindWidth + searchMin + refreshWidth + gap * 2;
            bool searchBesideKind = width >= kindWidth + searchMin + gap;
            int rows = oneRow ? 1 : searchBesideKind ? 2 : 3;
            int filtersHeight = rowHeight * rows + gap * (rows - 1);
            int y = inline ? Sc(8) + (titleHeight - rowHeight) / 2 : Sc(8) + titleHeight + Sc(4);
            _pageTitle.SetBounds(pad, Sc(8), inline ? inner - width - gap : inner, titleHeight);
            _browseFilters.SetBounds(topBar.ClientSize.Width - pad - width, y, width, filtersHeight);
            _kindBox.SetBounds(0, (rowHeight - _kindBox.PreferredHeight) / 2, Math.Min(kindWidth, width), _kindBox.PreferredHeight);
            int searchY = searchBesideKind ? 0 : rowHeight + gap;
            int searchX = searchBesideKind ? kindWidth + gap : 0;
            int searchWidth = width - searchX - (oneRow ? refreshWidth + gap : 0);
            _searchSurface.SetBounds(searchX, searchY, Math.Max(1, searchWidth), rowHeight);
            refresh.SetBounds(Math.Max(0, width - refreshWidth), oneRow ? 0 : (rows - 1) * (rowHeight + gap), Math.Min(refreshWidth, width), rowHeight);
            topBar.Height = Math.Max(Sc(8) + titleHeight, y + filtersHeight) + Sc(10);
        }
        finally { _layingOutBrowseFilters = false; }
    }

    private void ConfigureResourceList()
    {
        _resourceList.BackColor = Theme.FlowBg;
        _resourceList.ForeColor = Theme.Text;
        _resourceList.Font = Theme.UI(9.5f);
        _resourceList.Columns.Add(UiText.T("资源名"), 360);
        _resourceList.Columns.Add(UiText.T("分组"), 210);
        _resourceList.Columns.Add(UiText.T("尺寸/类型"), 100);
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
            if (IsSkillAnimationCandidate(_selectedAsset) || _selectedAsset?.IsSkillMovie == true) OpenDynamicReplacement(_selectedAsset);
            else if (_selectedAsset is { IsTexture: true }) PickAndReplaceTexture(_selectedAsset);
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
        var animationItem = menu.Items.Add("替换为视频 / GIF...", null, (_, _) => OpenDynamicReplacement(_selectedAsset));
        var restoreItem = menu.Items.Add("还原当前资源...", null, async (_, _) => await RestoreSelectedResourceAsync());
        BindMenuCaptions(menu);
        menu.Opening += (_, e) =>
        {
            bool has = _selectedAsset != null;
            bool tex = _selectedAsset is { IsTexture: true };
            menu.Items[0].Enabled = tex && !IsSkillAnimationCandidate(_selectedAsset);
            menu.Items[1].Enabled = tex;
            menu.Items[2].Enabled = has;
            menu.Items[4].Enabled = has;
            animationItem.Visible = IsDynamicReplacementCandidate(_selectedAsset);
            animationItem.Text = _selectedAsset?.IsSkillMovie == true ? UiText.T("替换技能特写...") : IsSkillAnimationCandidate(_selectedAsset) ? UiText.T("替换Q版动作...") : UiText.T("替换为视频 / GIF...");
            restoreItem.Enabled = CanRestoreSelection();
            e.Cancel = !has;
        };
        _resourceList.Disposed += (_, _) => menu.Dispose();
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
        foreach (var label in new[] { _detailTitle, _detailState, _detailMeta, _detailPath, _detailHint })
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
        foreach (var b in new[] { _replaceTextureBtn, _replaceAnimationBtn, _restoreResourceBtn, _exportPngBtn, _exportBundleZipBtn, _replaceBundleBtn, _locateBtn })
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
        _replaceAnimationBtn.Click += (_, _) => OpenDynamicReplacement(_selectedAsset);
        _restoreResourceBtn.Click += async (_, _) => await RestoreSelectedResourceAsync();
        _exportPngBtn.Click += (_, _) => { if (_selectedAsset != null) ExportSingle(_selectedAsset); };
        _exportBundleZipBtn.Click += (_, _) => { if (_selectedAsset != null) ExportBundleZip(new[] { _selectedAsset }, UiText.T("当前资源包")); };
        _replaceBundleBtn.Click += (_, _) => { if (_selectedAsset != null) ReplaceBundleFile(_selectedAsset); };
        _locateBtn.Click += (_, _) => { if (_selectedAsset != null) LocateBundle(_selectedAsset); };
        SetDetailButtons(false, false);
        ConfigurePreviewDragDrop();

        _preview.Margin = new Padding(0, 0, 0, 12);
        foreach (var control in new Control[] { _preview, _detailTitle, _detailState, buttons, _detailMeta, _detailPath, _detailHint })
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
            e.Effect = _selectedAsset is { IsVisual: true } && e.Data.GetDataPresent(DataFormats.FileDrop)
                ? DragDropEffects.Copy
                : DragDropEffects.None;
        };
        _preview.DragDrop += (_, e) =>
        {
            if (_selectedAsset is not { IsVisual: true }) return;
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files is { Length: > 0 })
                DoReplace(null, files[0], explicitAsset: _selectedAsset);
        };
        AttachDragExport(_preview, () => _selectedAsset);
    }

    private void ApplyDpiScale(int deviceDpi)
    {
        _layoutDpi = deviceDpi;
        LayoutMainColumns();
        foreach (var grid in _pages.Values) grid.Padding = new Padding(Sc(12));
        _navigation?.SetDpi(deviceDpi);
        _categoryList.Padding = new Padding(Sc(8));
        _sideTitle.Height = Sc(68);
        _preview.Height = Sc(210);
        _thumb = Math.Clamp(Sc(ThumbDef), Sc(MinThumb), Sc(MaxThumb));
        LayoutBrowseFilters(_topBar, _refreshIndexBtn);

        foreach (var grid in _pages.Values)
        foreach (Control c in grid.Controls)
        {
            if (c is WorkshopBanner) c.Height = Sc(145);
            else if (c is RoundedCard { Tag: TexRef } card) LayoutCard(card);
            else if ((c.Tag as string) == "header") c.Height = Sc(34);
            else if ((c.Tag as string) == "page_card") c.PerformLayout();
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        LayoutMainColumns();
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

    private void ShowPackPage()
    {
        ClearFlow();
        var current = _displayMods;
        if (_index == null)
        {
            _packLoading = AddLoadState();
            return;
        }

        AddPageCard(UiText.T("作品 / 导出"),
            SourceCaption + "\n" + UiText.F($"当前修改 / 待核验 {current.Count} 项，保留记录 {_ws.Entries.Count} 项。\n{_modVerificationInfo}")
                + (string.IsNullOrWhiteSpace(_ws.LoadNotice) ? "" : "\n" + _ws.LoadNotice),
            MakeButton(UiText.T("导入图包"), (_, _) => ImportPack()),
            MakeButton(UiText.T("导出图包"), (_, _) => ExportPack()),
            MakeButton(UiText.T("导出已改Bundle ZIP"), (_, _) => ExportModifiedBundlesZip()),
            MakeButton(UiText.T("导出安卓贴图"), (_, _) => OpenMobileExport()),
            MakeButton(UiText.T("重新核验修改"), (_, _) => VerifyModificationsAsync(reloadWorkspace: true)),
            MakeButton(UiText.T("打开资源目录"), (_, _) => OpenFolderInExplorer()));

        if (current.Count == 0 && _modVerification != null)
        {
            AddPageCard(UiText.T("正在核验可导出的修改"), _modVerificationInfo);
            return;
        }
        if (current.Count == 0)
        {
            AddPageCard(UiText.T("当前没有可导出的修改"), _ws.Entries.Count == 0
                ? UiText.T("尚无修改记录；有原始备份的资源会在后台比对并恢复记录。")
                : UiText.T("旧记录已保留。与备份一致或已不在当前索引中的资源不会列入本次导出。"), MakeButton(UiText.T("去资源浏览"), (_, _) => Navigate(PageBrowse)));
            return;
        }

        var rows = current
            .OrderBy(e => e.Label)
            .ThenBy(e => e.TextureName)
            .Take(24)
            .ToList();
        AddHeader(UiText.F($"当前修改 / 待核验 {rows.Count}/{current.Count}"));
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

        AddPageCard(UiText.T("路径"), SourceCaption,
            MakeButton(UiText.T("检测游戏"), (_, _) => DetectGame()),
            MakeButton(UiText.T("检测海外服"), (_, _) => DetectEdition(GameEdition.International)),
            MakeButton(UiText.T("检测国服"), (_, _) => DetectEdition(GameEdition.China)),
            MakeButton(UiText.T("打开游戏目录"), (_, _) => OpenGameDir()),
            MakeButton(UiText.T("打开任意资源文件夹"), (_, _) => OpenFolder()),
            MakeButton(UiText.T("通过 Steam 启动"), (_, _) => LaunchGame()));

        var indexInfo = AddPageCard(UiText.T("索引"),
            _index == null
                ? LoadingDetail()
                : UiText.F($"BuiltAt: {_index.BuiltAt}\n模式: {(_index.Lightweight ? UiText.T("轻量贴图索引") : UiText.T("完整索引"))}\nStamp: {ShortHash(_index.SourceStamp)}\n资源: {_index.AssetCount} / Bundle: {_index.BundleCount}"),
            MakeButton(UiText.T("刷新索引"), (_, _) => LoadIndexAsync(true)),
            MakeButton(UiText.T("完整扫描"), (_, _) => LoadIndexAsync(true, includeAdvancedTypes: true)),
            MakeButton(UiText.T("回到资源浏览"), (_, _) => Navigate(PageBrowse)));
        _toolsLoading = _index == null ? indexInfo : null;

        AddPageCard(UiText.T("维护"),
            _modVerificationInfo,
            MakeButton(UiText.T("重新核验修改"), (_, _) => VerifyModificationsAsync(reloadWorkspace: true)),
            MakeButton(UiText.T("导出已改Bundle ZIP"), (_, _) => ExportModifiedBundlesZip()),
            MakeButton(UiText.T("迁移旧Mod"), (_, _) => MigrateOldMods()),
            MakeButton(UiText.T("全部还原"), (_, _) => RestoreAll()));

        AddPageCard(UiText.T("安卓贴图"),
            UiText.T("当前版本 APK / 资源表、贴图 ZIP 与手机安装记录"),
            MakeButton(UiText.T("导出 / 安装安卓贴图"), (_, _) => OpenMobileExport()));

        AddPageCard(UiText.T("视频转换组件"), VideoRuntime.IsReady ? UiText.T("已就绪 · 视频 / GIF 转换可离线使用") : UiText.T("尚未启用 · FFmpeg / Python / CriCodecs"),
            MakeButton(VideoRuntime.IsReady ? UiText.T("检查 / 修复组件") : UiText.T("启用视频组件"), (_, _) =>
            {
                using var setup = new VideoRuntimeDialog();
                setup.ShowDialog(this);
                if (!IsDisposed && _activePage == PageTools) ShowToolsPage();
            }));

        _restoreDynamicAction = MakeButton(UiText.T("还原动态替换"), async (_, _) => await RestoreDynamicAsync());
        _dynamicStatus = AddPageCard(UiText.T("动态替换"), "", _restoreDynamicAction);
        RefreshDynamicStatus();
        AddPageCard(UiText.T("关于工坊"), AppBuildInfo.ReleaseLabel, MakeButton(UiText.T("开源项目"), (_, _) => NoticeDialog.Show(this)));
    }

    private Button _restoreDynamicAction;
    private Label _dynamicStatus;
    private void RefreshDynamicStatus()
    {
        var receipt = string.IsNullOrWhiteSpace(_folder) ? null : PortraitReplacement.ReadReceipt(AnimatedPortraitRoot());
        _restoreDynamicAction.Enabled = receipt != null && !_restoring;
        _dynamicStatus.Text = receipt == null ? UiText.T("当前没有动态立绘 / 卡牌替换记录") :
            receipt.Mode == IndependentVideoPatch.Mode ? UiText.F($"{receipt.Texture}\n{receipt.SourceName}\n独立视频：{receipt.VideoKey}") :
            UiText.F($"{receipt.Texture}\n{receipt.SourceName}\n借用视频：{receipt.VideoKey}");
    }

    private Button MakeButton(string text, EventHandler handler, int width = 142)
    {
        var b = Theme.FlatButton(text, Sc(width));
        b.MinimumSize = new Size(Sc(width), Sc(32));
        int textWidth = TextRenderer.MeasureText(text, b.Font, Size.Empty, TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine).Width;
        b.Width = Math.Max(Sc(width), textWidth + Sc(46));
        b.Height = Math.Max(Sc(32), b.Font.Height + Sc(12));
        b.Padding = new Padding(Sc(12), 0, Sc(12), 0);
        b.Margin = new Padding(0, 0, Sc(8), Sc(6));
        StyleSecondaryButton(b);
        b.Click += handler;
        return b;
    }

    private static void StylePrimaryButton(Button button)
    {
        button.BackColor = Theme.Accent;
        button.ForeColor = Theme.OnAccent;
        button.Font = Theme.UI(9.2f, true);
        if (button is RoundButton rb)
        {
            rb.BorderColor = Color.Transparent;
            rb.GradientColor = Color.Transparent;
            rb.HoverBackColor = Theme.AccentSoft;
            rb.DownBackColor = Theme.AccentSoft;
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

    private Label AddPageCard(string title, string body, params Button[] buttons)
        => AddPageSection(title, body, false, buttons);

    private Label AddPageSection(string title, string body, bool showProgress, params Button[] buttons)
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
        card.SetFill(Theme.FlowBg);
        card.Radius = Sc(8);
        card.BorderColor = Color.Transparent;

        var titleMark = new Panel { BackColor = Theme.Cyan };
        var titleLabel = Theme.Caption(title, true);
        titleLabel.Font = Theme.UI(11f, true);
        titleLabel.ForeColor = Theme.Text;

        var bodyLabel = Theme.Caption(body);
        bodyLabel.TextAlign = ContentAlignment.TopLeft;
        bodyLabel.Font = Theme.UI(9.2f);

        var actions = new BufferedFlowPanel
        {
            BackColor = card.Fill,
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
        var meter = showProgress ? new LoadStrip { Dock = DockStyle.None, TrackOnly = true, BackColor = card.Fill } : null;
        if (meter != null) { _loadStrip.CopyTo(meter); card.Controls.Add(meter); }
        _flow.Controls.Add(card);

        bool layingOut = false;
        void LayoutContents()
        {
            if (layingOut || card.IsDisposed) return;
            layingOut = true;
            try
            {
                foreach (var button in buttons) FitActionButton(button);
                int pad = Sc(16);
                int inner = Math.Max(1, card.ClientSize.Width - pad * 2);
                int titleHeight = Sc(30);
                titleMark.SetBounds(pad, Sc(18), Sc(3), Sc(16));
                titleLabel.SetBounds(pad + Sc(11), Sc(12), inner - Sc(11), titleHeight);

                var textSize = TextRenderer.MeasureText(bodyLabel.Text, bodyLabel.Font,
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
                if (meter != null)
                {
                    meter.SetBounds(pad, bodyLabel.Bottom + Sc(4), Math.Min(inner, Sc(470)), Sc(12));
                    neededHeight = meter.Bottom + Sc(16);
                }
                if (card.Height != neededHeight) card.Height = neededHeight;
            }
            finally
            {
                layingOut = false;
            }
        }

        card.Layout += (_, _) => LayoutContents();
        bodyLabel.TextChanged += (_, _) => LayoutContents();
        LayoutContents();
        return bodyLabel;
    }

    private void FitActionButton(Button button)
    {
        button.Width = Math.Max(button.MinimumSize.Width,
            TextRenderer.MeasureText(button.Text, button.Font).Width + button.Padding.Horizontal + Sc(12));
        button.Height = Math.Max(Sc(32), button.Font.Height + Sc(12));
    }

    private void AddEntryCard(ModEntry entry, TexRef asset, string bundlePath)
    {
        int width = Math.Max(Sc(360), _flow.ClientSize.Width - _flow.Padding.Horizontal - Sc(24));
        var card = new RoundedCard
        {
            Tag = "page_card",
            Width = width,
            Height = Sc(86),
            Margin = new Padding(Sc(6), Sc(4), Sc(6), Sc(4)),
            Padding = new Padding(Sc(12)),
            BackColor = Theme.FlowBg
        };
        card.SetFill(Theme.CardAlt);

        var actions = new BufferedFlowPanel
        {
            BackColor = card.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true
        };
        actions.Controls.Add(MakeButton(UiText.T("定位"), (_, _) =>
        {
            if (asset != null) LocateBundle(asset);
            else LocatePath(bundlePath);
        }, 82));
        actions.Controls.Add(MakeButton(UiText.T("导出Bundle ZIP"), (_, _) =>
        {
            var target = asset ?? new TexRef
            {
                BundlePath = bundlePath,
                BundleName = entry.Bundle,
                Name = entry.TextureName,
                Source = "作品集",
                PathId = entry.PathId,
                Kind = entry.Kind
            };
            ExportBundleZip(new[] { target }, entry.TextureName ?? entry.Bundle ?? "bundle");
        }, 140));

        var main = Theme.Caption($"{entry.TextureName}\n{UiText.T(ModificationCatalog.ShortLabel(asset?.Modification ?? ModificationState.Recorded))} · {UiText.T(entry.Label)} · {entry.Bundle}");
        main.TextAlign = ContentAlignment.MiddleLeft;
        main.AutoEllipsis = true;

        card.Controls.Add(main);
        card.Controls.Add(actions);
        _flow.Controls.Add(card);
        bool layingOut = false;
        card.Layout += (_, _) =>
        {
            if (layingOut) return;
            layingOut = true;
            try
            {
                foreach (Button action in actions.Controls) FitActionButton(action);
                int pad = Sc(12), gap = Sc(8);
                int inner = Math.Max(1, card.ClientSize.Width - pad * 2);
                int actionWidth = actions.Controls.Cast<Control>().Sum(c => c.Width + c.Margin.Horizontal);
                bool inline = inner >= Sc(240) + gap + actionWidth;
                main.SetBounds(pad, pad, inline ? inner - actionWidth - gap : inner, Sc(48));
                int available = inline ? actionWidth : inner;
                int rowHeight = actions.Controls.Cast<Control>().Max(c => c.Height + c.Margin.Vertical);
                int rows = 1, used = 0;
                foreach (Control action in actions.Controls)
                {
                    int item = action.Width + action.Margin.Horizontal;
                    if (used > 0 && used + item > available) { rows++; used = 0; }
                    used += item;
                }
                actions.SetBounds(inline ? main.Right + gap : pad, inline ? pad : main.Bottom + gap, available, rows * rowHeight);
                card.Height = Math.Max(main.Bottom, actions.Bottom) + pad;
            }
            finally { layingOut = false; }
        };
        card.PerformLayout();
    }

    private string CurrentKind => _kindBox.Kind;
    private string EditionCaption => UiText.T(_edition switch
    {
        GameEdition.International => "海外服",
        GameEdition.China => "国服",
        _ => "资源目录"
    });
    private string SourceCaption => _folder == null ? _loadStrip.Caption : EditionCaption + "   |   " + _folder;

    private void DetectGame() => OpenSource(null, true);
    private void DetectEdition(GameEdition edition)
    {
        _discoverEdition = edition;
        _source = (null, true);
        LoadIndexAsync(false);
    }
    private void OpenGameDir() => PickSource(true);
    private void OpenFolder() => PickSource(false);

    private void PickSource(bool game)
    {
        using var dlg = new FolderBrowserDialog { Description = UiText.T(game
            ? "选择游戏安装目录或资源目录" : "选择资源文件夹（递归扫描 .bundle / __data）"), UseDescriptionForTitle = true };
        if (Directory.Exists(_folder)) dlg.SelectedPath = _folder;
        if (dlg.ShowDialog(this) == DialogResult.OK) OpenSource(dlg.SelectedPath, game);
    }

    private void OpenSource(string path, bool game)
    {
        _discoverEdition = null;
        _source = (path, game);
        LoadIndexAsync(false);
    }

    private void OpenFolderInExplorer()
    {
        if (_folder == null || !Directory.Exists(_folder))
        {
            _status.Text = UiText.T("当前没有可打开的资源目录");
            return;
        }
        try { Process.Start("explorer.exe", _folder); }
        catch (Exception ex) { _status.Text = UiText.T("打开目录失败: ") + ex.Message; }
    }

    private void LaunchGame()
    {
        try
        {
            Process.Start(GameStartup.SteamStart());
            _status.Text = UiText.T("已向 Steam 发送启动请求");
        }
        catch (Exception ex)
        {
            MessageBox.Show(UiText.T("启动游戏失败：") + ex.Message, UiText.T("错误"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            _status.Text = UiText.T("启动游戏失败");
        }
    }

    private void LoadIndexAsync(bool rebuild, bool includeAdvancedTypes = false)
    {
        if (!_importingPack) _ = ReadSourceAsync(_source.Path, _source.Game, rebuild, includeAdvancedTypes);
    }

    private async Task ReadSourceAsync(string path, bool game, bool rebuild, bool advanced)
    {
        var edition = _discoverEdition;
        var request = (path, game, rebuild, advanced, edition);
        if (_loadingIndex && _activeRequest == request) return;
        _activeRequest = request;
        _indexLoad?.Cancel();
        using var job = new CancellationTokenSource();
        _indexLoad = job;
        var token = job.Token;
        bool Current() => !token.IsCancellationRequested && !IsDisposed && !Disposing && ReferenceEquals(job, _indexLoad);
        StopModificationVerification();
        _loadingIndex = true;
        _loadError = null;
        _status.Text = "";
        _overviewGeneration++;
        _loadSeq++;
        _previewSeq++;
        _folder = _backupDir = null;
        _edition = null;
        _index = _artIndex = null;
        ClearWorkshopArt();
        ClearDetails();
        _modifications = null;
        _ws = new();
        _displayMods = new();
        _counts = default;
        _verificationText = () => UiText.T("载入索引后将核验修改记录");
        _renderedPages.Clear();
        ResetBrowseState();
        SetLoadStage(path == null ? LoadStage.Detect : LoadStage.Resolve);
        ShowCurrentPage();
        try
        {
            var (folder, hot, selectedEdition) = await Task.Run(() =>
            {
                if (path == null)
                {
                    var install = GameLocator.Find(token, edition);
                    return (install?.AaDir, true, install?.Edition);
                }
                if (!Directory.Exists(path)) throw new DirectoryNotFoundException(path);
                string pc = GameLocator.ResolvePcResources(path);
                if (pc != null && (game || RuntimeBundleLocator.GameDataDirectory(pc) != null))
                    return (pc, true, GameLocator.EditionOf(pc));
                return (ResourceLocator.NormalizeGameDirectory(Path.TrimEndingDirectorySeparator(Path.GetFullPath(path))), false, null);
            }, token);
            if (!Current()) return;
            if (folder == null) throw new DirectoryNotFoundException(UiText.T("未检测到 Steam 游戏安装。"));
            if (path == null) _source = (folder, true);
            _folder = folder;
            _edition = selectedEdition;
            _backupDir = Path.Combine(folder, "_原始备份");
            _includeHotCache = hot;
            bool recursive = _recursiveFolder = !hot;
            SetLoadStage(LoadStage.Workspace);
            var records = await ReadWorkspaceAsync(folder, token);
            if (!Current()) return;
            _ws = records;
            SetLoadStage(LoadStage.Cache);
            var index = rebuild || advanced ? null :
                await Task.Run(() => _indexSvc.Load(folder, false, hot, recursive, token), token);
            if (!Current()) return;
            if (index == null)
            {
                SetLoadStage(LoadStage.Enumerate);
                IProgress<(int done, int total)> progress = new Progress<(int done, int total)>(p =>
                {
                    if (Current() && _loadStrip.Stage is LoadStage.Enumerate or LoadStage.Scan)
                        SetLoadStage(LoadStage.Scan, p.done, p.total);
                });
                index = await Task.Run(() =>
                {
                    var clock = Stopwatch.StartNew();
                    return _indexSvc.Build(folder, (done, total) =>
                    {
                        if (done == 0 || done == total || clock.ElapsedMilliseconds >= 80)
                        { progress.Report((done, total)); clock.Restart(); }
                    }, false, hot, recursive, advanced, token);
                }, token);
                if (!Current()) return;
                SetLoadStage(LoadStage.Save);
                try { await Task.Run(() => _indexSvc.Save(index, token), token); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { if (Current()) _status.Text = ex.Message; }
            }
            if (!Current()) return;
            SetLoadStage(LoadStage.Match);
            LoadWorkshopArt(index, folder);
            var workspace = _ws;
            var (catalog, entries, bundles) = await Task.Run(() =>
            {
                var catalog = new ModificationCatalog(index);
                token.ThrowIfCancellationRequested();
                return (catalog, catalog.CurrentEntries(workspace), index.BundleCount);
            }, token);
            if (!Current()) return;
            _index = index;
            _modifications = catalog;
            _displayMods = entries;
            _counts = (bundles, index.AssetCount, entries.Count);
            _loadingIndex = false;
            SetLoadStage(index.AssetCount == 0 ? LoadStage.Empty : LoadStage.Ready);
            _renderedPages.Clear();
            VerifyModificationsAsync();
            ShowCurrentPage();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            if (!Current()) return;
            _loadingIndex = false;
            _loadError = ex.Message;
            SetLoadStage(LoadStage.Failed);
            _renderedPages.Clear();
            ShowCurrentPage();
        }
        finally
        {
            if (ReferenceEquals(job, _indexLoad)) { _indexLoad = null; _loadingIndex = false; }
        }
    }

    private static Task<ModManifest> ReadWorkspaceAsync(string folder, CancellationToken token)
        => Task.Run(() => PackService.LoadWorkspace(folder), token);

    private void SetLoadStage(LoadStage stage, int done = 0, int total = 0)
    {
        _loadStrip.Edition = _edition;
        _loadStrip.Set(stage, done, total);
        if (_homeFolder is { IsDisposed: false }) _homeFolder.Text = SourceCaption;
        if (_activePage == PageDashboard && _index == null && _pages.TryGetValue(PageDashboard, out var home))
            foreach (var banner in home.Controls.OfType<WorkshopBanner>()) banner.Invalidate();
        foreach (var label in new[] { _browseLoading, _packLoading, _toolsLoading })
            if (label is { IsDisposed: false })
            {
                label.Text = LoadingDetail();
                if (label.Parent?.Controls.OfType<LoadStrip>().FirstOrDefault() is { } inline) _loadStrip.CopyTo(inline);
            }
    }

    private string LoadingDetail() => (_loadStrip.Stage == LoadStage.Failed ? _loadError : _loadStrip.Caption)
        + (_ws.Entries.Count > 0 && _loadingIndex ? "\n" + UiText.F($"已读入 {_ws.Entries.Count:N0} 条修改记录") : "")
        + (string.IsNullOrEmpty(_folder) ? "" : "\n" + _folder);

    private Label AddLoadState()
    {
        if (_loadingIndex) return AddPageSection(UiText.T("正在打开资源库"), LoadingDetail(), true);
        if (_loadStrip.Stage == LoadStage.Failed)
            return AddPageCard(UiText.T("资源库未能打开"), LoadingDetail(),
                MakeButton(UiText.T("重试"), (_, _) => LoadIndexAsync(false)),
                MakeButton(UiText.T("选择文件夹"), (_, _) => OpenGameDir()));
        return AddPageCard(UiText.T("尚未连接游戏目录"), UiText.T("选择游戏安装目录或资源目录"),
            MakeButton(UiText.T("检测游戏"), (_, _) => DetectGame()), MakeButton(UiText.T("选择文件夹"), (_, _) => OpenGameDir()));
    }

    private void StopModificationVerification()
    {
        _modVerificationSeq++;
        _modVerification?.Cancel();
        _modVerification = null;
    }

    private async void VerifyModificationsAsync(bool reloadWorkspace = false)
    {
        if (_modifications == null || _index == null) return;
        StopModificationVerification();
        var catalog = _modifications;
        var workspace = _ws;
        string folder = _folder;
        int seq = _modVerificationSeq;
        var cancellation = new CancellationTokenSource();
        _modVerification = cancellation;
        _verificationText = () => UiText.T("正在后台核验原始备份，资源可正常浏览")
            + (string.IsNullOrWhiteSpace(workspace.LoadNotice) ? "" : " · " + UiText.T(workspace.LoadNotice));
        if (_index.AssetCount > 0) SetLoadStage(LoadStage.Verify);
        if (_homeVerification is { IsDisposed: false }) _homeVerification.Text = _modVerificationInfo;
        var progress = new Progress<(int done, int total)>(value =>
        {
            if (seq == _modVerificationSeq && !IsDisposed && ReferenceEquals(_modVerification, cancellation) && _index?.AssetCount > 0)
                SetLoadStage(LoadStage.Verify, value.done, value.total);
        });
        try
        {
            if (reloadWorkspace)
            {
                var records = await ReadWorkspaceAsync(folder, cancellation.Token);
                if (IsDisposed || seq != _modVerificationSeq) return;
                _ws = workspace = records;
            }
            var result = await Task.Run(() => catalog.Scan((done, total) =>
            {
                if (done == 0 || done == total || done % 100 == 0) ((IProgress<(int, int)>)progress).Report((done, total));
            }, cancellation.Token));
            if (IsDisposed || cancellation.IsCancellationRequested || seq != _modVerificationSeq
                || !ReferenceEquals(catalog, _modifications) || !ReferenceEquals(workspace, _ws)) return;
            int repaired = PackService.MergeRecovered(workspace, result.Recovered);
            string saveError = null;
            if (repaired > 0)
            {
                try { PackService.SaveWorkspace(folder, workspace); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { saveError = ex.Message; }
            }
            _verificationText = () => UiText.F($"已核验 {result.VerifiedBundles + result.ReusedBundles} 个有备份的资源包，恢复 / 更新 {repaired} 条记录")
                + (result.FailedBundles > 0 ? UiText.F($"；{result.FailedBundles} 包未能核验，保留原记录") : "")
                + (saveError == null ? "" : UiText.T("；记录尚未保存：") + saveError)
                + (result.CacheNotice == null ? "" : "；" + result.CacheNotice)
                + (string.IsNullOrWhiteSpace(workspace.LoadNotice) ? "" : " · " + UiText.T(workspace.LoadNotice));
            _modVerification = null;
            RefreshModificationViews();
            _status.Text = _modVerificationInfo;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!IsDisposed && seq == _modVerificationSeq)
            {
                _verificationText = () => UiText.T("核验未完成，旧记录已保留：") + ex.Message;
                _status.Text = _modVerificationInfo;
            }
        }
        finally
        {
            if (ReferenceEquals(_modVerification, cancellation)) _modVerification = null;
            if (!IsDisposed && seq == _modVerificationSeq && _index != null)
                SetLoadStage(_index.AssetCount > 0 ? LoadStage.Ready : LoadStage.Empty);
            cancellation.Dispose();
        }
    }

    private List<ModEntry> CurrentModEntries() => _modifications?.CurrentEntries(_ws) ?? new();

    private void ApplyModificationState(TexRef asset)
    {
        bool recorded = PackService.Contains(_ws, asset.BundleName, asset.PathId, asset.Name);
        asset.Modification = _modifications?.GetState(asset.BundlePath, asset.Name, recorded)
            ?? (recorded ? ModificationState.Recorded : ModificationState.Unverified);
        asset.Modded = asset.Modification is ModificationState.Modified or ModificationState.Recorded;
    }

    private void UpdateCardModification(RoundedCard card)
    {
        if (card.IsDisposed || card.Tag is not TexRef asset) return;
        ApplyModificationState(asset);
        card.SetFill(asset.Modded ? Theme.CardMod : Theme.Card);
        card.GradientFill = Color.Transparent;
        card.Controls[1].BackColor = card.Controls[2].BackColor = card.Fill;
        card.Controls[1].Text = CardText(asset);
    }

    private void RefreshModificationViews()
    {
        UpdateDashboard();
        foreach (var card in _pages[PageBrowse].Controls.OfType<RoundedCard>()) UpdateCardModification(card);
        foreach (ListViewItem item in _resourceList.Items)
        {
            if (item.Tag is not TexRef asset) continue;
            ApplyModificationState(asset);
            item.ToolTipText = $"{asset.Name}\n{UiText.T(ModificationCatalog.Label(asset.Modification))}\n{asset.BundlePath}";
            item.BackColor = asset.Modded ? Theme.CardMod : Theme.FlowBg;
        }
        if (_selectedAsset != null) UpdateDetailText(_selectedAsset);
        _renderedPages.Remove(PageTools);
        if (_activePage == PageTools) ShowCurrentPage();
        if (_homeVerification is { IsDisposed: false }) _homeVerification.Text = _modVerificationInfo;
    }

    private void BuildCategoryList(string kind)
    {
        using var paint = new StudioPaint(_categoryList);
        int scroll = -_categoryList.AutoScrollPosition.Y;
        _categoryList.SuspendLayout();
        ClearSideList();

        Dictionary<string, int> counts;
        if (_index?.Lightweight == true && kind == ResourceKinds.Texture)
        {
            counts = _index.TextureCategoryCounts != null
                ? new Dictionary<string, int>(_index.TextureCategoryCounts, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            counts[ResourceCategories.AllId] = _index.CountKind(ResourceKinds.Texture);
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
                .Where(i => ResourceKinds.InBrowser(i.Kind, kind))
                .GroupBy(i => i.CategoryId ?? ResourceCategories.AllId)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
            counts[ResourceCategories.AllId] = (_index?.Items ?? new List<TexIndexEntry>()).Count(i => ResourceKinds.InBrowser(i.Kind, kind));
        }

        var defs = ResourceCategories.ForKind(kind).ToList();
        if (!defs.Any(d => d.Id == ResourceCategories.AllId))
            defs.Insert(0, new ResourceCategoryDef { Id = ResourceCategories.AllId, Label = UiText.T("全部") });

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

            AddCategoryButton($"{UiText.T(def.Label)}  [{count}]", def.Id, null, null, 0, count);
        }

        if (kind == ResourceKinds.Texture)
        {
            var advanced = new CheckBox { Text = UiText.T("动作帧 / 特效 / 其它资源"), Checked = _advancedCategories, AutoSize = false, ForeColor = Theme.SubText, Margin = new Padding(Sc(8), Sc(14), Sc(4), Sc(8)) };
            advanced.CheckedChanged += (_, _) => { _advancedCategories = advanced.Checked; BuildCategoryList(kind); };
            _categoryList.Controls.Add(advanced);
        }
        _categoryList.ResumeLayout(true);
        if (scroll > 0) _categoryList.AutoScrollPosition = new Point(0, scroll);
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
        var btn = AddCategoryButton($"{prefix} {UiText.T(def.Label)}  [{count}]", def.Id, null, null, 0, count, selectOnClick: false);
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
            bool groupExpanded = _expandedHeroGroups.Contains(group.GroupKey);
            var prefix = groupExpanded ? "▾" : "▸";
            var groupButton = AddCategoryButton($"{prefix} {UiText.Group(group.GroupKey, group.Display)}  [{group.Count}]",
                ResourceCategories.CharacterId, group.GroupKey, null, 1, group.Count, selectOnClick: false);
            groupButton.MouseClick += (_, e) =>
            {
                if (e.X < Sc(40))
                {
                    ToggleHeroGroup(group.GroupKey);
                    return;
                }
                SelectCategory(ResourceCategories.CharacterId, group.GroupKey, null);
            };

            if (!groupExpanded) continue;
            foreach (var skin in group.Skins.OrderBy(s => s.Order).ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase))
            {
                AddCategoryButton($"{UiText.Skin(skin.Name)}  [{skin.Count}]",
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
        btn.Padding = new Padding(Sc(12 + indent * 14), 0, Sc(6), 0);
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
        else btn.KeyDown += (_, e) =>
        {
            if (e.KeyCode is not (Keys.Enter or Keys.Space or Keys.Left or Keys.Right)) return;
            e.SuppressKeyPress = true;
            if (e.KeyCode is Keys.Enter or Keys.Space) SelectCategory(categoryId, heroGroupKey, skin);
            else
            {
                if (heroGroupKey == null) _characterTreeExpanded = e.KeyCode == Keys.Right;
                else if (e.KeyCode == Keys.Right) _expandedHeroGroups.Add(heroGroupKey);
                else _expandedHeroGroups.Remove(heroGroupKey);
                BuildCategoryList(CurrentKind);
                _categoryList.Controls.OfType<Button>().FirstOrDefault(b => b.Tag is CategoryButtonTag t
                    && t.CategoryId == categoryId && t.HeroGroupKey == heroGroupKey && t.Skin == skin)?.Focus();
            }
        };
        _categoryList.Controls.Add(btn);
        if (IsCategorySelected(categoryId, heroGroupKey, skin))
            _selectedCategoryButton = btn;
        return btn;
    }

    private void SelectCategory(string categoryId, string heroGroupKey, string skin)
    {
        bool expand = heroGroupKey != null && _expandedHeroGroups.Add(heroGroupKey);
        if (IsCategorySelected(categoryId, heroGroupKey, skin) && !expand && _browseLoadedCount > 0)
        { SelectPane(1); return; }
        _selectedCategoryId = categoryId;
        _selectedHeroGroupKey = heroGroupKey;
        _selectedHeroSkin = skin;
        if (!string.Equals(categoryId, ResourceCategories.CharacterId, StringComparison.OrdinalIgnoreCase))
        {
            _selectedHeroGroupKey = null;
            _selectedHeroSkin = null;
        }
        ResetBrowseState();
        if (expand) BuildCategoryList(CurrentKind);
        else foreach (var b in _categoryList.Controls.OfType<Button>())
        {
            if (b.Tag is not CategoryButtonTag t) continue;
            bool selected = IsCategorySelected(t.CategoryId, t.HeroGroupKey, t.Skin);
            if (selected || ReferenceEquals(b, _selectedCategoryButton))
                StyleCategoryButton(b, selected, t.Skin != null ? 2 : t.HeroGroupKey != null ? 1 : 0);
        }
        _selectedCategoryButton = _categoryList.Controls.OfType<Button>().FirstOrDefault(b => b.Tag is CategoryButtonTag t
            && IsCategorySelected(t.CategoryId, t.HeroGroupKey, t.Skin));
        SelectPane(1);
        ShowCurrentAssets();
    }

    private bool IsCategorySelected(string categoryId, string heroGroupKey, string skin)
        => string.Equals(_selectedCategoryId, categoryId, StringComparison.OrdinalIgnoreCase)
        && string.Equals(_selectedHeroGroupKey ?? "", heroGroupKey ?? "", StringComparison.OrdinalIgnoreCase)
        && string.Equals(_selectedHeroSkin ?? "", skin ?? "", StringComparison.OrdinalIgnoreCase);

    private static void StyleCategoryButton(Button btn, bool selected, int indent)
    {
        btn.BackColor = selected ? Theme.CardMod : Theme.Bg;
        btn.ForeColor = selected ? Theme.Accent : (indent == 0 ? Theme.Text : Theme.SubText);
        if (btn is RoundButton rb)
        {
            rb.BorderColor = Color.Transparent;
            rb.GradientColor = Color.Transparent;
            rb.IndicatorColor = selected ? Theme.Accent : Color.Transparent;
        }
        btn.Invalidate();
    }

    private void ToggleHeroGroup(string groupKey)
    {
        if (!_expandedHeroGroups.Remove(groupKey))
            _expandedHeroGroups.Add(groupKey);
        BuildCategoryList(CurrentKind);
    }

    private GameIndex _groupIndex;
    private List<CharacterGroupInfo> _groups;

    private List<CharacterGroupInfo> GetCharacterGroups()
    {
        if (_groups != null && ReferenceEquals(_groupIndex, _index)) return _groups;
        IEnumerable<AssetCategory> assets = Enumerable.Empty<AssetCategory>();
        if (_index?.Lightweight == true)
        {
            if (_index.TextureRefsByCategory != null
                && _index.TextureRefsByCategory.TryGetValue(ResourceCategories.CharacterId, out var refs))
                assets = refs.Select(r => NameParser.Parse(r.Name, r.OwnerHeroId, r.OwnerVariant,
                    r.IsSkillAnimation, r.OwnerIsMonster));
            assets = assets.Concat(_index.Items.Where(i => i.Kind == ResourceKinds.SkillMovie).Select(ParseAsset));
        }
        else if (_index != null)
        {
            assets = _index.Items
                .Where(i => ResourceKinds.InBrowser(i.Kind, ResourceKinds.Texture)
                    && string.Equals(i.CategoryId, ResourceCategories.CharacterId, StringComparison.OrdinalIgnoreCase))
                .Select(ParseAsset);
        }

        var map = new Dictionary<string, CharacterGroupInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var parsed in assets)
        {
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

        _groupIndex = _index;
        return _groups = map.Values
            .OrderBy(g => g.Sort)
            .ThenBy(g => g.Display, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void ShowCurrentAssets()
    {
        if (_activePage != PageBrowse) return;
        using var paint = new StudioPaint(_flow);
        _searchDelay.Stop();
        if (_index == null)
        {
            ClearFlow();
            ClearSideList();
            ClearDetails();
            _browseLoading = AddLoadState();
            return;
        }
        if (_index.AssetCount == 0)
        {
            ClearFlow();
            ClearDetails();
            AddPageCard(UiText.T("目录内没有可浏览资源"), _folder,
                MakeButton(UiText.T("选择文件夹"), (_, _) => OpenFolder()),
                MakeButton(UiText.T("完整扫描"), (_, _) => LoadIndexAsync(true, true)));
            return;
        }

        var kind = CurrentKind;
        var cat = _selectedCategoryId ?? ResourceCategories.AllId;
        var query = (_searchBox.Text ?? "").Trim();
        var key = CurrentBrowseKey(kind, cat, query);

        if (kind != ResourceKinds.Texture && !_index.IncludesAdvancedTypes && _index.CountKind(kind) == 0)
        {
            ClearFlow();
            ClearDetails();
            AddPageCard(UiText.T("需要完整扫描"), UiText.T("此类型尚未建立索引。完整扫描包含音频、文本、模型和动画。"), MakeButton(UiText.T("完整扫描"), (_, _) => LoadIndexAsync(true, includeAdvancedTypes: true)));
            _status.Text = UiText.T("需要完整扫描");
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
            .Where(i => ResourceKinds.InBrowser(i.Kind, kind))
            .Where(i => categoryId == ResourceCategories.AllId || string.Equals(i.CategoryId, categoryId, StringComparison.OrdinalIgnoreCase))
            .Where(i => MatchesQuery(i.Name, i.Bundle, query))
            .Where(MatchesHeroFilter);

        var rows = SortAssetRows(filtered, kind, categoryId);
        _sortedIndex = _index;
        _sortedKey = key;
        _sortedRows = rows;
        total = rows.Count;
        return rows.Skip(offset).Take(cap).ToList();
    }

    private List<TexIndexEntry> GetLightweightTextureRows(string categoryId, string query, int offset, int cap, out int total)
    {
        if (_index.Items.Any(i => i.Kind == ResourceKinds.SkillMovie))
        {
            string key = CurrentBrowseKey(ResourceKinds.Texture, categoryId, query);
            if (ReferenceEquals(_sortedIndex, _index) && _sortedKey == key && _sortedRows != null)
            {
                total = _sortedRows.Count;
                return _sortedRows.Skip(offset).Take(cap).ToList();
            }
            var rows = SortAssetRows(_index.EnumerateLightweightTextures().Concat(_index.Items.Where(i => i.Kind == ResourceKinds.SkillMovie))
                .Where(i => categoryId == ResourceCategories.AllId || i.CategoryId == categoryId)
                .Where(i => MatchesQuery(i.Name, i.Bundle, query)).Where(MatchesHeroFilter), ResourceKinds.Texture, categoryId);
            _sortedIndex = _index;
            _sortedKey = key;
            _sortedRows = rows;
            total = rows.Count;
            return rows.Skip(offset).Take(cap).ToList();
        }
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
                var entry = MakeLightweightEntry(r.Bundle, r.Name, categoryId);
                if (!MatchesHeroFilter(entry))
                    continue;
                if (keepAllForGrouping || total >= offset && picked.Count < cap)
                    picked.Add(entry);
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

                string catId = null;
                if (needsCategory)
                {
                    catId = ResourceCategories.Categorize(ResourceKinds.Texture, name);
                }
                var entry = MakeLightweightEntry(bundle, name, catId);
                if (needsCategory && !string.Equals(entry.CategoryId, categoryId, StringComparison.OrdinalIgnoreCase)) continue;
                if (!MatchesHeroFilter(entry)) continue;

                if (keepAllForGrouping || total >= offset && picked.Count < cap)
                    picked.Add(entry);
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
        var entry = new TexIndexEntry
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
        _index.ApplyCharacterOwnership(entry);
        return entry;
    }

    private static bool MatchesQuery(string name, string bundle, string query)
        => string.IsNullOrWhiteSpace(query)
        || (name ?? "").Contains(query, StringComparison.OrdinalIgnoreCase)
        || (bundle ?? "").Contains(query, StringComparison.OrdinalIgnoreCase);

    private static AssetCategory ParseAsset(TexIndexEntry entry)
        => NameParser.Parse(entry?.Name, entry?.OwnerHeroId, entry?.OwnerVariant,
            entry?.IsSkillAnimation == true, entry?.OwnerIsMonster == true);

    private static AssetCategory ParseAsset(TexRef asset)
        => NameParser.Parse(asset?.Name, asset?.OwnerHeroId, asset?.OwnerVariant,
            asset?.IsSkillAnimation == true, asset?.OwnerIsMonster == true);

    private bool MatchesHeroFilter(TexIndexEntry entry)
    {
        if (!HasHeroFilter()) return true;
        var parsed = ParseAsset(entry);
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
        var asset = new TexRef
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
            IsSkillAnimation = e.IsSkillAnimation,
            OwnerHeroId = e.OwnerHeroId,
            OwnerVariant = e.OwnerVariant,
            OwnerIsMonster = e.OwnerIsMonster,
            Display = DisplayName(e)
        };
        ApplyModificationState(asset);
        return asset;
    }

    private string DisplayName(TexIndexEntry e)
    {
        if (!ResourceKinds.InBrowser(e.Kind, ResourceKinds.Texture)) return e.Name;
        var parsed = ParseAsset(e);
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
                .ThenBy(i => IndexService.SourcePriority(i.Source))
                .ToList();
        }

        return rows
            .OrderBy(i => i.CategoryLabel, StringComparer.OrdinalIgnoreCase)
            .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(i => IndexService.SourcePriority(i.Source))
            .ToList();
    }

    private List<(string title, List<TexRef> assets)> BuildAssetSections(string kind, string categoryId, List<TexRef> shown)
    {
        if (kind == ResourceKinds.Texture && IsHeroSkinCategory(categoryId))
            return shown.GroupBy(HeroSkinTitle).Select(g => (g.Key, g.ToList())).ToList();

        return categoryId == ResourceCategories.AllId
            ? shown.GroupBy(t => t.CategoryLabel ?? UiText.T("其它")).Select(g => (g.Key, g.ToList())).ToList()
            : new List<(string, List<TexRef>)> { (ResourceCategories.Label(kind, categoryId), shown) };
    }

    private static bool IsHeroSkinCategory(string categoryId)
        => string.Equals(categoryId, ResourceCategories.CharacterId, StringComparison.OrdinalIgnoreCase)
        || string.Equals(categoryId, "hero_card", StringComparison.OrdinalIgnoreCase)
        || string.Equals(categoryId, "hero_bust", StringComparison.OrdinalIgnoreCase)
        || string.Equals(categoryId, "hero_photo", StringComparison.OrdinalIgnoreCase);

    private string HeroSkinTitle(TexRef asset)
    {
        var parsed = ParseAsset(asset);
        if (!parsed.IsHero) return UiText.T(asset.CategoryLabel ?? "其它");
        return $"{UiText.Group(parsed.GroupKey, _naming.Display(parsed.GroupKey))} / {UiText.Skin(parsed.Skin)}";
    }

    private static int HeroGroupOrder(TexIndexEntry e)
    {
        var parsed = ParseAsset(e);
        if (!parsed.IsHero) return int.MaxValue;
        if (parsed.IsMonster) return 900000;
        return int.TryParse(parsed.HeroId, out var n) ? n : int.MaxValue - 1;
    }

    private static int HeroSkinOrder(TexIndexEntry e)
    {
        var parsed = ParseAsset(e);
        return parsed.IsHero ? parsed.SkinOrder : 999;
    }

    private static int HeroKindOrder(TexIndexEntry e)
    {
        var parsed = ParseAsset(e);
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
            AddPageCard(UiText.T("没有匹配的资源"), UiText.T("换个分类、角色、皮肤或搜索词。"), MakeButton(UiText.T("清空搜索"), (_, _) =>
            {
                _searchBox.Text = "";
                SelectCategory(ResourceCategories.AllId, null, null);
            }));
            _status.Text = UiText.T("没有匹配的资源");
            return;
        }

        _status.Text = UiText.F($"加载资源元数据... {Math.Min(total, offset + visibleCount)}/{total}");
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

        if (seq == _loadSeq && _activePage == PageBrowse)
            _status.Text = UiText.F($"已显示 {_browseLoadedCount}/{_browseTotalCount} 项，继续向下滚动自动加载");

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
        var label = Theme.Caption(LoadMoreCaption());
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
        if (_activePage != PageBrowse || !_flow.Visible || _index == null || _appendingRows) return;
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
                _status.Text = UiText.F($"已显示 {_browseLoadedCount}/{_browseTotalCount} 项");
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
                    ToolTipText = $"{asset.Name}\n{UiText.T(ModificationCatalog.Label(asset.Modification))}\n{asset.BundleName}\n{asset.BundlePath}"
                };
                item.SubItems.Add(ResourceListGroupLabel(asset));
                item.SubItems.Add(asset.IsTexture
                    ? (asset.Width > 0 && asset.Height > 0 ? $"{asset.Width}x{asset.Height}" : UiText.T("点选解析"))
                    : UiText.T(ResourceKinds.Label(asset.Kind)));
                item.SubItems.Add(asset.BundleName ?? "");
                if (asset.Modded)
                {
                    item.BackColor = Theme.CardMod;
                    item.ForeColor = Theme.Text;
                }
                _resourceList.Items.Add(item);
            }
        }

        if (total == 0)
        {
            var group = new ListViewGroup(UiText.T("没有匹配"));
            _resourceList.Groups.Add(group);
            var item = new ListViewItem(UiText.T("没有匹配的资源"), group);
            item.SubItems.Add(UiText.T("换个分类或搜索词"));
            item.SubItems.Add("-");
            item.SubItems.Add("-");
            _resourceList.Items.Add(item);
        }

        _resourceList.EndUpdate();
        LayoutResourceListColumns();
        var suffix = total > cap ? UiText.F($"，只列前 {cap} 项，搜索可缩小范围") : "";
        _status.Text = UiText.F($"资源列表 {Math.Min(total, cap)}/{total} 项{suffix}");
    }

    private string ResourceListGroupLabel(TexRef asset)
    {
        if (!asset.IsVisual) return asset.CategoryLabel ?? UiText.T(ResourceKinds.Label(asset.Kind));
        var parsed = ParseAsset(asset);
        if (parsed.IsHero)
            return $"{UiText.Group(parsed.GroupKey, _naming.Display(parsed.GroupKey))} / {UiText.Skin(parsed.Skin)}";
        if (parsed.IsHandCard)
            return UiText.T("手牌");
        return asset.CategoryLabel ?? UiText.T("其它");
    }

    private async Task LoadThumbnailsAsync(int seq, List<RoundedCard> cards)
    {
        var candidates = cards
            .Where(c => c.Tag is TexRef a && a.IsVisual)
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
                if (seq != _loadSeq) return;
                previews = await Task.Run(async () =>
                {
                    var assets = group.cards.Select(c => (TexRef)c.Tag).ToList();
                    if (assets[0].IsSkillMovie)
                    {
                        var movies = new Dictionary<long, byte[]>();
                        foreach (var asset in assets)
                        {
                            if (seq != _loadSeq) break;
                            asset.PathId = SkillMovieEngine.Read(asset.BundlePath, asset.Name).PathId;
                            movies[asset.PathId] = await SkillMovieEngine.PreviewAsync(asset.BundlePath, asset.Name, false, CancellationToken.None);
                        }
                        return movies;
                    }
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
                UpdateCardModification(card);
                if (!previews.TryGetValue(asset.PathId, out var png)) continue;
                SetCardThumbnail(card, png);
                done++;
                if (_activePage == PageBrowse && done % 8 == 0) _status.Text = UiText.F($"缩略图加载中... {done}/{candidates.Count}");
            }
        }).ToArray();

        await Task.WhenAll(tasks);
        if (seq == _loadSeq)
            _status.Text = UiText.F($"已显示 {_browseLoadedCount}/{_browseTotalCount} 项，本批缩略图 {done}/{candidates.Count}");
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
            Text = "  " + UiText.T(title),
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
        var visual = new BufferedPanel { BackColor = Theme.PicBg };
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

        var card = new RoundedCard { Margin = new Padding(6), Tag = asset, AllowDrop = asset.IsVisual };
        var actions = new BufferedPanel
        {
            BackColor = Color.Transparent,
            Cursor = asset.IsVisual ? Cursors.Hand : Cursors.Default
        };
        actions.Paint += (_, e) => PaintCardActions(e.Graphics, actions.ClientRectangle, asset.IsVisual);
        actions.MouseClick += (_, e) =>
        {
            if (!asset.IsVisual) return;
            if (e.X < actions.Width / 2) PickAndReplaceTexture(asset, card);
            else if (asset.IsSkillMovie) ExportBundleZip(new[] { asset }, asset.Name);
            else ExportSingle(asset);
        };

        card.SetFill(asset.Modded ? Theme.CardMod : Theme.Card);
        lbl.BackColor = actions.BackColor = card.Fill;
        card.Radius = Sc(8);
        card.GradientFill = Color.Transparent;
        card.Controls.Add(visual);
        card.Controls.Add(lbl);
        card.Controls.Add(actions);
        LayoutCard(card);

        void Select(object s, EventArgs e)
        {
            SelectAsset(asset, png);
            if (_paneBar.Visible) SelectPane(2);
        }
        card.Click += Select;
        visual.Click += Select;
        lbl.Click += Select;
        foreach (Control child in visual.Controls) child.Click += Select;
        AttachDragExport(card, () => asset);
        AttachDragExport(visual, () => asset);
        AttachDragExport(pic, () => asset);

        if (asset.IsVisual)
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
        if (asset.IsSkillMovie)
            menu.Items.Add("替换技能特写...", null, (_, _) => OpenSkillMovie(asset));
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
            menu.Items.Add("替换为视频 / GIF...", null, (_, _) => OpenAnimatedPortrait(asset));
        BindMenuCaptions(menu);
        card.Disposed += (_, _) => menu.Dispose();
        card.ContextMenuStrip = menu;
        visual.ContextMenuStrip = menu;
        lbl.ContextMenuStrip = menu;
        actions.ContextMenuStrip = menu;
        foreach (Control child in visual.Controls) child.ContextMenuStrip = menu;

        card.DoubleClick += (_, _) => { if (asset.IsVisual) PickAndReplaceTexture(asset, card); else LocateBundle(asset); };
        visual.DoubleClick += (_, _) => { if (asset.IsVisual) PickAndReplaceTexture(asset, card); else LocateBundle(asset); };
        lbl.DoubleClick += (_, _) => { if (asset.IsVisual) PickAndReplaceTexture(asset, card); else LocateBundle(asset); };

        return card;
    }

    private static string KindGlyph(string kind) => kind switch
    {
        ResourceKinds.Audio => "AUD",
        ResourceKinds.Text => "TXT",
        ResourceKinds.Mesh => "MESH",
        ResourceKinds.Animation => "ANIM",
        ResourceKinds.SkillMovie => "VIDEO",
        _ => "RES"
    };

    private static string CardText(TexRef asset)
    {
        var size = asset.IsVisual
            ? asset.Width > 0 && asset.Height > 0 ? $"{asset.Width}x{asset.Height}" : UiText.T("点选预览")
            : UiText.T(ResourceKinds.Label(asset.Kind));
        var parsed = ParseAsset(asset);
        var caption = UiText.English && parsed.IsHero && IsHeroSkinCategory(asset.CategoryId)
            ? UiText.AssetCaption(parsed) : asset.Display ?? asset.Name;
        return $"{caption}\n{UiText.T(ModificationCatalog.ShortLabel(asset.Modification))} · {size}\n{UiText.T(IndexService.SourceLabel(asset.Source))}";
    }

    private void PaintCardActions(Graphics g, Rectangle bounds, bool enabled)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var left = new Rectangle(0, 0, Math.Max(1, bounds.Width / 2 - 3), bounds.Height - 1);
        var right = new Rectangle(bounds.Width / 2 + 3, 0, Math.Max(1, bounds.Width - bounds.Width / 2 - 3), bounds.Height - 1);
        using var line = new Pen(Theme.Line);
        using var font = Theme.UI(8f, true);
        g.DrawLine(line, 0, 0, bounds.Width, 0);
        g.DrawLine(line, bounds.Width / 2, 6, bounds.Width / 2, Math.Max(6, bounds.Height - 6));
        DrawCentered(g, UiText.T("替换"), font, enabled ? Theme.Accent : Theme.Muted, left);
        DrawCentered(g, UiText.T("导出"), font, enabled ? Theme.Cyan : Theme.Muted, right);
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
        int pad = Sc(Pad), gap = Sc(5), lblH = Math.Max(Sc(46), lbl.Font.Height * 3 + Sc(6)), actionH = Sc(28), bottom = Sc(7);
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
        _moviePreview?.Cancel();
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
            _status.Text = UiText.T("解析选中贴图 PathId / 尺寸...");
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
                        _detailHint.Text = UiText.T("未能在 bundle 里解析到同名 Texture2D；可导出/定位整个 bundle。");
                        SetDetailButtons(true, false);
                        _status.Text = UiText.T("解析失败：") + asset.Name;
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
        ApplyModificationState(asset);
        _detailTitle.Text = asset.Name;
        if (asset.IsSkillMovie && asset.MovieTiming != null)
            _detailTitle.Text += UiText.F($"\n原技能 {asset.MovieTiming.Duration:0.###} 秒 · {asset.MovieTiming.TotalFrames} 帧");
        _detailState.Text = UiText.T(ModificationCatalog.Label(asset.Modification));
        _detailState.ForeColor = asset.Modification switch
        {
            ModificationState.Modified => Theme.AccentSoft,
            ModificationState.Original => Theme.Good,
            ModificationState.Recorded => Theme.Warn,
            _ => Theme.SubText
        };
        var path = ShortDisplayPath(asset.BundlePath);
        _detailMeta.Text =
            UiText.F($"类型: {UiText.T(ResourceKinds.Label(asset.Kind))}\n") +
            UiText.F($"分类: {UiText.T(asset.CategoryLabel)}\n") +
            UiText.F($"尺寸: {(asset.IsVisual ? (asset.Width > 0 ? $"{asset.Width}x{asset.Height}" : UiText.T("点选后解析")) : "-")}\n") +
            UiText.F($"格式: {UiText.T(asset.Format) ?? "-"}\n") +
            UiText.F($"来源: {UiText.Source(asset.Source)}");
        _detailPath.Text = UiText.F($"Bundle: {asset.BundleName}\nPathId: {asset.PathId}\n位置: {path}");
        if (IndexService.SourcePriority(asset.Source) == 0) _detailPath.Text += "\n" + asset.BundlePath;
        _detailHint.Text = asset.IsSkillMovie ? UiText.T("原生技能特写 · 当前资源包 · 游戏 DLL 不变")
            : asset.IsSkillAnimation
            ? UiText.T("Q版动作 · 原生帧数与 Sprite 网格 · 只修改当前资源包")
            : asset.IsTexture
            ? UiText.T("贴图支持资产级替换、裁切、导出 PNG。")
            : UiText.T("非贴图当前支持索引、定位、替换整个资源包；资产级音频/动画写回仍需单独编码器。");
        if (asset.Source == "基础包" && _index?.Items.Any(item => item.Name == asset.Name && item.Kind == asset.Kind
            && IndexService.SourcePriority(item.Source) < 2) == true)
            _detailHint.Text = UiText.T("同名资源还存在缓存版本，修改此基础包不一定改变游戏显示。请核对安装缓存或用户缓存中的同名项。\n") + _detailHint.Text;
        if (ReferenceEquals(asset, _selectedAsset)) UpdateRestoreButton(true);
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
        return true;
    }

    private bool EnsureTextureReady(TexRef asset)
    {
        if (asset == null || !asset.IsTexture) return false;
        if (asset.PathId != 0) return true;
        _status.Text = UiText.T("正在解析选中贴图...");
        try
        {
            if (!HydrateTexture(asset))
            {
                MessageBox.Show(UiText.T("无法在 bundle 中解析这张贴图，不能做资产级替换/导出。"), UiText.T("提示"));
                return false;
            }
            UpdateDetailText(asset);
            UpdateSelectedListRow(asset);
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(UiText.T("解析贴图失败：") + ex.Message, UiText.T("错误"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    private void LoadPreviewAsync(TexRef asset, byte[] thumbnail)
    {
        if (asset.IsSkillMovie) { LoadSkillMoviePreview(asset); return; }
        if (!asset.IsTexture || asset.PathId == 0) return;
        int seq = ++_previewSeq;
        string root = AnimatedPortraitRoot();
        string gameDirectory = _folder;
        Task.Run(() =>
        {
            string state = null;
            try
            {
                if (asset.IsSkillAnimation)
                {
                    var info = SkillAnimationEngine.Inspect(asset.BundlePath, asset.PathId, asset.Name);
                    return (Bytes: SkillAnimationEngine.DecodeFramePreview(asset.BundlePath, asset.PathId, 0),
                        State: UiText.F($"Q版动作 · {info.FrameNames.Count} 帧 · 预览第 1 帧"), Animated: false);
                }
                var receipt = PortraitReplacement.ReadReceipt(root, asset.Name);
                if (receipt?.Texture == asset.Name)
                {
                    var binding = PortraitReplacement.CheckCurrentBinding(root, receipt, gameDirectory);
                    if (binding.Current)
                    {
                        state = binding.Message + UiText.T("\n本地预览（前 6 秒）：") + receipt.SourceName;
                        if (File.Exists(receipt.Preview)) return (Bytes: File.ReadAllBytes(receipt.Preview), State: state, Animated: true);
                        state += UiText.T(" · 预览文件缺失");
                    }
                    else state = binding.Message;
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
                _status.Text = t.Result.State ?? UiText.F($"已预览：{asset.Name}");
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
        bool skill = IsSkillAnimationCandidate(_selectedAsset);
        _replaceTextureBtn.Enabled = hasAsset && isTexture && !skill && !_restoring;
        _replaceAnimationBtn.Enabled = hasAsset && IsDynamicReplacementCandidate(_selectedAsset) && !_restoring;
        _replaceAnimationBtn.Text = _selectedAsset?.IsSkillMovie == true ? UiText.T("替换技能特写") : skill ? UiText.T("替换Q版动作") : UiText.T("替换为视频 / GIF");
        UpdateRestoreButton(hasAsset);
        _exportPngBtn.Enabled = hasAsset && isTexture;
        _exportBundleZipBtn.Enabled = hasAsset;
        _replaceBundleBtn.Enabled = hasAsset && !_restoring;
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
        _status.Text = UiText.F($"缩略图大小: {_thumb}px");
    }

    private void PickAndReplaceTexture(TexRef asset, RoundedCard card = null, bool forceCrop = false)
    {
        if (asset.IsSkillMovie || IsSkillAnimationCandidate(asset)) { OpenDynamicReplacement(asset); return; }
        if (!EnsureTextureReady(asset)) return;
        using var dlg = new OpenFileDialog { Filter = UiText.T("图片|*.png;*.jpg;*.jpeg;*.bmp;*.webp") };
        if (dlg.ShowDialog() != DialogResult.OK) return;
        DoReplace(card, dlg.FileName, forceCrop, asset);
    }

    private void DoReplace(RoundedCard card, string imagePath, bool forceCrop = false, TexRef explicitAsset = null)
    {
        var asset = explicitAsset ?? (TexRef)card.Tag;
        if (AnimatedPortraitDialog.IsVideo(imagePath))
        {
            if (!IsDynamicReplacementCandidate(asset)) { _status.Text = UiText.T("视频替换支持角色立绘、手牌、事件卡和技能特写。"); return; }
            OpenDynamicReplacement(asset, imagePath);
            return;
        }
        if (IsSkillAnimationCandidate(asset) || asset.IsSkillMovie)
        {
            _status.Text = UiText.T("动画替换请拖入视频或 GIF。");
            return;
        }
        if (!asset.IsTexture)
        {
            _status.Text = UiText.T("该资源不是 Texture2D，不能做贴图级替换。");
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
                if (crop.ShowDialog(this) != DialogResult.OK) { _status.Text = UiText.T("已取消"); return; }
                cropped = crop.ResultPng;
            }

            StopModificationVerification();
            _modifications?.Invalidate(asset.BundlePath);
            PortraitReplacement.EnsureGameClosed();
            _status.Text = UiText.F($"替换 {asset.Name} ...");
            if (cropped != null)
                _engine.ReplaceInPlaceFromBytes(asset.BundlePath, asset.PathId, cropped, _backupDir, asset.BundleName, asset.Name);
            else
                _engine.ReplaceInPlace(asset.BundlePath, asset.PathId, imagePath, _backupDir, asset.BundleName, asset.Name);

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
            RefreshModificationViews();
            VerifyModificationsAsync();
            _status.Text = UiText.F($"已替换 {asset.Name}，原始资源已备份。");
        }
        catch (Exception ex)
        {
            MessageBox.Show(UiText.F($"替换失败：{ex.Message}"), UiText.T("错误"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            _status.Text = UiText.T("替换失败");
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
            _status.Text = UiText.T("拖到文件夹即可导出：") + Path.GetFileName(outPath);
            source.DoDragDrop(data, DragDropEffects.Copy);
        }
        catch (Exception ex)
        {
            MessageBox.Show(UiText.T("拖动导出失败: ") + ex.Message, UiText.T("错误"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ReplaceBundleFile(TexRef asset)
    {
        using var dlg = new OpenFileDialog { Filter = UiText.T("Unity资源包|*.bundle;__data|所有文件|*.*") };
        if (dlg.ShowDialog() != DialogResult.OK) return;

        if (MessageBox.Show(UiText.F($"将用所选文件覆盖当前资源包：\n{asset.BundlePath}\n\n会先备份原文件。继续？"),
            UiText.T("替换所在资源包"), MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;

        try
        {
            PortraitReplacement.EnsureGameClosed();
            Directory.CreateDirectory(_backupDir);
            var backup = Path.Combine(_backupDir, ModEngine.SafeBackupName(asset.BundleName));
            if (!File.Exists(backup)) File.Copy(asset.BundlePath, backup);
            File.Copy(dlg.FileName, asset.BundlePath, overwrite: true);
            _status.Text = UiText.T("已替换所在资源包：") + asset.BundleName;
            LoadIndexAsync(true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(UiText.F($"资源包替换失败：{ex.Message}"), UiText.T("错误"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ExportModifiedBundlesZip()
    {
        if (_folder == null)
        {
            _status.Text = UiText.T("请先打开目录");
            return;
        }
        if (_ws == null || _ws.Entries.Count == 0)
        {
            MessageBox.Show(UiText.T("当前作品集没有已修改记录。"), UiText.T("提示"));
            return;
        }

        var current = CurrentModEntries();
        if (current.Count == 0)
        {
            MessageBox.Show(this, UiText.T("当前没有可关联到现有资源的修改。旧记录已保留，请等待后台核验或重新核验修改。"), UiText.T("没有可导出的修改"));
            return;
        }
        using var selection = new ExportSelectionDialog(current, true);
        if (selection.ShowDialog(this) != DialogResult.OK) return;
        var assets = selection.SelectedEntries
            .Select(e =>
            {
                var indexed = FindAssetForEntry(e);
                if (indexed != null) return indexed;
                var fallback = Path.Combine(_folder, e.Bundle ?? "");
                return File.Exists(fallback)
                    ? new TexRef { BundlePath = fallback, BundleName = e.Bundle, Name = e.TextureName, Source = "作品集", PathId = e.PathId, Kind = e.Kind }
                    : null;
            })
            .Where(a => a != null)
            .ToList();

        ExportBundleZip(assets, UiText.T("已改Bundle"));
    }

    private static bool IsAnimatedPortraitCandidate(TexRef asset) =>
        asset is { IsTexture: true } && AnimatedPortraitPatch.IsSupportedTarget(asset.Name);

    private static bool IsSkillAnimationCandidate(TexRef asset) => SkillAnimationEngine.IsCandidate(asset);

    private static bool IsDynamicReplacementCandidate(TexRef asset)
        => IsAnimatedPortraitCandidate(asset) || IsSkillAnimationCandidate(asset) || asset?.IsSkillMovie == true;

    private void OpenDynamicReplacement(TexRef asset, string videoFile = null)
    {
        if (asset?.IsSkillMovie == true) OpenSkillMovie(asset, videoFile);
        else if (IsSkillAnimationCandidate(asset)) OpenSkillAnimation(asset, videoFile);
        else OpenAnimatedPortrait(asset, videoFile);
    }

    private async void LoadSkillMoviePreview(TexRef asset)
    {
        _moviePreview?.Cancel();
        if (!VideoRuntime.IsReady)
        {
            _detailHint.Text = UiText.T("视频组件尚未启用");
            _status.Text = UiText.T("点击动态替换，或到“工具 / 维护”启用视频组件。");
            return;
        }
        using var operation = new CancellationTokenSource();
        _moviePreview = operation;
        int seq = ++_previewSeq;
        try
        {
            _status.Text = UiText.T("正在读取原生技能特写...");
            var info = await Task.Run(() => SkillMovieEngine.InspectAsync(asset.BundlePath, asset.Name, operation.Token));
            if (IsDisposed || _selectedAsset != asset || seq != _previewSeq) return;
            asset.PathId = info.PathId;
            asset.Width = info.Timing.Width;
            asset.Height = info.Timing.Height;
            asset.Format = $"USM / 原技能 {info.Timing.Duration:0.###} 秒";
            asset.MovieTiming = info.Timing;
            UpdateDetailText(asset);
            _detailHint.Text = SkillAnimationDialog.TimingNotice(info.Timing, null) + UiText.T("\n原生技能特写 · 游戏 DLL 不变");
            byte[] bytes = await Task.Run(() => SkillMovieEngine.PreviewAsync(asset.BundlePath, asset.Name, true, operation.Token));
            if (IsDisposed || _selectedAsset != asset || seq != _previewSeq) return;
            var old = _preview.Image;
            _preview.Image = null;
            old?.Dispose();
            _animationPreviewStream?.Dispose();
            _animationPreviewStream = new MemoryStream(bytes);
            _preview.Image = Image.FromStream(_animationPreviewStream);
            _status.Text = UiText.F($"技能特写 · 原时长 {info.Timing.Duration:0.###} 秒 · 当前资源包的动态预览");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!IsDisposed && _selectedAsset == asset && seq == _previewSeq) _status.Text = UiText.T("技能特写预览失败：") + ex.Message;
        }
        finally { if (ReferenceEquals(_moviePreview, operation)) _moviePreview = null; }
    }

    private void OpenAnimatedPortrait(TexRef asset, string videoFile = null)
    {
        if (!IsAnimatedPortraitCandidate(asset)) return;
        if (!EnsureTextureReady(asset)) return;
        string root = AnimatedPortraitRoot();
        using var dialog = new AnimatedPortraitDialog(root, asset.Name, videoFile, new Size(asset.Width, asset.Height), _folder);
        dialog.ShowDialog(this);
        _renderedPages.Remove(PageTools);
        _status.Text = dialog.ResultMessage.Replace('\n', ' ');
        if (_selectedAsset == asset)
        {
            UpdateDetailText(asset);
            SetDetailButtons(true, asset.IsTexture);
            LoadPreviewAsync(asset, null);
        }
    }

    private async void OpenSkillAnimation(TexRef asset, string videoFile = null)
    {
        if (!IsSkillAnimationCandidate(asset) || !EnsureTextureReady(asset)) return;
        try
        {
            _status.Text = UiText.T("正在读取Q版动作帧结构...");
            var info = await Task.Run(() => SkillAnimationEngine.Inspect(asset.BundlePath, asset.PathId, asset.Name));
            if (IsDisposed) return;
            using var dialog = new SkillAnimationDialog(asset, info, _backupDir, videoFile);
            dialog.ShowDialog(this);
            _status.Text = dialog.ResultMessage.Replace('\n', ' ');
            if (dialog.Replaced)
            {
                PackService.Upsert(_ws, new ModEntry
                {
                    Bundle = asset.BundleName,
                    PathId = asset.PathId,
                    TextureName = asset.Name,
                    Width = asset.Width,
                    Height = asset.Height,
                    Label = UiText.T("Q版动作")
                });
                asset.Modded = true;
            }
            else if (dialog.Restored)
            {
                _ws.Entries.RemoveAll(entry =>
                    string.Equals(entry.Bundle, asset.BundleName, StringComparison.OrdinalIgnoreCase));
                asset.Modded = false;
            }
            if (dialog.Replaced || dialog.Restored)
            {
                StopModificationVerification();
                _modifications?.Invalidate(asset.BundlePath);
                PackService.SaveWorkspace(_folder, _ws);
                RefreshModificationViews();
                VerifyModificationsAsync();
                if (_selectedAsset == asset)
                {
                    UpdateDetailText(asset);
                    LoadPreviewAsync(asset, null);
                }
            }
        }
        catch (Exception ex)
        {
            _status.Text = UiText.T("Q版动作读取失败：") + ex.Message;
            MessageBox.Show(_status.Text, UiText.T("错误"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async void OpenSkillMovie(TexRef asset, string videoFile = null)
    {
        if (asset?.IsSkillMovie != true || _openingSkillMovie) return;
        _openingSkillMovie = true;
        try
        {
            if (!VideoRuntimeDialog.EnsureReady(this)) return;
            _status.Text = UiText.T("正在读取技能特写的原生尺寸与时长...");
            var info = await Task.Run(() => SkillMovieEngine.InspectAsync(asset.BundlePath, asset.Name, CancellationToken.None));
            if (IsDisposed) return;
            asset.PathId = info.PathId;
            asset.Width = info.Timing.Width;
            asset.Height = info.Timing.Height;
            asset.Format = $"USM / 原技能 {info.Timing.Duration:0.###} 秒";
            asset.MovieTiming = info.Timing;
            using var dialog = new SkillAnimationDialog(asset, info, _backupDir, videoFile);
            dialog.ShowDialog(this);
            _status.Text = dialog.ResultMessage;
            if (dialog.Replaced)
            {
                PackService.Upsert(_ws, new ModEntry
                {
                    Bundle = asset.BundleName, PathId = asset.PathId, TextureName = asset.Name,
                    Width = asset.Width, Height = asset.Height, Label = UiText.T("技能特写"), Kind = ResourceKinds.SkillMovie
                });
                asset.Modded = true;
            }
            else if (dialog.Restored)
            {
                _ws.Entries.RemoveAll(entry => string.Equals(entry.Bundle, asset.BundleName, StringComparison.OrdinalIgnoreCase));
                asset.Modded = false;
            }
            if (dialog.Replaced || dialog.Restored)
            {
                StopModificationVerification();
                _modifications?.Invalidate(asset.BundlePath);
                PackService.SaveWorkspace(_folder, _ws);
                RefreshModificationViews();
                VerifyModificationsAsync();
                var cards = _flow.Controls.OfType<RoundedCard>().Where(card => card.Tag is TexRef target &&
                    target.BundlePath == asset.BundlePath && target.Name == asset.Name).ToList();
                foreach (var card in cards)
                {
                    card.SetFill(asset.Modded ? Theme.CardMod : Theme.Card);
                    card.Controls[1].Text = CardText(asset);
                }
                _ = LoadThumbnailsAsync(_loadSeq, cards);
            }
            if (_selectedAsset == asset)
            {
                UpdateDetailText(asset);
                UpdateSelectedListRow(asset);
                LoadPreviewAsync(asset, null);
            }
        }
        catch (Exception ex)
        {
            if (!IsDisposed) MessageBox.Show(this, ex.Message, UiText.T("技能特写未写入"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { _openingSkillMovie = false; }
    }

    private string AnimatedPortraitRoot() => PortraitReplacement.FindRoot(
        string.Equals(Path.GetFileName(_folder), "StandaloneWindows64", StringComparison.OrdinalIgnoreCase)
            && Directory.Exists(_index?.HotDir) ? _index.HotDir : _folder, _folder);

    private PortraitReplacement.Receipt SelectedDynamicReceipt() => AnimatedPortraitPatch.IsSupportedTarget(_selectedAsset?.Name) && !string.IsNullOrWhiteSpace(_folder)
        ? PortraitReplacement.ReadReceipt(AnimatedPortraitRoot(), _selectedAsset.Name) : null;

    private void UpdateRestoreButton(bool hasAsset)
    {
        _restoreResourceBtn.Text = SelectedDynamicReceipt() != null ? UiText.T("还原动态替换") : UiText.T("还原当前资源包");
        _restoreResourceBtn.Enabled = hasAsset && !_restoring && CanRestoreSelection();
    }

    private bool CanRestoreSelection() => _selectedAsset != null && (SelectedDynamicReceipt() != null ||
        !string.IsNullOrWhiteSpace(_selectedAsset.BundlePath) && !string.IsNullOrWhiteSpace(_backupDir) &&
        File.Exists(ResourceLocator.BackupPath(_selectedAsset.BundlePath, _backupDir, _selectedAsset.BundleName)));

    private async Task RestoreSelectedResourceAsync()
    {
        if (_restoring || _selectedAsset == null) return;
        if (SelectedDynamicReceipt() != null) { await RestoreDynamicAsync(); return; }
        var asset = _selectedAsset;
        string folder = _folder, backupDir = _backupDir;
        var workspace = _ws;
        _restoring = true;
        SetDetailButtons(true, asset.IsTexture);
        try
        {
            PortraitReplacement.EnsureGameClosed();
            var plan = await Task.Run(() => BundleRestore.Prepare(asset.BundlePath, backupDir, asset.BundleName));
            if (IsDisposed) return;
            if (MessageBox.Show(this,
                UiText.F($"还原 {asset.Name} 所在资源包？\n\n该包会恢复到首次备份的版本，同包中的其他修改也会撤销。其他资源包不动。"),
                UiText.T("还原当前资源包"), MessageBoxButtons.OKCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.OK) return;
            _status.Text = UiText.T("正在还原并校验资源包...");
            StopModificationVerification();
            _modifications?.Invalidate(asset.BundlePath);
            await Task.Run(() =>
            {
                if (asset.IsSkillMovie)
                {
                    SkillMovieEngine.Read(plan.Backup, asset.Name);
                    AnimatedPortraitPatch.VerifyTypeLayout(plan.Target, plan.Backup);
                }
                BundleRestore.Apply(plan);
            });
            workspace.Entries.RemoveAll(entry => string.Equals(entry.Bundle, asset.BundleName, StringComparison.OrdinalIgnoreCase));
            PackService.SaveWorkspace(folder, workspace);
            asset.Modded = false;
            if (IsDisposed || !string.Equals(_folder, folder, StringComparison.OrdinalIgnoreCase)) return;
            UpdateDashboard();
            ResetBrowseState();
            if (_activePage == PageBrowse) ShowCurrentAssets();
            if (_selectedAsset == asset) { UpdateDetailText(asset); LoadPreviewAsync(asset, null); }
            VerifyModificationsAsync();
            _status.Text = UiText.T("已还原并校验：") + asset.Name;
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, UiText.T("还原未完成"), MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally
        {
            _restoring = false;
            if (!IsDisposed) SetDetailButtons(_selectedAsset != null, _selectedAsset?.IsTexture == true);
        }
    }

    private async Task RestoreDynamicAsync()
    {
        if (_restoring || string.IsNullOrWhiteSpace(_folder)) return;
        string root = AnimatedPortraitRoot();
        var receipt = PortraitReplacement.ReadReceipt(root);
        if (receipt == null) { _status.Text = UiText.T("当前没有动态替换记录"); return; }
        if (MessageBox.Show(this,
            receipt.Mode == IndependentVideoPatch.Mode
                ? UiText.F($"还原全部 {receipt.Portraits?.Count ?? 1} 个独立动态目标？\n\n恢复首次备份的程序集，删除本工具新增的独立视频。官方异画和其他 Mod 不动。")
                : UiText.F($"还原 {receipt.Texture} 的动态替换？\n\n将恢复首次动态修改前的程序集包和视频包，释放 {receipt.VideoKey}。其他 Mod 不动。"),
            UiText.T("还原动态替换"), MessageBoxButtons.OKCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.OK) return;
        _restoring = true;
        SetDetailButtons(_selectedAsset != null, _selectedAsset?.IsTexture == true);
        try
        {
            _status.Text = UiText.T("正在还原动态替换...");
            await Task.Run(() => PortraitReplacement.Restore(root));
            if (IsDisposed) return;
            if (_selectedAsset != null) { UpdateDetailText(_selectedAsset); LoadPreviewAsync(_selectedAsset, null); }
            if (_activePage == PageTools) ShowToolsPage();
            _status.Text = receipt.Mode == IndependentVideoPatch.Mode ? UiText.T("已还原动态替换并移除独立视频") : UiText.T("已还原动态替换并释放原生视频槽位");
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, UiText.T("还原未完成"), MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally
        {
            _restoring = false;
            if (!IsDisposed) SetDetailButtons(_selectedAsset != null, _selectedAsset?.IsTexture == true);
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _indexLoad?.Cancel();
        StopModificationVerification();
        _loadSeq++;
        _previewSeq++;
        _artIndex = null;
        _searchDelay.Dispose();
        ClearWorkshopArt();
        _fallbackArtwork?.Dispose();
        _moviePreview?.Cancel();
        if (_preview != null)
        {
            var image = _preview.Image;
            _preview.Image = null;
            image?.Dispose();
        }
        _animationPreviewStream?.Dispose();
        base.OnFormClosed(e);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_importingPack)
        {
            e.Cancel = true;
            _status.Text = UiText.T("图包正在写入，完成后即可关闭窗口");
        }
        if (_exportingBundles)
        {
            e.Cancel = true;
            _status.Text = UiText.T("ZIP 正在导出，完成后即可关闭窗口");
        }
        base.OnFormClosing(e);
    }

    private bool _exportingBundles;

    private void OpenMobileExport()
    {
        var images = CurrentModEntries().Select(FindAssetForEntry).Where(asset => asset?.IsTexture == true)
            .DistinctBy(asset => asset.Name, StringComparer.Ordinal).Select(MobileTexturePackage.FromTexture).ToList();
        using var dialog = new MobileExportDialog(images);
        dialog.ShowDialog(this);
    }

    private async void ExportBundleZip(IEnumerable<TexRef> assets, string defaultName)
    {
        if (_exportingBundles) return;
        var selected = assets.Where(asset => asset != null).ToList();
        if (!string.IsNullOrWhiteSpace(_folder) && IndexService.UserCacheFor(_folder) == null
            && selected.Count > 0 && selected.All(asset => asset.IsTexture && ResourceLocator.IsWrappedData(asset.BundlePath))
            && MobileTextureBundle.IsAndroid(selected[0].BundlePath))
        {
            using var mobile = new MobileExportDialog(selected.DistinctBy(asset => asset.Name, StringComparer.Ordinal).Select(MobileTexturePackage.FromTexture));
            mobile.ShowDialog(this);
            return;
        }
        var bundles = selected
            .Where(a => a != null && !string.IsNullOrWhiteSpace(a.BundlePath) && File.Exists(a.BundlePath))
            .GroupBy(a => a.BundlePath, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
        if (bundles.Count == 0)
        {
            MessageBox.Show(UiText.T("没有可导出的 bundle 文件。若是旧记录，请先刷新索引。"), UiText.T("提示"));
            return;
        }

        try
        {
            var sources = new[]
            {
                new ReplacementZip.Source(_folder, RuntimeBundleLocator.SamePath(_folder, _index?.GameCacheDir) ? "安装缓存"
                    : RuntimeBundleLocator.SamePath(_folder, _index?.HotDir) ? "热更新缓存" : "基础资源"),
                new ReplacementZip.Source(_index?.HotDir, "热更新缓存"),
                new ReplacementZip.Source(_index?.GameCacheDir, "安装缓存")
            };
            var paths = bundles.Select(bundle => bundle.BundlePath).ToArray();
            bool mixed = ReplacementZip.Plan(sources, paths, Path.Combine(Path.GetTempPath(), "export.zip")).Count > 1;
            using var dlg = new SaveFileDialog
            {
                Title = mixed ? UiText.T("导出到此位置（不同资源目录分别生成 ZIP）") : UiText.T("导出 Bundle ZIP"),
                Filter = UiText.T("Bundle 压缩包|*.zip"),
                FileName = SafeFileName(defaultName) + ".zip",
                OverwritePrompt = false
            };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            var parts = ReplacementZip.Plan(sources, paths, dlg.FileName);
            var existing = parts.Where(part => File.Exists(part.Destination)).Select(part => part.Destination).ToList();
            if (existing.Count > 0 && MessageBox.Show(this, UiText.T("以下文件已存在，是否替换？\n\n") + string.Join("\n", existing),
                UiText.T("替换导出文件"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            _exportingBundles = true;
            _status.Text = mixed ? UiText.T("正在按资源目录分别导出 ZIP...") : UiText.T("正在导出 Bundle ZIP...");
            var result = await Task.Run(() => ReplacementZip.Export(parts));
            if (IsDisposed) return;
            int files = result.Sum(part => part.Files);
            _status.Text = UiText.F($"已导出 {result.Count} 个直接替换 ZIP，共 {files} 个文件");
            string locations = string.Join("\n\n", result.Select(part => UiText.F($"{UiText.T(part.Label)}（{part.Files} 个文件）\n{part.Destination}\n覆盖目录：{parts.Single(plan => plan.Destination == part.Destination).Root}")));
            MessageBox.Show(this, UiText.F($"导出成功。\n\n{locations}\n\n")
                + UiText.T("分别解压到上面对应的覆盖目录，不要混用用户缓存和安装缓存。")
                + UiText.T("\nPC / 手机资源格式不会自动转换。"), UiText.T("导出成功"));
        }
        catch (Exception ex)
        {
            if (IsDisposed) return;
            MessageBox.Show(UiText.F($"导出 bundle ZIP 失败：{ex.Message}"), UiText.T("错误"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            _status.Text = UiText.T("导出 bundle ZIP 失败");
        }
        finally { _exportingBundles = false; }
    }

    private TexRef FindAssetForEntry(ModEntry entry)
    {
        var indexed = _modifications != null ? _modifications.Find(entry) : _index?.FindTextureTarget(entry.Bundle, entry.TextureName, entry.PathId);
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
        if (_folder == null) { _status.Text = UiText.T("请先打开目录"); return; }
        if (_ws.Entries.Count == 0)
        {
            MessageBox.Show(UiText.T("当前还没有改过的贴图，无法导出图包。"), UiText.T("提示"));
            return;
        }

        var current = CurrentModEntries();
        if (current.Count == 0)
        {
            MessageBox.Show(this, UiText.T("当前没有可关联到现有资源的修改。旧记录已保留，请等待后台核验或重新核验修改。"), UiText.T("没有可导出的修改"));
            return;
        }
        var textures = current.Where(entry => entry.Kind == ResourceKinds.Texture && !NameParser.IsSkillMovie(entry.TextureName)).ToArray();
        if (textures.Length == 0)
        {
            MessageBox.Show(this, UiText.T("技能特写是视频资源，请使用“导出已改Bundle ZIP”。"), UiText.T("导出视频资源"));
            return;
        }
        using var selection = new ExportSelectionDialog(textures, false);
        if (selection.ShowDialog(this) != DialogResult.OK) return;
        using var dlg = new SaveFileDialog { Filter = UiText.F($"吉星图包|*{PackService.PackExt}"), FileName = UiText.T("我的资源包") + PackService.PackExt };
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
                long pathId = asset is { PathId: not 0 } ? asset.PathId : e.PathId;
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
            _status.Text = UiText.F($"已导出图包：{images.Count} 张 -> {Path.GetFileName(dlg.FileName)}");
            MessageBox.Show(UiText.F($"图包导出成功。\n\n包含 {images.Count} 张贴图\n{dlg.FileName}"), UiText.T("导出成功"));
        }
        catch (Exception ex)
        {
            MessageBox.Show(UiText.F($"导出失败：{ex.Message}"), UiText.T("错误"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private bool _importingPack;

    private async void ImportPack()
    {
        if (_importingPack || _restoring) return;
        if (_index == null)
        {
            _status.Text = _loadingIndex ? _loadStrip.Caption : UiText.T("请先打开要应用到的目录。");
            return;
        }
        using var dlg = new OpenFileDialog { Filter = UiText.F($"吉星图包|*{PackService.PackExt}"),
            Title = UiText.F($"导入图包到：{EditionCaption}") };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        var index = _index;
        var folder = _folder;
        var backup = _backupDir;
        var workspace = _ws;
        var path = dlg.FileName;
        var surfaces = new Control[] { _navigation, _centerPanel, _leftPanel, _rightPanel, _paneBar };
        _importingPack = true;
        StopModificationVerification();
        _loadSeq++;
        _previewSeq++;
        _moviePreview?.Cancel();
        foreach (var control in surfaces) control.Enabled = false;
        _status.Text = SourceCaption;
        SetLoadStage(LoadStage.Import);
        IProgress<(int done, int total)> progress = new Progress<(int done, int total)>(p =>
        {
            if (_importingPack && !IsDisposed) SetLoadStage(LoadStage.Import, p.done, p.total);
        });
        string message;
        bool error = false;
        try
        {
            var (result, saveError) = await Task.Run(() =>
            {
                PortraitReplacement.EnsureGameClosed();
                var clock = Stopwatch.StartNew();
                var res = PackService.ImportV2(path, index, folder, _engine, backup, workspace, (done, total) =>
                {
                    if (done == 0 || done == total || clock.ElapsedMilliseconds >= 80)
                    { progress.Report((done, total)); clock.Restart(); }
                });
                string saveError = null;
                if (res.Targets != 0)
                    try { PackService.SaveWorkspace(folder, workspace); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { saveError = ex.Message; }
                return (res, saveError);
            });
            foreach (var bundle in result.WrittenBundles) _modifications?.Invalidate(bundle);
            _renderedPages.Remove(PageBrowse);
            if (_activePage == PageBrowse) ShowCurrentPage();
            message = UiText.F($"图包「{result.PackName}」应用完成：\n\n{EditionCaption}：写入 {result.Applied} 张图、{result.Targets} 个贴图位置。");
            if (result.VariantTargets > 0) message += "\n" + UiText.F($"其中 {result.VariantTargets} 处使用普通版 / 和谐版匹配。");
            if (result.Missing > 0) message += "\n" + UiText.F($"当前服没有对应贴图：{result.Missing} 张。");
            if (result.Failed > 0) message += "\n" + UiText.F($"读取或写入失败：{result.Failed} 处。");
            var issues = result.Issues.Take(5).Select(issue => issue.Error == null ? issue.Name : issue.Name + "： " + issue.Error);
            if (result.Issues.Count > 0) message += "\n\n" + string.Join("\n", issues);
            if (saveError != null) message += "\n\n" + UiText.T("；记录尚未保存：") + saveError;
            _status.Text = UiText.F($"图包已写入 {result.Targets} 处");
        }
        catch (Exception ex)
        {
            error = true;
            message = UiText.F($"导入失败：{ex.Message}");
            _status.Text = UiText.T("导入失败");
        }
        finally
        {
            _importingPack = false;
            foreach (var control in surfaces) control.Enabled = true;
            SetLoadStage(index.AssetCount == 0 ? LoadStage.Empty : LoadStage.Ready);
            VerifyModificationsAsync();
        }
        MessageBox.Show(this, message, UiText.T(error ? "错误" : "导入完成"), MessageBoxButtons.OK,
            error ? MessageBoxIcon.Error : MessageBoxIcon.Information);
    }

    private async void MigrateOldMods()
    {
        string oldRoot;
        using (var dlg = new FolderBrowserDialog { Description = UiText.T("选择旧 mod 所在目录（递归找 .bundle / __data）") })
        {
            if (dlg.ShowDialog() != DialogResult.OK) return;
            oldRoot = dlg.SelectedPath;
        }

        string outDir = Path.Combine(oldRoot, "_迁移结果");
        if (MessageBox.Show(UiText.F($"将把\n{oldRoot}\n下的旧 mod 迁移为新版图包，输出到：\n{outDir}\n\n继续？"),
            UiText.T("迁移旧 mod"), MessageBoxButtons.OKCancel) != DialogResult.OK) return;

        try
        {
            var mig = new MigrationService();
            var rep = await Task.Run(() => mig.Migrate(oldRoot, outDir,
                (d, t, f) => { if (IsHandleCreated) BeginInvoke(() => _status.Text = UiText.F($"迁移中... {d}/{t} {f}")); }));

            string groups = string.Join("\n", rep.GroupCounts.Select(k => UiText.F($"  {k.Key}: {k.Value} 张")));
            _status.Text = UiText.F($"迁移完成：{rep.Packs.Count} 个图包");
            MessageBox.Show(UiText.F($"迁移完成。\n\n处理 {rep.Files} 个旧文件，{rep.Textures} 张贴图\n\n{groups}\n\n输出：\n{outDir}"), UiText.T("迁移完成"));
            try { System.Diagnostics.Process.Start("explorer.exe", outDir); } catch { }
        }
        catch (Exception ex)
        {
            MessageBox.Show(UiText.F($"迁移失败：{ex.Message}"), UiText.T("错误"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            _status.Text = UiText.T("迁移失败");
        }
    }

    private void RestoreAll()
    {
        if (_restoring) return;
        try { PortraitReplacement.EnsureGameClosed(); }
        catch (IOException ex) { MessageBox.Show(this, ex.Message, UiText.T("还原未完成")); return; }
        if (_folder == null || !Directory.Exists(_backupDir))
        {
            _status.Text = UiText.T("没有可还原的备份");
            return;
        }
        if (MessageBox.Show(UiText.T("把所有备份过的资源还原为原始文件？"), UiText.T("确认"), MessageBoxButtons.YesNo) != DialogResult.Yes) return;

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
        StopModificationVerification();
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
        _status.Text = UiText.F($"已还原 {n} 个资源文件");
    }

    private void ClearDetails()
    {
        _moviePreview?.Cancel();
        _previewSeq++;
        _selectedAsset = null;
        _preview.Image?.Dispose();
        _preview.Image = null;
        _detailTitle.Text = UiText.T("未选择资源");
        _detailState.Text = "";
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
        using var dlg = new SaveFileDialog { Filter = UiText.T("PNG 图片|*.png"), FileName = SafeFileName(asset.Name) + ".png" };
        if (dlg.ShowDialog() != DialogResult.OK) return;
        try
        {
            File.WriteAllBytes(dlg.FileName, _engine.DecodePng(asset.BundlePath, asset.PathId, 0));
            _status.Text = UiText.T("已导出 ") + Path.GetFileName(dlg.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(UiText.T("导出失败: ") + ex.Message, UiText.T("错误"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void LocateBundle(TexRef asset)
    {
        if (!File.Exists(asset.BundlePath))
        {
            _status.Text = UiText.T("找不到对应资源包文件");
            return;
        }
        LocatePath(asset.BundlePath);
    }

    private void LocatePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            _status.Text = UiText.T("找不到文件");
            return;
        }
        try { Process.Start("explorer.exe", $"/select,\"{path}\""); }
        catch (Exception ex) { _status.Text = UiText.T("定位失败: ") + ex.Message; }
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

}
