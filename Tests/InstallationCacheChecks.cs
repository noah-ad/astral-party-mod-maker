using System.Diagnostics;
using System.Drawing;
using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using JixModMaker;

internal static class InstallationCacheChecks
{
    private const string Owner = "11111111111111111111111111111111";
    private const string Hash = "22222222222222222222222222222222";
    private const string OtherHash = "33333333333333333333333333333333";
    private const string Target = "UT_Hero_Bust_117_04_0";
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;

    public static void Run(Action<bool, string> check)
    {
        string root = Path.Combine(Path.GetTempPath(), "JixInstallationIndex-" + Guid.NewGuid().ToString("N"));
        string profile = Environment.GetEnvironmentVariable("USERPROFILE");
        string savedIndex = null;
        try
        {
            Environment.SetEnvironmentVariable("USERPROFILE", Path.Combine(root, "user"));
            string game = RuntimeLocatorChecks.Settings(root, "3.3.0");
            string installed = RuntimeBundleLocator.GameDataCache(game)!;
            string user = IndexService.HotCacheDir();
            string Wrapped(string cache, string owner, string hash)
            {
                string path = Path.Combine(cache, owner, hash, "__data");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, "cached fixture metadata; do not parse");
                File.WriteAllText(Path.Combine(Path.GetDirectoryName(path)!, "__info"), "access timestamp");
                return path;
            }
            string active = Wrapped(installed, Owner, Hash);
            string sfw = Wrapped(installed, Owner, OtherHash);
            string userCopy = Wrapped(user, Owner, Hash);
            string legacy = Path.Combine(game, "legacy.bundle");
            File.WriteAllText(legacy, "legacy mod bytes");
            string backup = Wrapped(Path.Combine(installed, "_原始备份"), Owner, Hash);
            string staging = Wrapped(Path.Combine(installed, "downloads"), Owner, Hash);
            string incomplete = Path.Combine(installed, OtherHash, Hash, "__data");
            Directory.CreateDirectory(Path.GetDirectoryName(incomplete)!);
            File.WriteAllText(incomplete, "not a complete cache entry");
            var bundles = IndexService.EnumerateBundles(game, true, false);
            check(bundles.Any(entry => entry.Path == active && entry.Source == "游戏安装缓存"), "installation-cache resource is discovered from the normal Steam resource directory");
            check(bundles.Any(entry => entry.Path == sfw), "new same-owner cache version is included alongside its sibling");
            check(bundles.Any(entry => entry.Path == userCopy) && bundles.Any(entry => entry.Path == legacy), "user cache and base mods remain indexed");
            check(bundles.Select(entry => entry.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() == bundles.Count &&
                bundles.Single(entry => entry.Path == userCopy).Name == Hash + ".bundle", "same hash in both caches retains separate targets and stable legacy user identity");
            check(!bundles.Any(entry => entry.Path == backup || entry.Path == staging || entry.Path == incomplete), "backups, download staging and cache entries without metadata are excluded");
            check(IndexService.EnumerateBundles(game, false, false).Single().Path == legacy, "standalone folder mode does not unexpectedly attach other game caches");
            check(IndexService.EnumerateBundles(installed, true, true).Count(entry => entry.Path == active) == 1, "opening the installation cache directly does not duplicate physical files");

            var service = new IndexService();
            var previous = new GameIndex { GameDir = game, HotDir = user, HeroOnly = false, IncludeHotCache = true,
                QuickSourceStamp = "before-installation-cache-fix", BuiltAt = "legacy metadata" };
            foreach (var bundle in bundles)
            {
                previous.BundleStamps[bundle.Name] = $"{bundle.Path}|{bundle.Length}|{bundle.LastWriteTicks}";
                previous.Items.Add(new TexIndexEntry { Bundle = bundle.Name, BundlePath = bundle.Path,
                    Name = bundle.Path == sfw ? Target + "_sfw" : Target, PathId = 1, Width = 1568, Height = 900, Source = "old source" });
            }
            savedIndex = IndexService.CachePath(game, false, true, false);
            service.Save(previous);
            check(service.Load(game, false, true, false) == null, "old fast-index fingerprint cannot hide the previously omitted installation cache");
            var index = service.Build(game, heroOnly: false, includeHotCache: true);
            check(index.GameCacheDir == installed && index.Items.Count == bundles.Count && index.Items.Any(entry => entry.Source == "游戏安装缓存"),
                "incremental build refreshes sources while reusing every unchanged invalid-byte fixture without opening it");
            service.Save(index);
            check(service.Load(game, false, true, false)?.Items.Count == bundles.Count, "subsequent startup reuses the combined cached index");
            check(index.FindTextureTargets(Target).Any(entry => entry.BundlePath == active) &&
                index.FindTextureTargets(Target + "_sfw").Single().BundlePath == sfw, "ordinary and SFW replacements resolve to their exact newly indexed packages");
            var workspace = new ModManifest { Entries = new() { new() { Bundle = "legacy.bundle", TextureName = Target, PathId = 1 } } };
            check(new ModificationCatalog(index, Path.Combine(root, "state.json")).CurrentEntries(workspace).Single().Bundle == "legacy.bundle",
                "adding cache copies does not move or lose a legacy base-package mod record");

            var sources = new[] { new ReplacementZip.Source(game, "基础资源"), new ReplacementZip.Source(user, "热更新缓存"), new ReplacementZip.Source(installed, "安装缓存") };
            string zipPath = Path.Combine(root, "mixed.zip");
            var parts = ReplacementZip.Plan(sources, new[] { active, userCopy, legacy }, zipPath);
            check(parts.Count == 3 && parts.Select(part => part.Root).Distinct().Count() == 3, "three-source direct ZIP export keeps installation cache separate from user cache and base resources");
            ReplacementZip.Export(parts);
            using (var archive = ZipFile.OpenRead(parts.Single(part => part.Root == installed).Destination))
                check(archive.GetEntry(Owner + "/" + Hash + "/__data") != null && archive.GetEntry(Owner + "/" + Hash + "/__info") != null,
                    "installation-cache ZIP preserves exact nested data and metadata paths");
            string before = IndexService.ComputeQuickSourceStamp(game, true, false);
            string added = Wrapped(installed, Hash, OtherHash);
            check(before != IndexService.ComputeQuickSourceStamp(game, true, false) && IndexService.EnumerateBundles(game, true, false).Any(entry => entry.Path == added),
                "new installation-cache owner invalidates fast startup and the per-root file list");
            before = IndexService.ComputeQuickSourceStamp(game, true, false);
            File.WriteAllText(Path.Combine(Directory.GetParent(installed)!.FullName, "catalog_3.3.0.json"), "changed catalog");
            check(before != IndexService.ComputeQuickSourceStamp(game, true, false), "installation catalog update invalidates fast startup without scanning asset payloads");
            File.Delete(Path.Combine(Directory.GetParent(installed)!.FullName, "catalog_3.3.0.json"));
            Ui(index, null, check);
            check(File.ReadAllText(legacy) == "legacy mod bytes" && File.ReadAllText(active) == "cached fixture metadata; do not parse",
                "indexing, browser checks and export never modify source game resources");
        }
        finally
        {
            Environment.SetEnvironmentVariable("USERPROFILE", profile);
            if (savedIndex != null)
            {
                if (File.Exists(savedIndex)) File.Delete(savedIndex);
                string directory = Path.GetDirectoryName(Path.GetFullPath(savedIndex))!;
                foreach (string path in Directory.EnumerateFiles(directory, "hot_data_files_*.json"))
                {
                    using var document = JsonDocument.Parse(File.ReadAllText(path));
                    string hot = document.RootElement.GetProperty("HotDir").GetString();
                    if (hot?.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) == true) File.Delete(path);
                }
            }
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    public static void Live(string game, string output, Action<bool, string> check)
    {
        output = Path.GetFullPath(output);
        if (Directory.Exists(output)) throw new IOException("Live audit output must be a new directory.");
        Directory.CreateDirectory(output);
        var timer = Stopwatch.StartNew();
        var service = new IndexService();
        var index = service.Load(game, false, true, false);
        if (index == null)
        {
            index = service.Build(game, heroOnly: false, includeHotCache: true);
            service.Save(index);
        }
        Console.WriteLine($"Combined index {timer.ElapsedMilliseconds} ms; {index.Items.Count} resources / {index.BundleCount} packages");
        var target = index.Items.Single(entry => entry.Name == Target && entry.Source == "游戏安装缓存");
        check(target.Width == 1568 && target.Height == 900 && File.Exists(target.BundlePath), "real current Hero117 skin04 bust is visible with its actual dimensions and installation path");
        check(index.Items.Any(entry => entry.Name == Target && entry.Source == "基础包") &&
            index.Items.Any(entry => entry.Name == Target + "_sfw" && entry.Source == "游戏安装缓存"), "real old modified base copy and previously missing SFW cache copy both remain visible");
        string original = SkillMovieEngine.Hash(target.BundlePath);
        File.WriteAllBytes(Path.Combine(output, "current-bust.png"), new ModEngine().DecodePng(target.BundlePath, target.PathId));
        timer.Restart();
        var reloaded = service.Load(game, false, true, false);
        check(reloaded?.Items.Any(entry => entry.BundlePath == target.BundlePath) == true, "real combined index is reused on the next load without rescanning bundles");
        Console.WriteLine($"Cached load {timer.ElapsedMilliseconds} ms");
        File.WriteAllText(Path.Combine(output, "sources.json"), JsonSerializer.Serialize(index.Items.Where(entry => entry.Name.StartsWith("UT_Hero_Bust_117_04", StringComparison.Ordinal)), new JsonSerializerOptions { WriteIndented = true }));
        Ui(index, output, check);
        check(SkillMovieEngine.Hash(target.BundlePath) == original, "real installation-cache resource remains byte-identical after browser verification");
    }

    private sealed class Browser : MainForm { protected override void OnShown(EventArgs e) { } }

    public static void Smoke(string source, string output, Action<bool, string> check)
    {
        source = Path.GetFullPath(source);
        output = Path.GetFullPath(output);
        if (Directory.Exists(output)) throw new IOException("Replacement smoke output must be a new directory.");
        string original = SkillMovieEngine.Hash(source);
        string game = RuntimeLocatorChecks.Settings(output, "3.3.0");
        string cache = RuntimeBundleLocator.GameDataCache(game)!;
        string current = Path.Combine(cache, Owner, Hash, "__data");
        Directory.CreateDirectory(Path.GetDirectoryName(current)!);
        File.Copy(source, current);
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(current)!, "__info"), "fixture cache metadata");
        var engine = new ModEngine();
        var asset = new IndexService().Build(cache, heroOnly: false, recursive: true).Items.Single(entry => entry.Name == Target);
        check(asset.Source == "游戏安装缓存" && asset.BundlePath == current, "real resource copy indexes as installation-cache artwork and retains the exact writable target");
        string image = Path.Combine(output, "replacement.png");
        using (var bitmap = new Bitmap(asset.Width, asset.Height))
        {
            using var drawing = Graphics.FromImage(bitmap);
            drawing.Clear(Color.CornflowerBlue);
            drawing.FillRectangle(Brushes.White, 0, 0, asset.Width / 2, asset.Height);
            bitmap.Save(image);
        }
        string backupDir = Path.Combine(game, "_原始备份");
        engine.ReplaceInPlace(current, asset.PathId, image, backupDir, asset.Bundle);
        var decoded = engine.DecodePng(current, asset.PathId);
        using (var stream = new MemoryStream(decoded))
        using (var bitmap = new Bitmap(stream))
            check(bitmap.GetPixel(bitmap.Width / 4, bitmap.Height / 2).ToArgb() == Color.White.ToArgb() &&
                bitmap.GetPixel(bitmap.Width * 3 / 4, bitmap.Height / 2).ToArgb() == Color.CornflowerBlue.ToArgb(),
                "static replacement on the newly indexed package roundtrips both image regions without changing target identity");
        string archive = Path.Combine(output, "current-cache.zip");
        ReplacementZip.Export(cache, new[] { current }, archive);
        using (var zip = ZipFile.OpenRead(archive))
            check(zip.GetEntry(Owner + "/" + Hash + "/__data") != null && zip.GetEntry(Owner + "/" + Hash + "/__info") != null,
                "newly indexed replacement exports as direct data and metadata files");
        check(engine.RestoreFromBackup(current, backupDir, asset.Bundle) && SkillMovieEngine.Hash(current) == original,
            "newly indexed cache package restores byte-for-byte from its own original backup");
        check(SkillMovieEngine.Hash(source) == original, "isolated replacement and restore leave the live game resource untouched");
    }

    private static void Ui(GameIndex index, string output, Action<bool, string> check)
    {
        Exception failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
                Application.EnableVisualStyles();
                foreach (int dpi in new[] { 96, 120, 144, 192, 240 })
                {
                    using var form = new Browser { StartPosition = FormStartPosition.Manual, Location = new(-5000, -5000) };
                    object Field(string name) => typeof(MainForm).GetField(name, Flags)!.GetValue(form)!;
                    object Call(string name, params object[] args) => typeof(MainForm).GetMethod(name, Flags)!.Invoke(form, args)!;
                    foreach (var pair in new (string, object)[] { ("_index", index), ("_folder", index.GameDir), ("_activePage", "browse"),
                        ("_selectedCategoryId", ResourceCategories.CharacterId), ("_selectedHeroGroupKey", "角色 117"), ("_selectedHeroSkin", "皮肤04") })
                        typeof(MainForm).GetField(pair.Item1, Flags)!.SetValue(form, pair.Item2);
                    form.Show();
                    form.MinimumSize = Size.Empty;
                    form.Size = new(1400, 900);
                    Call("ApplyDpiScale", dpi);
                    var args = new object[] { ResourceKinds.Texture, ResourceCategories.CharacterId, Target, 0, 48, 0 };
                    var rows = (List<TexIndexEntry>)Call("GetFullIndexRows", args);
                    check(rows.Count > 0 && rows[0].Source == "游戏安装缓存", $"same-name browser results prioritize installation cache at {dpi} DPI");
                    var asset = (TexRef)Call("ToTexRef", rows[0]);
                    var label = typeof(MainForm).GetMethod("CardText", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new[] { asset })!.ToString()!;
                    check(label.EndsWith("安装缓存", StringComparison.Ordinal), $"resource card exposes the omitted cache source at {dpi} DPI");
                    using var card = (RoundedCard)Call("MakeCard", asset, null);
                    var text = (Label)card.Controls[1];
                    check(text.Height >= text.Font.Height * 3 && card.ClientRectangle.Contains(card.Controls[2].Bounds),
                        $"three-line cache source label and card actions fit at {dpi} DPI");
                    Call("UpdateDetailText", asset);
                    check(((Label)Field("_detailMeta")).Text.Contains("游戏安装缓存") && ((Label)Field("_detailPath")).Text.Contains(asset.BundlePath),
                        $"details identify the installation cache and complete resource path at {dpi} DPI");
                    var old = index.Items.FirstOrDefault(entry => entry.Name == Target && entry.Source == "基础包");
                    if (old != null)
                    {
                        Call("UpdateDetailText", Call("ToTexRef", old));
                        check(((Label)Field("_detailHint")).Text.Contains("同名资源还存在缓存版本"), $"legacy base copy warns about cached alternatives at {dpi} DPI");
                    }
                    if (output != null && dpi is 96 or 144)
                    {
                        ((TextBox)Field("_searchBox")).Text = "UT_Hero_Bust_117_04";
                        Call("Navigate", "browse");
                        Call("SelectAsset", asset, null);
                        var preview = (PictureBox)Field("_preview");
                        var elapsed = Stopwatch.StartNew();
                        while (preview.Image == null && elapsed.Elapsed < TimeSpan.FromSeconds(15)) { Application.DoEvents(); Thread.Sleep(15); }
                        check(preview.Image != null, $"newly indexed real installation-cache bust renders in the browser at {dpi} DPI");
                        using var screenshot = new Bitmap(form.Width, form.Height);
                        form.DrawToBitmap(screenshot, new Rectangle(Point.Empty, screenshot.Size));
                        screenshot.Save(Path.Combine(output, $"browser-{dpi}.png"));
                    }
                    form.Close();
                    Application.DoEvents();
                }
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
