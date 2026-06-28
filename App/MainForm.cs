using System.Drawing;
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
    private readonly string _initFolder;
    private ModManifest _ws = new();
    private int _loadSeq;                 // 防角色快速切换时旧加载继续填充
    private Button _selHeroBtn;

    public const string Version = "v1.0.4";   // 显示在标题栏, 方便确认是否为最新修复版
    // 以下均为 96DPI(100%缩放) 下的"设计像素", 运行时用 Sc() 按系统缩放放大
    private const int MinThumb = 64, MaxThumb = 256, SrcThumb = 256, Pad = 10, ThumbDef = 132;
    private int _thumb = ThumbDef;

    private float _dpi = 1f;                      // 系统缩放倍率 = DeviceDpi/96 (100%=1, 200%=2)
    private int Sc(int v) => (int)Math.Round(v * _dpi);   // 设计像素 -> 当前缩放下的物理像素
    private const string GameDefault =
        "D:/steam/steamapps/common/Astral Party/8vJXn6CN/AstralParty_CN_Data/StreamingAssets/aa/StandaloneWindows64";

    private readonly GridFlow _flow = new()
    {
        Dock = DockStyle.Fill, BackColor = Theme.FlowBg, Padding = new Padding(10)
    };
    private readonly Panel _leftPanel = new() { Dock = DockStyle.Left, Width = 210, BackColor = Theme.Bar, Visible = false };
    private readonly SideListFlow _heroList = new()
    {
        Dock = DockStyle.Fill, BackColor = Theme.Bar, Padding = new Padding(4, 4, 4, 4)
    };
    private readonly Label _status = new()
    {
        Dock = DockStyle.Fill, ForeColor = Theme.SubText, TextAlign = ContentAlignment.MiddleLeft,
        Padding = new Padding(12, 0, 0, 0), Text = "就绪 — 「打开游戏目录」按角色浏览，或「打开文件夹」浏览 mod 包"
    };
    private Label _heroTitle;
    private Panel _bottomBar;

    public MainForm(string initFolder = null)
    {
        _initFolder = initFolder;
        AutoScaleMode = AutoScaleMode.None;   // 完全手动按 DeviceDpi 缩放, 避免框架重复缩放
        Text = "吉星立绘 Mod 制作器  " + Version;
        Width = 1200; Height = 800;
        MinimumSize = new Size(560, 420);   // 放小: 200%缩放的小屏笔记本也能完整放下/自由缩放
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Bg;
        Font = Theme.UI(9f);

        // 顶栏: 自适应换行 (窄屏/高DPI 自动折到第二行, 不再挤压截断)
        var topBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = true, BackColor = Theme.Bar, Padding = new Padding(8, 7, 8, 7),
            MinimumSize = new Size(0, 50)
        };
        var btnGame = Theme.FlatButton("🎭 游戏目录"); btnGame.Margin = new Padding(3);
        btnGame.Click += (_, _) => OpenGameDir();
        var btnFolder = Theme.FlatButton("📂 文件夹"); btnFolder.Margin = new Padding(3);
        btnFolder.Click += (_, _) => OpenFolder();
        var btnExport = Theme.FlatButton("📦 导出"); btnExport.Margin = new Padding(3);
        btnExport.Click += (_, _) => ExportPack();
        var btnImport = Theme.FlatButton("📥 导入"); btnImport.Margin = new Padding(3);
        btnImport.Click += (_, _) => ImportPack();
        var btnRestore = Theme.FlatButton("↩ 还原"); btnRestore.Margin = new Padding(3);
        btnRestore.Click += (_, _) => RestoreAll();
        var btnMigrate = Theme.FlatButton("🔄 迁移旧mod"); btnMigrate.Margin = new Padding(3);
        btnMigrate.Click += (_, _) => MigrateOldMods();
        topBar.Controls.AddRange(new Control[] { btnGame, btnFolder, btnExport, btnImport, btnRestore, btnMigrate });

        _heroTitle = new Label
        {
            Dock = DockStyle.Top, Height = 30, Text = "  角色 / 分组", ForeColor = Theme.Text,
            Font = Theme.UI(10f, true), TextAlign = ContentAlignment.MiddleLeft, BackColor = Theme.Bg
        };
        _leftPanel.Controls.Add(_heroList);
        _leftPanel.Controls.Add(_heroTitle);

        _bottomBar = new Panel { Dock = DockStyle.Bottom, Height = 30, BackColor = Theme.Bar };
        _status.Font = Theme.UI(9f);
        _bottomBar.Controls.Add(_status);

        Controls.Add(_flow);
        Controls.Add(_leftPanel);
        Controls.Add(_bottomBar);
        Controls.Add(topBar);

        _flow.MouseWheel += OnFlowWheel;
        // 分组标题宽度自适应由 GridFlow.OnLayout 统一处理(每次布局前缩到客户区宽), 避免撑出横向滚动条
    }

    /// <summary>句柄就绪后读取真实 DeviceDpi, 把所有"设计像素"按系统缩放放大一次(启动自适应)。</summary>
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyDpiScale(DeviceDpi);
    }

    /// <summary>跨不同缩放的显示器拖动时, 实时按新 DPI 重新缩放整套界面。</summary>
    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        ApplyDpiScale(e.DeviceDpiNew);
    }

    private bool _dpiSized;

    private void ApplyDpiScale(int deviceDpi)
    {
        _dpi = deviceDpi / 96f;

        // 固定像素的容器尺寸
        _leftPanel.Width = Sc(210);
        if (_heroTitle != null) _heroTitle.Height = Sc(30);
        if (_bottomBar != null) _bottomBar.Height = Sc(30);
        _flow.Padding = new Padding(Sc(10));
        _heroList.Padding = new Padding(Sc(4));

        // 缩略图尺寸: 按比例保持物理大小不变 (并夹在缩放后的上下限内)
        _thumb = Math.Clamp(Sc(ThumbDef), Sc(MinThumb), Sc(MaxThumb));

        var wa = Screen.FromControl(this).WorkingArea;
        MinimumSize = new Size(Math.Min(Sc(560), wa.Width), Math.Min(Sc(420), wa.Height));
        // 只在启动首次按缩放放大窗口(不超工作区); 跨屏 DPI 变化时窗口由系统调整, 这里只缩内容
        if (!_dpiSized && WindowState == FormWindowState.Normal)
        {
            Size = new Size(Math.Min(Sc(1200), wa.Width), Math.Min(Sc(800), wa.Height));
            _dpiSized = true;
        }

        // 已经显示的卡片/标题/角色按钮全部按新尺寸重排
        RescaleExisting();
    }

    private void RescaleExisting()
    {
        _flow.SuspendLayout();
        foreach (Control c in _flow.Controls)
        {
            if (c is RoundedCard rc) LayoutCard(rc);
            else if ((c.Tag as string) == "header") c.Height = Sc(34);
        }
        _flow.ResumeLayout();
        foreach (Control c in _heroList.Controls) c.Height = Sc(32);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        NoticeDialog.ShowIfFirst(this);   // 首次启动: 免费开源提示, 防被倒卖付费
        if (!string.IsNullOrEmpty(_initFolder) && Directory.Exists(_initFolder))
        {
            SetFolder(_initFolder);
            if (_initFolder.Replace('\\', '/').Contains("StandaloneWindows64"))
            { _leftPanel.Visible = true; LoadIndexAsync(); }
            else { _leftPanel.Visible = false; ShowFolderTextures(); }
        }
    }

    private void SetFolder(string path)
    {
        _folder = path;
        _backupDir = Path.Combine(_folder, "_原始备份");
        _ws = PackService.LoadWorkspace(_folder);
    }

    // ---------------- 角色模式 ----------------

    private void OpenGameDir()
    {
        using var dlg = new FolderBrowserDialog { Description = "选择游戏 StandaloneWindows64 目录" };
        if (Directory.Exists(GameDefault)) dlg.SelectedPath = GameDefault;
        if (dlg.ShowDialog() != DialogResult.OK) return;
        SetFolder(dlg.SelectedPath);
        _leftPanel.Visible = true;
        LoadIndexAsync();
    }

    private async void LoadIndexAsync()
    {
        _heroList.Controls.Clear();
        _flow.Controls.Clear();
        _index = _indexSvc.Load(_folder);
        if (_index == null)
        {
            _status.Text = "首次扫描立绘索引（约 10 秒，仅首次）…";
            string dir = _folder;
            _index = await Task.Run(() => _indexSvc.Build(dir, (d, t) =>
            {
                if (d % 500 == 0 && IsHandleCreated)
                    BeginInvoke(() => _status.Text = $"扫描立绘索引… {d}/{t}");
            }));
            try { _indexSvc.Save(_index); } catch { }
        }
        BuildHeroList();
    }

    private void BuildHeroList()
    {
        var groups = _index.Items
            .Select(e => new { e, c = NameParser.Parse(e.Name) })
            .GroupBy(x => x.c.GroupKey)
            .Select(g => new { Key = g.Key, Items = g.Select(x => x.e).ToList(), Order = OrderOf(g.First().c), IsHero = g.First().c.IsHero })
            .OrderBy(g => g.Order).ThenBy(g => g.Key)
            .ToList();

        _heroList.SuspendLayout();
        foreach (var g in groups)
        {
            var items = g.Items;
            string key = g.Key;
            var btn = new Button
            {
                Text = $"{_naming.Display(key)}  ({items.Count})", Tag = key,
                FlatStyle = FlatStyle.Flat, ForeColor = Theme.Text, BackColor = Theme.Card,
                Font = Theme.UI(9.5f), Width = 188, Height = Sc(32), TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(2, 2, 2, 0), Cursor = Cursors.Hand, Padding = new Padding(Sc(8), 0, 0, 0)
            };
            btn.FlatAppearance.BorderSize = 0;
            btn.FlatAppearance.MouseOverBackColor = Theme.AccentDim;
            btn.Click += (_, _) => ShowGroup(items, btn);

            // 仅角色组(含怪兽)可重命名; 卡图(手牌/其他)不可
            if (g.IsHero)
            {
                btn.MouseUp += (_, me) => { if (me.Button == MouseButtons.Right) RenameGroup(btn, key, items.Count); };
                var menu = new ContextMenuStrip();
                menu.Items.Add("重命名", null, (_, _) => RenameGroup(btn, key, items.Count));
                btn.ContextMenuStrip = menu;
            }

            _heroList.Controls.Add(btn);
        }
        _heroList.ResumeLayout();

        _status.Text = $"索引就绪：{_index.Items.Count} 张立绘 · {groups.Count} 个分组。点左侧选角色。";
        if (_heroList.Controls.Count > 0)
            ((Button)_heroList.Controls[0]).PerformClick();
    }

    private static int OrderOf(AssetCategory c)
        => c.IsHero && int.TryParse(c.HeroId, out var n) ? n
         : c.IsHandCard ? 1_000_000 : 2_000_000;

    private void RenameGroup(Button btn, string key, int count)
    {
        string cur = _naming.Custom(key);
        string name = PromptDialog.Show(this, "重命名", $"给「{key}」起个名字（留空恢复默认）：", cur);
        if (name == null) return;   // 取消
        _naming.Set(key, name);
        btn.Text = $"{_naming.Display(key)}  ({count})";
    }

    private TexRef ToTexRef(TexIndexEntry e, AssetCategory c) => new()
    {
        BundlePath = Path.Combine(_folder, e.Bundle), PathId = e.PathId,
        Name = e.Name, Width = e.Width, Height = e.Height,
        Display = c.SubLabel, Modded = PackService.Contains(_ws, e.Bundle, e.PathId)
    };

    private void ShowGroup(List<TexIndexEntry> items, Button btn)
    {
        if (_selHeroBtn != null) _selHeroBtn.BackColor = Theme.Card;
        btn.BackColor = Theme.Accent;
        _selHeroBtn = btn;

        var parsed = items.Select(e => new { e, c = NameParser.Parse(e.Name) }).ToList();
        bool isHero = parsed[0].c.IsHero;

        List<(string title, List<TexRef> texs)> sections;
        if (!isHero)
        {
            // 卡图(手牌/其他): 不分组, 平铺
            sections = new() { ("", parsed.OrderBy(x => x.e.Name).Select(x => ToTexRef(x.e, x.c)).ToList()) };
        }
        else if (parsed.Select(x => x.c.HeroId).Distinct().Count() > 1)
        {
            // 怪兽组: 按怪兽ID分节
            sections = parsed
                .GroupBy(x => x.c.HeroId)
                .OrderBy(g => int.TryParse(g.Key, out var n) ? n : int.MaxValue)
                .Select(g => (
                    title: "怪兽 " + g.Key,
                    texs: g.OrderBy(x => x.c.KindOrder).ThenBy(x => x.e.Name)
                           .Select(x => ToTexRef(x.e, x.c)).ToList()
                )).ToList();
        }
        else
        {
            // 普通角色: 按皮肤分节 (原皮/皮肤01/皮肤02/Max), 节内按类型排序
            sections = parsed
                .GroupBy(x => x.c.Skin)
                .OrderBy(g => g.First().c.SkinOrder)
                .Select(g => (
                    title: g.Key,
                    texs: g.OrderBy(x => x.c.KindOrder).ThenBy(x => x.e.Name)
                           .Select(x => ToTexRef(x.e, x.c)).ToList()
                )).ToList();
        }
        ShowSections(sections);
    }

    // ---------------- 文件夹模式 ----------------

    private void OpenFolder()
    {
        using var dlg = new FolderBrowserDialog { Description = "选择含 .bundle 的文件夹（如 mod 包）" };
        if (Directory.Exists("D:/clearmind/mod/JixingModMaker/演示bundle"))
            dlg.SelectedPath = "D:/clearmind/mod/JixingModMaker/演示bundle";
        if (dlg.ShowDialog() != DialogResult.OK) return;
        SetFolder(dlg.SelectedPath);
        _leftPanel.Visible = false;
        ShowFolderTextures();
    }

    private async void ShowFolderTextures()
    {
        _flow.Controls.Clear();
        var bundles = Directory.GetFiles(_folder, "*.bundle");
        if (bundles.Length == 0) { _status.Text = "该文件夹下没有 .bundle 文件"; return; }
        _status.Text = $"扫描中… 共 {bundles.Length} 个 bundle";

        var texs = await Task.Run(() =>
        {
            var list = new List<TexRef>();
            foreach (var b in bundles)
            {
                try { list.AddRange(_engine.ListTextures(b)); } catch { }
            }
            return list;
        });
        foreach (var t in texs)
            t.Modded = PackService.Contains(_ws, Path.GetFileName(t.BundlePath), t.PathId);
        ShowSections(new List<(string, List<TexRef>)> { ("", texs) });
    }

    // ---------------- 通用缩略图渲染 ----------------

    private async void ShowSections(List<(string title, List<TexRef> texs)> sections)
    {
        int seq = ++_loadSeq;
        _flow.Controls.Clear();
        int total = sections.Sum(s => s.texs.Count);
        _status.Text = $"加载 {total} 张缩略图…";

        await Task.Run(() =>
        {
            foreach (var sec in sections)
            {
                if (seq != _loadSeq) return;
                if (!string.IsNullOrEmpty(sec.title) && IsHandleCreated)
                    BeginInvoke(() => { if (seq == _loadSeq) AddHeader(sec.title); });

                foreach (var tex in sec.texs)
                {
                    if (seq != _loadSeq) return;
                    byte[] png;
                    try { png = _engine.DecodePng(tex.BundlePath, tex.PathId, Sc(SrcThumb)); }
                    catch { continue; }
                    if (IsHandleCreated)
                        BeginInvoke(() => { if (seq == _loadSeq) _flow.Controls.Add(MakeCard(tex, png)); });
                }
            }
        });
        if (seq == _loadSeq)
        {
            int modded = sections.Sum(s => s.texs.Count(t => t.Modded));
            _status.Text = $"共 {total} 张" + (modded > 0 ? $"，{modded} 张已改" : "") + "  ·  拖图替换 · Ctrl+滚轮缩放";
        }
    }

    // 初始宽度(布局前的占位); 实际宽度由 GridFlow.OnLayout 按客户区精确校正
    private int HeaderWidth() => Math.Max(80, _flow.ClientSize.Width - _flow.Padding.Horizontal - 12);

    /// <summary>添加一个撑满整行的皮肤分组标题。</summary>
    private void AddHeader(string title)
    {
        var p = new Panel
        {
            Tag = "header", Width = HeaderWidth(), Height = Sc(34),
            Margin = new Padding(Sc(6), Sc(12), Sc(6), Sc(2)), BackColor = Theme.FlowBg
        };
        p.Controls.Add(new Panel { Dock = DockStyle.Bottom, Height = Sc(2), BackColor = Theme.AccentDim });
        p.Controls.Add(new Label
        {
            Text = "  " + title, Dock = DockStyle.Fill, ForeColor = Theme.Accent,
            Font = Theme.UI(11.5f, true), TextAlign = ContentAlignment.MiddleLeft
        });
        _flow.Controls.Add(p);
        _flow.SetFlowBreak(p, true);
    }

    private RoundedCard MakeCard(TexRef tex, byte[] png)
    {
        var pic = new PictureBox { SizeMode = PictureBoxSizeMode.Zoom, BackColor = Theme.PicBg, Image = BytesToImage(png) };
        var lbl = new Label
        {
            Text = $"{tex.Display ?? tex.Name}\n{tex.Width}×{tex.Height}",
            ForeColor = Theme.Text, Font = Theme.UI(7.5f), TextAlign = ContentAlignment.TopCenter,
            AutoEllipsis = true, BackColor = Color.Transparent
        };
        var card = new RoundedCard { Margin = new Padding(6), Tag = tex, AllowDrop = true };
        card.SetFill(tex.Modded ? Theme.CardMod : Theme.Card);
        card.Controls.Add(pic);
        card.Controls.Add(lbl);
        LayoutCard(card);

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
        foreach (Control c in new Control[] { card, pic, lbl })
        {
            c.AllowDrop = true; c.DragEnter += DragEnter; c.DragLeave += DragLeave; c.DragDrop += DragDrop;
        }

        // 拖出导出: 从格子往外拖（到桌面/文件夹）= 导出该图 PNG，与拖入对称
        Point dragStart = Point.Empty; bool mayDrag = false;
        void MDown(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) { dragStart = e.Location; mayDrag = true; } }
        void MUp(object s, MouseEventArgs e) => mayDrag = false;
        void MMove(object s, MouseEventArgs e)
        {
            if (!mayDrag || e.Button != MouseButtons.Left) return;
            if (Math.Abs(e.X - dragStart.X) < SystemInformation.DragSize.Width &&
                Math.Abs(e.Y - dragStart.Y) < SystemInformation.DragSize.Height) return;
            mayDrag = false;
            try
            {
                string tmp = Path.Combine(Path.GetTempPath(), SafeFileName(tex.Name) + ".png");
                File.WriteAllBytes(tmp, _engine.DecodePng(tex.BundlePath, tex.PathId, 0));
                ((Control)s).DoDragDrop(new DataObject(DataFormats.FileDrop, new[] { tmp }), DragDropEffects.Copy);
            }
            catch (Exception ex) { _status.Text = "导出失败: " + ex.Message; }
        }
        foreach (Control c in new Control[] { card, pic, lbl })
        {
            c.MouseDown += MDown; c.MouseMove += MMove; c.MouseUp += MUp;
        }

        // 右键菜单: 裁切替换 / 导出这张图 / 定位 bundle 文件
        var menu = new ContextMenuStrip();
        menu.Items.Add("✂ 裁切替换…", null, (_, _) =>
        {
            using var dlg = new OpenFileDialog { Filter = "图片|*.png;*.jpg;*.jpeg;*.bmp;*.webp" };
            if (dlg.ShowDialog() == DialogResult.OK) DoReplace(card, dlg.FileName, forceCrop: true);
        });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("📤 导出这张图…", null, (_, _) => ExportSingle(tex));
        menu.Items.Add("📁 在资源管理器中定位 bundle", null, (_, _) => LocateBundle(tex));
        card.ContextMenuStrip = menu; pic.ContextMenuStrip = menu; lbl.ContextMenuStrip = menu;

        void Pick(object s, EventArgs e)
        {
            using var dlg = new OpenFileDialog { Filter = "图片|*.png;*.jpg;*.jpeg;*.bmp;*.webp" };
            if (dlg.ShowDialog() == DialogResult.OK) DoReplace(card, dlg.FileName);
        }
        pic.DoubleClick += Pick; card.DoubleClick += Pick; lbl.DoubleClick += Pick;

        return card;
    }

    private void LayoutCard(RoundedCard card)
    {
        var pic = (PictureBox)card.Controls[0];
        var lbl = (Label)card.Controls[1];
        int pad = Sc(Pad), gap = Sc(4), lblH = Sc(28), bottom = Sc(6);
        pic.Location = new Point(pad, pad);
        pic.Size = new Size(_thumb, _thumb);
        lbl.Location = new Point(pad, pad + _thumb + gap);
        lbl.Size = new Size(_thumb, lblH);
        card.Size = new Size(_thumb + pad * 2, pad + _thumb + gap + lblH + bottom);
    }

    private void OnFlowWheel(object sender, MouseEventArgs e)
    {
        if ((ModifierKeys & Keys.Control) == 0) return;
        if (e is HandledMouseEventArgs he) he.Handled = true;
        int next = Math.Clamp(_thumb + (e.Delta > 0 ? Sc(14) : -Sc(14)), Sc(MinThumb), Sc(MaxThumb));
        if (next == _thumb) return;
        _thumb = next;
        _flow.SuspendLayout();
        foreach (Control c in _flow.Controls)
            if (c is RoundedCard card) LayoutCard(card);
        _flow.ResumeLayout();
        _status.Text = $"缩略图大小: {_thumb}px";
    }

    private void DoReplace(RoundedCard card, string imagePath, bool forceCrop = false)
    {
        var tex = (TexRef)card.Tag;
        try
        {
            // 比例与目标差异大(或强制裁切)时弹裁切窗口; 比例接近则直接缩放
            byte[] cropped = null;
            int sw, sh;
            using (var probe = Image.FromFile(imagePath)) { sw = probe.Width; sh = probe.Height; }
            bool needCrop = forceCrop ||
                Math.Abs((double)sw / sh - (double)tex.Width / tex.Height) > 0.02;
            if (needCrop)
            {
                using var crop = new CropDialog(imagePath, tex.Width, tex.Height, tex.Display ?? tex.Name);
                if (crop.ShowDialog(this) != DialogResult.OK) { _status.Text = "已取消"; return; }
                cropped = crop.ResultPng;
            }

            _status.Text = $"替换 {tex.Display ?? tex.Name} …";
            if (cropped != null)
                _engine.ReplaceInPlaceFromBytes(tex.BundlePath, tex.PathId, cropped, _backupDir);
            else
                _engine.ReplaceInPlace(tex.BundlePath, tex.PathId, imagePath, _backupDir);

            PackService.Upsert(_ws, new ModEntry
            {
                Bundle = Path.GetFileName(tex.BundlePath), PathId = tex.PathId,
                TextureName = tex.Name, Width = tex.Width, Height = tex.Height
            });
            PackService.SaveWorkspace(_folder, _ws);

            var pic = (PictureBox)card.Controls[0];
            pic.Image?.Dispose();
            pic.Image = BytesToImage(_engine.DecodePng(tex.BundlePath, tex.PathId, Sc(SrcThumb)));
            tex.Modded = true;
            card.SetFill(Theme.CardMod);
            _status.Text = $"✓ 已替换 {tex.Display ?? tex.Name}（原始已备份；可导出图包分享）";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"替换失败：{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            _status.Text = "替换失败";
        }
    }

    /// <summary>加载或构建当前目录的全量索引 (v2 导入按贴图名匹配用)。</summary>
    private async Task<GameIndex> GetFullIndexAsync()
    {
        var full = _indexSvc.Load(_folder, heroOnly: false);
        if (full == null)
        {
            _status.Text = "首次构建全量索引（约 10 秒，仅首次）…";
            string dir = _folder;
            full = await Task.Run(() => _indexSvc.Build(dir, null, heroOnly: false));
            try { _indexSvc.Save(full); } catch { }
        }
        return full;
    }

    private void ExportPack()
    {
        if (_folder == null) { _status.Text = "请先打开目录"; return; }
        if (_ws.Entries.Count == 0) { MessageBox.Show("当前还没有改过的贴图，无法导出图包。", "提示"); return; }

        using var dlg = new SaveFileDialog { Filter = $"吉星图包|*{PackService.PackExt}", FileName = "我的立绘包" + PackService.PackExt };
        if (dlg.ShowDialog() != DialogResult.OK) return;
        try
        {
            // 导出 v2 (按贴图名定位, 跨版本通用)
            var images = new List<NamedImage>();
            foreach (var e in _ws.Entries)
            {
                string bundlePath = Path.Combine(_folder, e.Bundle);
                if (!File.Exists(bundlePath) || string.IsNullOrEmpty(e.TextureName)) continue;
                try { images.Add(new NamedImage { TextureName = e.TextureName, Width = e.Width, Height = e.Height, Png = _engine.DecodePng(bundlePath, e.PathId, 0), Label = e.Label }); }
                catch { }
            }
            PackService.ExportV2(images, dlg.FileName, Path.GetFileNameWithoutExtension(dlg.FileName), "", "", "");
            _status.Text = $"✓ 已导出图包：{images.Count} 张 → {Path.GetFileName(dlg.FileName)}";
            MessageBox.Show($"图包导出成功！\n\n包含 {images.Count} 张改过的立绘\n{dlg.FileName}\n\n新版格式按贴图名定位，换版本/跨PC安卓都能用。", "导出成功");
        }
        catch (Exception ex) { MessageBox.Show($"导出失败：{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private async void ImportPack()
    {
        if (_folder == null) { MessageBox.Show("请先打开要应用到的目录。", "提示"); return; }
        using var dlg = new OpenFileDialog { Filter = $"吉星图包|*{PackService.PackExt}" };
        if (dlg.ShowDialog() != DialogResult.OK) return;
        try
        {
            _status.Text = "正在应用图包…";
            PackService.ImportResult res;
            if (PackService.PeekFormat(dlg.FileName) >= 2)
            {
                var full = await GetFullIndexAsync();
                res = PackService.ImportV2(dlg.FileName, full, _folder, _engine, _backupDir, _ws);
            }
            else
                res = PackService.Import(dlg.FileName, _folder, _engine, _backupDir, _ws);

            PackService.SaveWorkspace(_folder, _ws);
            string msg = $"图包「{res.PackName}」应用完成：\n\n成功替换 {res.Applied} 张";
            if (res.Missing > 0) msg += $"\n{res.Missing} 张未匹配（当前目录里没有对应立绘）";
            MessageBox.Show(msg, "导入完成");
            if (_selHeroBtn != null) _selHeroBtn.PerformClick(); else ShowFolderTextures();
        }
        catch (Exception ex) { MessageBox.Show($"导入失败：{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error); _status.Text = "导入失败"; }
    }

    private async void MigrateOldMods()
    {
        string oldRoot;
        using (var dlg = new FolderBrowserDialog { Description = "选择旧 mod 所在目录（会递归找所有 .bundle / __data）" })
        {
            if (dlg.ShowDialog() != DialogResult.OK) return;
            oldRoot = dlg.SelectedPath;
        }
        string outDir = Path.Combine(oldRoot, "_迁移结果");
        if (MessageBox.Show($"将把\n{oldRoot}\n下的旧 mod 迁移为最新版图包（按 PC/安卓 × 角色/卡图 分类），\n输出到：\n{outDir}\n\n继续？", "迁移旧 mod", MessageBoxButtons.OKCancel) != DialogResult.OK) return;

        try
        {
            var mig = new MigrationService();
            var rep = await Task.Run(() => mig.Migrate(oldRoot, outDir,
                (d, t, f) => { if (IsHandleCreated) BeginInvoke(() => _status.Text = $"迁移中… {d}/{t} {f}"); }));

            string groups = string.Join("\n", rep.GroupCounts.Select(k => $"  {k.Key}: {k.Value} 张"));
            _status.Text = $"✓ 迁移完成：{rep.Packs.Count} 个图包";
            MessageBox.Show($"迁移完成！\n\n处理 {rep.Files} 个旧文件，{rep.Textures} 张贴图\n\n{groups}\n\n图包已生成到：\n{outDir}\n\n这些图包按贴图名定位，永久有效。", "迁移完成");
            try { System.Diagnostics.Process.Start("explorer.exe", outDir); } catch { }
        }
        catch (Exception ex) { MessageBox.Show($"迁移失败：{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error); _status.Text = "迁移失败"; }
    }

    private void RestoreAll()
    {
        if (_folder == null || !Directory.Exists(_backupDir)) { _status.Text = "没有可还原的备份"; return; }
        if (MessageBox.Show("把所有改过的 bundle 还原为原始文件？", "确认", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        int n = 0;
        foreach (var b in Directory.GetFiles(_backupDir, "*.bundle"))
        {
            string target = Path.Combine(_folder, Path.GetFileName(b));
            if (File.Exists(target)) { File.Copy(b, target, true); n++; }
        }
        _ws = new ModManifest();
        PackService.SaveWorkspace(_folder, _ws);
        _status.Text = $"已还原 {n} 个 bundle";
        if (_selHeroBtn != null) _selHeroBtn.PerformClick(); else ShowFolderTextures();
    }

    private static string SafeFileName(string s)
    {
        string r = string.Concat((s ?? "tex").Split(Path.GetInvalidFileNameChars()));
        return string.IsNullOrEmpty(r) ? "tex" : r;
    }

    private void ExportSingle(TexRef tex)
    {
        using var dlg = new SaveFileDialog { Filter = "PNG 图片|*.png", FileName = SafeFileName(tex.Name) + ".png" };
        if (dlg.ShowDialog() != DialogResult.OK) return;
        try
        {
            File.WriteAllBytes(dlg.FileName, _engine.DecodePng(tex.BundlePath, tex.PathId, 0));
            _status.Text = "✓ 已导出 " + Path.GetFileName(dlg.FileName);
        }
        catch (Exception ex) { MessageBox.Show("导出失败: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void LocateBundle(TexRef tex)
    {
        if (!File.Exists(tex.BundlePath)) { _status.Text = "找不到对应 bundle 文件"; return; }
        try { System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{tex.BundlePath}\""); }
        catch (Exception ex) { _status.Text = "定位失败: " + ex.Message; }
    }

    private static Image BytesToImage(byte[] png)
    {
        using var ms = new MemoryStream(png);
        return Image.FromStream(ms);
    }
}
