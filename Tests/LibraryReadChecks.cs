using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using JixModMaker;

internal static class LibraryReadChecks
{
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    private sealed class Studio : MainForm { protected override void OnShown(EventArgs e) { } }

    public static void Run(string cachePath, string directory, Action<bool, string> check)
    {
        string output = Directory.CreateDirectory(Path.GetFullPath(directory)).FullName;
        var settings = Directory.CreateDirectory(Path.Combine(output, "settings")).FullName;
        AppContext.SetData("JixModMaker.SettingsDirectory", settings);
        File.WriteAllText(Path.Combine(settings, "ui-language.txt"), "zh-CN");
        using var stream = File.OpenRead(cachePath);
        var installed = JsonSerializer.Deserialize<GameIndex>(stream)!;
        var edition = installed.GameDir.Contains("AstralParty_INT_Data", StringComparison.OrdinalIgnoreCase)
            ? GameEdition.International : GameEdition.China;
        var found = GameLocator.Find(CancellationToken.None, edition);
        check(found != null && RuntimeBundleLocator.SamePath(installed.GameDir, found.AaDir), "Steam discovery resolves the live indexed installation");
        var watch = Stopwatch.StartNew();
        var reopened = new IndexService().Load(found!.AaDir, false, true, false);
        check(reopened != null && reopened.Items.Count(item => item.Kind != ResourceKinds.SkillMovie) ==
            installed.Items.Count(item => item.Kind != ResourceKinds.SkillMovie),
            $"real warm index: {reopened?.AssetCount:N0} assets, {watch.ElapsedMilliseconds:N0} ms (index read only)");
        var root = Directory.CreateDirectory(Path.Combine(output, "fixture-" + Guid.NewGuid().ToString("N"))).FullName;
        string player = Path.Combine(root, "8vJXn6CN"), data = Path.Combine(player, "AstralParty_CN_Data");
        string aa = Path.Combine(data, "StreamingAssets", "aa");
        string platform = Directory.CreateDirectory(Path.Combine(aa, "StandaloneWindows64")).FullName;
        foreach (var selection in new[] { root, player, data, Path.GetDirectoryName(aa)!, aa, platform })
            check(GameLocator.ResolvePcResources(selection) == platform, "manual PC folder resolves: " + Path.GetFileName(selection));
        foreach (var name in new[] { "UT_AccountBackground_101_01", "UT_Item_Dice_72001" })
        {
            var source = installed.FindTextureTargets(name).First(x => File.Exists(x.BundlePath));
            File.Copy(source.BundlePath, Path.Combine(platform, name + ".bundle"));
        }
        var saved = new ModEntry { Bundle = "UT_Item_Dice_72001.bundle", TextureName = "UT_Item_Dice_72001" };
        PackService.SaveWorkspace(platform, new ModManifest { Entries = new() { saved } });
        string copy = Directory.CreateDirectory(Path.Combine(root, "mobile-copy")).FullName;
        string child = Directory.CreateDirectory(Path.Combine(copy, "parts")).FullName;
        File.Copy(Path.Combine(platform, saved.Bundle), Path.Combine(child, "one.bundle"));
        string backup = Directory.CreateDirectory(Path.Combine(child, "_原始备份")).FullName;
        File.Copy(Path.Combine(platform, saved.Bundle), Path.Combine(backup, "ignored.bundle"));
        check(ResourceLocator.EnumerateResourceFiles(copy).Count == 1, "recursive import omits nested backups");
        string wr = Directory.CreateDirectory(Path.Combine(root, "android", "AssetBundles")).FullName;
        string ver = Directory.CreateDirectory(Path.Combine(wr, new string('a', 32), new string('b', 32))).FullName;
        File.WriteAllText(Path.Combine(ver, "__info"), "");
        File.WriteAllText(Path.Combine(ver, "__data"), "test");
        check(ResourceLocator.NormalizeGameDirectory(ver) == wr && IndexService.EnumerateBundles(wr, false, true).Count == 1,
            "wrapped Android selection stays in its own tree");
        using (var cancel = new CancellationTokenSource())
        {
            bool stopped = false;
            try { new IndexService().Build(platform, (n, _) => { if (n == 0) cancel.Cancel(); }, false, token: cancel.Token); }
            catch (OperationCanceledException) { stopped = true; }
            check(stopped, "cold scanner honours cancellation before parsing the next bundle");
        }
        Exception failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
                Application.EnableVisualStyles();
                WindowsFormsSynchronizationContext.AutoInstall = false;
                using var context = new WindowsFormsSynchronizationContext();
                SynchronizationContext.SetSynchronizationContext(context);
                using var form = new Studio { StartPosition = FormStartPosition.Manual, Location = new Point(-5000, 0) };
                form.Show();
                form.ClientSize = new Size(1380, 820);
                object F(string name) => typeof(MainForm).GetField(name, Private)!.GetValue(form)!;
                void Set(string name, object value) => typeof(MainForm).GetField(name, Private)!.SetValue(form, value);
                void Call(string name, params object[] args) => typeof(MainForm).GetMethod(name, Private)!.Invoke(form, args);
                void Wait(Func<bool> ready)
                {
                    var timer = Stopwatch.StartNew();
                    do { Application.DoEvents(); Thread.Sleep(10); } while (!ready() && timer.ElapsedMilliseconds < 45000);
                    if (!ready()) throw new TimeoutException("Current stage: " + ((Control)F("_loadStrip")).Text);
                }
                void Finish() => Wait(() => !(bool)F("_loadingIndex") && F("_modVerification") == null);
                void Shot(string name, Control surface = null)
                {
                    surface ??= form;
                    using var bitmap = new Bitmap(surface.Width, surface.Height);
                    surface.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                    bitmap.Save(Path.Combine(output, name + ".png"));
                }
                var meter = (Control)F("_loadStrip");
                var stages = new List<string>();
                meter.TextChanged += (_, _) => stages.Add(meter.Text);
                Call("Navigate", "browse");
                Call("OpenSource", root, true);
                check((bool)F("_loadingIndex") && meter.Visible && ((Label)F("_browseLoading")).Text.Contains("正在"),
                    "library exposes progress before the first asynchronous result");
                Call("Navigate", "pack");
                check(((Label)F("_packLoading")).Visible && ((Label)F("_packLoading")).Text.Contains("正在"),
                    "projects has a pending state instead of reporting zero changes");
                Shot("loading-zh");
                Finish();
                check(((GameIndex)F("_index")).AssetCount >= 2 && ((List<ModEntry>)F("_displayMods")).Count == 1
                    && stages.Any(t => t.StartsWith("正在扫描资源包") && t.Contains('/')) && stages.Contains("正在保存索引")
                    && stages.Contains("正在匹配资源与修改记录"), "cold load reports measured scan and matches the saved record");
                stages.Clear();
                Call("OpenSource", data, true); Finish();
                check(stages.Contains("正在校验索引缓存") && !stages.Any(t => t.StartsWith("正在扫描资源包"))
                    && (string)F("_folder") == platform, "warm load through a different install level reuses the cache");
                Call("OpenSource", root, true);
                Call("OpenSource", copy, false);
                Finish();
                check(((GameIndex)F("_index")).Items.All(x => x.BundlePath.StartsWith(child))
                    && ((ModManifest)F("_ws")).Entries.Count == 0 && !(bool)F("_includeHotCache"),
                    "superseded work cannot publish index, records or PC caches into a resource folder");
                string missing = Path.Combine(root, "not-yet-created");
                Call("OpenSource", missing, false); Finish();
                check(meter.Text == "资源库未能打开", "missing folder is a retryable failure");
                Directory.CreateDirectory(missing);
                Call("LoadIndexAsync", false, false); Finish();
                check(meter.Text == "目录内没有可浏览资源", "retry on a valid empty folder is distinct from failure");

                // Render the full live installation without starting recovery or writing any game records.
                Set("_folder", installed.GameDir);
                Set("_backupDir", Path.Combine(installed.GameDir, "_原始备份"));
                Set("_index", installed);
                Set("_ws", PackService.LoadWorkspace(installed.GameDir));
                Set("_modifications", new ModificationCatalog(installed));
                Call("UpdateDashboard");
                Call("LoadWorkshopArt", installed, installed.GameDir);
                Wait(() => F("_diceArt") is Image && F("_artwork") is Image);
                var ready = Enum.Parse(meter.GetType().GetProperty("Stage")!.PropertyType, "Ready");
                Call("SetLoadStage", ready, 0, 0);
                Call("Navigate", "browse");
                Call("SelectCategory", "dice", null!, null!);
                var grid = (Panel)F("_flow");
                var card = grid.Controls.OfType<RoundedCard>().First(c => c.Tag is TexRef);
                var first = (TexRef)card.Tag;
                Wait(() => card.Controls[0].Controls.OfType<PictureBox>().Single().Image != null);
                for (int i = 0; i < 3; i++)
                {
                    grid.AutoScrollPosition = new Point(0, 70);
                    Call("Navigate", "tools"); Call("Navigate", "pack"); Call("Navigate", "browse");
                }
                grid.AutoScrollPosition = Point.Empty;
                ((Panel)F("_categoryList")).ScrollControlIntoView((Button)F("_selectedCategoryButton"));
                Call("SelectAsset", first, null!);
                Wait(() => ((PictureBox)F("_preview")).Image != null);
                check(ReferenceEquals(F("_selectedAsset"), first) && !card.IsDisposed && grid.Left == 0,
                    "repeated live page switches keep tiles, selected preview and a stationary viewport");
                Shot("library-zh");
                var picker = (Button)F("_kindBox");
                picker.PerformClick();
                var menu = (ContextMenuStrip)picker.GetType().GetField("_menu", Private)!.GetValue(picker)!;
                check(menu.Visible && menu.Items.Count == ResourceKinds.All.Length, "resource picker opens all five types");
                Shot("resource-menu", menu);
                ((ToolStripMenuItem)menu.Items[1]).PerformClick();
                menu.Close();
                check(picker.Text == "音频", "themed menu changes the actual resource type");
                picker.PerformClick(); ((ToolStripMenuItem)menu.Items[0]).PerformClick(); menu.Close();
                Call("SelectCategory", "dice", null!, null!);
                Call("SwitchLanguage");
                check(picker.Text == "Textures" || picker.Text == "Texture", "type selector follows the language change");
                Shot("library-en");
                var tab = (Button)F("_replaceTextureBtn");
                using var canvas = new Bitmap(tab.Width, tab.Height);
                using var g = Graphics.FromImage(canvas);
                foreach (string transition in new[] { "OnMouseEnter", "OnMouseLeave" })
                {
                    typeof(RoundButton).GetMethod(transition, Private)!.Invoke(tab, new object[] { EventArgs.Empty });
                    g.Clear(Color.Magenta);
                    typeof(RoundButton).GetMethod("OnPaint", Private)!.Invoke(tab, new object[] { new PaintEventArgs(g, tab.ClientRectangle) });
                    check(canvas.GetPixel(0, 0).ToArgb() == Theme.Bar.ToArgb() && canvas.GetPixel(tab.Width - 1, 0).ToArgb() == Theme.Bar.ToArgb(),
                        $"foreground-only paint replaces dirty pixels on {transition}: {canvas.GetPixel(0, 0)} / {canvas.GetPixel(tab.Width - 1, 0)}, expected {Theme.Bar}");
                }
                form.Close();
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
