namespace JixModMaker;

public partial class MainForm
{
    private readonly Dictionary<string, GridFlow> _pages = new();
    private readonly StudioViewport _viewport = new();
    private readonly HashSet<string> _renderedPages = new();
    private readonly System.Windows.Forms.Timer _searchDelay = new() { Interval = 175 };
    private readonly BufferedPanel _paneBar = new() { Dock = DockStyle.Top, BackColor = Theme.Bar };
    private readonly List<Button> _paneButtons = new();
    private readonly StudioNavigation _navigation;
    private readonly Image _fallbackArtwork;
    private Image _artwork;
    private Image _diceArt;
    private GameIndex _artIndex;
    private int _compactPane = 1;
    private bool _layingOutPanes, _changingLanguage;
    private (int, int, int) _counts;
    private int _overviewGeneration;
    private List<ModEntry> _displayMods = new();
    private Label _homeVerification;
    private Label _homeFolder;

    private static void BindMenuCaptions(ContextMenuStrip menu)
    {
        var labels = menu.Items.OfType<ToolStripMenuItem>().Select(item => (item, key: item.Text)).ToArray();
        menu.Opening += (_, _) =>
        {
            foreach (var (item, key) in labels) item.Text = UiText.T(key);
        };
    }

    private void BuildLeftPanel()
    {
        _sideTitle.Dock = DockStyle.Top;
        _sideTitle.Height = 64;
        _sideTitle.Padding = new Padding(14, 0, 8, 0);
        _sideTitle.ForeColor = Theme.Text;
        _leftPanel.Controls.Add(_categoryList);
        _leftPanel.Controls.Add(_sideTitle);
    }

    private void BuildPaneBar()
    {
        foreach (string caption in new[] { "分类", "浏览", "预览" })
        {
            int pane = _paneButtons.Count;
            var button = Theme.FlatButton(caption, 100);
            button.Tag = caption;
            button.Click += (_, _) => SelectPane(pane);
            _paneButtons.Add(button);
            _paneBar.Controls.Add(button);
        }
        _paneBar.Resize += (_, _) => LayoutPaneBar();
    }

    private void SelectPane(int pane)
    {
        _compactPane = pane;
        LayoutMainColumns();
        if (pane == 1) QueueLoadMoreCheck();
    }

    private void LayoutPaneBar()
    {
        if (_paneButtons.Count != 3) return;
        int gap = Sc(4), pad = Sc(12);
        int w = Math.Clamp((_paneBar.ClientSize.Width - 2 * pad - 2 * gap) / 3, 1, Sc(120));
        for (int i = 0; i < 3; i++)
        {
            var button = _paneButtons[i];
            button.SetBounds(pad + (w + gap) * i, Sc(3), w, Math.Max(1, _paneBar.Height - Sc(7)));
            button.BackColor = i == _compactPane ? Theme.CardMod : Theme.Bar;
            button.ForeColor = i == _compactPane ? Theme.Accent : Theme.SubText;
        }
    }

    private static string PageCaption(string page) => UiText.T(page switch
    {
        PageBrowse => "资源库", PagePack => "作品与导出", PageTools => "工具", _ => "工作台"
    });

    private void RefreshChromeLanguage()
    {
        Text = AppBuildInfo.WindowTitle;
        _navigation.RefreshLanguage();
        _pageTitle.Text = PageCaption(_activePage);
        _sideTitle.Text = UiText.T("资源分类");
        _searchBox.PlaceholderText = UiText.T("搜索资源名 / bundle");
        _searchSurface.RefreshLanguage();
        _refreshIndexBtn.Text = UiText.T("刷新索引");
        _replaceTextureBtn.Text = UiText.T("替换贴图");
        _exportPngBtn.Text = UiText.T("导出PNG");
        _exportBundleZipBtn.Text = UiText.T("导出当前Bundle ZIP");
        _replaceBundleBtn.Text = UiText.T("替换所在资源包");
        _locateBtn.Text = UiText.T("定位文件");
        _status.Text = UiText.T("就绪");
        _loadStrip.RefreshCaption();
        _kindBox.RefreshLanguage();
        foreach (var button in _paneButtons) button.Text = UiText.T((string)button.Tag);
        string[] captions = { "资源名", "分组", "尺寸/类型", "Bundle" };
        for (int i = 0; i < captions.Length; i++) _resourceList.Columns[i].Text = UiText.T(captions[i]);
        if (_selectedAsset == null) _detailTitle.Text = UiText.T("未选择资源");
        else UpdateDetailText(_selectedAsset);
        SetDetailButtons(_selectedAsset != null, _selectedAsset?.IsTexture == true);
    }

    private void SwitchLanguage()
    {
        bool saved = UiText.SetEnglish(!UiText.English);
        _changingLanguage = true;
        try
        {
            RefreshChromeLanguage();
            bool liveBrowse = _renderedPages.Contains(PageBrowse) && _pages[PageBrowse].Controls.OfType<RoundedCard>().Any(c => c.Tag is TexRef);
            _renderedPages.Clear();
            if (liveBrowse)
            {
                _renderedPages.Add(PageBrowse);
                BuildCategoryList(CurrentKind);
                RefreshBrowseCaptions();
            }
            Navigate(_activePage);
            if (!saved) _status.Text = UiText.T("语言设置仅在本次运行生效");
        }
        finally { _changingLanguage = false; }
    }

    private void RefreshBrowseCaptions()
    {
        var grid = _pages[PageBrowse];
        Label section = null;
        foreach (Control child in grid.Controls)
        {
            if (Equals(child.Tag, "header")) section = child.Controls.OfType<Label>().FirstOrDefault();
            if (Equals(child.Tag, "load_more") && child.Controls[0] is Label more) more.Text = LoadMoreCaption();
            if (child is not RoundedCard { Tag: TexRef asset } card) continue;
            if (section != null)
            {
                section.Text = "  " + (IsHeroSkinCategory(_selectedCategoryId) ? HeroSkinTitle(asset) : UiText.T(asset.CategoryLabel));
                section = null;
            }
            card.Controls[1].Text = CardText(asset);
            card.Controls[2].Invalidate();
        }
    }

    private void LayoutMainColumns()
    {
        if (_layingOutPanes || _paneButtons?.Count != 3) return;
        _layingOutPanes = true;
        try
        {
            bool browsing = _activePage == PageBrowse;
            bool compact = ClientSize.Width < Sc(900);
            _paneBar.Visible = browsing && compact;
            _paneBar.Height = Math.Max(Sc(39), _paneButtons[0].Font.Height + Sc(17));
            LayoutPaneBar();
            _leftPanel.Dock = compact ? DockStyle.Fill : DockStyle.Left;
            _rightPanel.Dock = compact ? DockStyle.Fill : DockStyle.Right;
            if (!compact)
            {
                _leftPanel.Width = Sc(ClientSize.Width < Sc(1120) ? 210 : 248);
                _rightPanel.Width = Sc(ClientSize.Width < Sc(1120) ? 250 : 306);
            }
            _leftPanel.Visible = browsing && (!compact || _compactPane == 0);
            _rightPanel.Visible = browsing && (!compact || _compactPane == 2);
            _centerPanel.Visible = !browsing || !compact || _compactPane == 1;
        }
        finally { _layingOutPanes = false; }
    }

    private void Navigate(string page)
    {
        if (!_pages.TryGetValue(page, out var next)) return;
        if (page == PageTools && _renderedPages.Contains(page)) RefreshDynamicStatus();
        if (_flow == next && _renderedPages.Contains(page) && !_changingLanguage) return;
        bool changed = _activePage != page;
        using var paint = new StudioPaint(this);
        SuspendLayout();
        _centerPanel.SuspendLayout();
        try
        {
            var previous = _flow;
            _activePage = page;
            _flow = next;
            _pageTitle.Text = PageCaption(page);
            _pageTitle.Art = page is PagePack or PageTools ? _artwork : null;
            _navigation.SelectPage(page, changed);
            _browseFilters.Visible = page == PageBrowse;
            _resourceList.Visible = false;
            LayoutMainColumns();
            LayoutBrowseFilters(_topBar, _refreshIndexBtn);
            next.Size = _viewport.ClientSize;
            if (!_renderedPages.Contains(page)) ShowCurrentPage();
            _viewport.Reveal(next, previous);
            if (page == PageBrowse) QueueLoadMoreCheck();
        }
        finally
        {
            _centerPanel.ResumeLayout(true);
            ResumeLayout(true);
        }
    }

    private void ShowCurrentPage()
    {
        _renderedPages.Add(_activePage);
        _flow.SuspendLayout();
        try
        {
            if (_activePage == PageBrowse) { BuildCategoryList(CurrentKind); ShowCurrentAssets(); }
            else if (_activePage == PagePack) ShowPackPage();
            else if (_activePage == PageTools) ShowToolsPage();
            else ShowDashboardPage();
        }
        finally { _flow.ResumeLayout(true); }
    }

    private void ShowDashboardPage()
    {
        ClearFlow();
        var banner = new WorkshopBanner(() => _artwork ?? _fallbackArtwork, () => _index == null ? _loadStrip.Caption :
            UiText.F($"{_counts.Item1:N0} 个资源包   ·   {_counts.Item2:N0} 个资源   ·   {_counts.Item3:N0} 项修改"))
            { Height = Sc(145), Width = HeaderWidth() };
        _flow.Controls.Add(banner);
        _homeFolder = AddPageCard(UiText.T("资源工作区"), SourceCaption,
            MakeButton(UiText.T("进入资源浏览"), (_, _) => Navigate(PageBrowse)),
            MakeButton(UiText.T("检测游戏"), (_, _) => DetectGame()),
            MakeButton(UiText.T("检测海外服"), (_, _) => DetectEdition(GameEdition.International)),
            MakeButton(UiText.T("打开游戏目录"), (_, _) => OpenGameDir()),
            MakeButton(UiText.T("启动游戏"), (_, _) => LaunchGame()));
        _homeVerification = AddPageCard(UiText.T("我的作品"), _modVerificationInfo,
            MakeButton(UiText.T("导入图包"), (_, _) => ImportPack()),
            MakeButton(UiText.T("导出图包"), (_, _) => ExportPack()),
            MakeButton(UiText.T("打开作品页"), (_, _) => Navigate(PagePack)));
    }

    private async void UpdateDashboard()
    {
        var index = _index;
        var catalog = _modifications;
        int generation = ++_overviewGeneration;
        var snapshot = new ModManifest { Entries = _ws.Entries.ToList() };
        try
        {
            var summary = catalog == null ? (0, 0, new List<ModEntry>()) :
                await Task.Run(() => (index.BundleCount, index.AssetCount, catalog.CurrentEntries(snapshot)));
            if (generation != _overviewGeneration || IsDisposed || Disposing) return;
            _displayMods = summary.Item3;
            _counts = (summary.Item1, summary.Item2, _displayMods.Count);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            if (generation == _overviewGeneration && !IsDisposed) _status.Text = ex.Message;
            return;
        }
        _renderedPages.Remove(PagePack);
        if (_activePage == PagePack && _flow == _pages[PagePack]) ShowCurrentPage();
        InvalidateArtwork();
    }

    private void InvalidateArtwork()
    {
        _sideTitle.Art = _artwork;
        _pageTitle.Art = _activePage is PagePack or PageTools ? _artwork : null;
        _pageTitle.Badge = _diceArt;
        _pageTitle.Invalidate();
        _sideTitle.Invalidate();
        if (_pages.TryGetValue(PageDashboard, out var home))
            foreach (var banner in home.Controls.OfType<WorkshopBanner>()) banner.Invalidate();
    }

    private void ClearWorkshopArt()
    {
        _artIndex = null;
        var old = _artwork;
        var dice = _diceArt;
        _artwork = _diceArt = null;
        InvalidateArtwork();
        old?.Dispose();
        dice?.Dispose();
    }

    private async void LoadWorkshopArt(GameIndex index, string folder)
    {
        if (ReferenceEquals(_artIndex, index)) return;
        _artIndex = index;
        try
        {
            var (png, badge) = await Task.Run(() => (WorkshopArt.Read(index, folder), WorkshopArt.ReadBadge(index, folder)));
            if (IsDisposed || Disposing || !ReferenceEquals(_artIndex, index)) return;
            _artwork?.Dispose();
            _diceArt?.Dispose();
            _artwork = png == null ? null : BytesToImage(png);
            _diceArt = badge == null ? null : BytesToImage(badge);
            InvalidateArtwork();
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException) { }
    }

    private string LoadMoreCaption() => UiText.F($"已显示 {_browseLoadedCount}/{_browseTotalCount}，继续向下滚动自动加载 · 点击立即加载");
}
