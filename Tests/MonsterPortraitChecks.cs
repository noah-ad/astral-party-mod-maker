using System.Drawing;
using System.Reflection;
using JixModMaker;

internal static class MonsterPortraitChecks
{
    public static void Run(Action<bool, string> check)
    {
        foreach (var (name, id, kind, skin, sfw) in new[]
        {
            ("UT_Monster_Card_1071", "1071", "Card", "原皮", false),
            ("UT_Monster_Card_1071.png", "1071", "Card", "原皮", false),
            ("UT_Monster_Card_1071_sfw", "1071", "Card", "原皮", true),
            ("UT_Monster_Bust_1071_0", "1071", "Bust", "原皮", false),
            ("UT_Monster_Bust_1071_0_sfw", "1071", "Bust", "原皮", true),
            ("UT_Monster_Card_1071_02", "1071", "Card", "皮肤02", false),
            ("UT_Monster_Card_1071_Max_sfw", "1071", "Card", "Max 皮肤", true),
            ("UT_Monster_ProfilePhoto_71", "71", "ProfilePhoto", "原皮", false),
            ("UT_Monster_Card_9999", "9999", "Card", "原皮", false)
        })
        {
            var parsed = NameParser.Parse(name);
            check(parsed.IsHero && parsed.IsMonster && parsed.GroupKey == "怪兽" && parsed.HeroId == id &&
                parsed.Kind == kind && parsed.Skin == skin && parsed.Sfw == sfw, "monster portrait naming: " + name);
            check(ResourceCategories.Categorize(ResourceKinds.Texture, name, 880, 1205) == ResourceCategories.CharacterId,
                "monster portrait is visible in character category: " + name);
            check(parsed.SubLabel.StartsWith(id + " · "), "monster tile labels retain their distinguishing ID: " + name);
        }
        check(NameParser.Parse("UT_Hero_Card_101").GroupKey == "角色 101" &&
            NameParser.Parse("UT_Hero_Card_1001").GroupKey == "怪兽", "existing hero and legacy monster groups are unchanged");
        check(!NameParser.Parse("UT_Monster_Card_").IsHero && !NameParser.Parse("UT_Monster_Card_frame").IsHero,
            "monster decorations and missing IDs are not character entries");
        check(!AnimatedPortraitPatch.IsSupportedTarget("UT_Monster_Card_1071"),
            "static monster indexing does not enable unverified runtime patches");
        check(!ResourceCategories.ForKind(ResourceKinds.Texture).Single(c => c.Id == ResourceCategories.CharacterId).Advanced,
            "monster portraits do not require showing advanced categories");

        string root = Path.Combine(Path.GetTempPath(), "JixMonsterIndex-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string cache = IndexService.CachePath(root, false, false, false);
        try
        {
            string bundle = Path.Combine(root, "monster.bundle");
            // Invalid bytes prove cached classification cannot be relying on reopening the resource.
            File.WriteAllText(bundle, "cached metadata only");
            string stamp = IndexService.ComputeSourceStamp(root, false, false);
            var service = new IndexService();
            string[] names = { "UT_Monster_Card_1071", "UT_Monster_Bust_1071_0", "UT_Monster_Card_1071_sfw", "UT_Hero_Card_101", "UT_HandCard_21002" };
            var full = new GameIndex { GameDir = root, HeroOnly = false, SourceStamp = stamp, BuiltAt = "before-monster-fix" };
            foreach (string name in names)
                full.Items.Add(new TexIndexEntry
                {
                    Bundle = "monster.bundle", BundlePath = bundle, Name = name, PathId = full.Items.Count + 1,
                    Width = 880, Height = 1205, Kind = ResourceKinds.Texture,
                    CategoryId = name.StartsWith("UT_Monster_") ? "other" : ResourceCategories.Categorize(ResourceKinds.Texture, name),
                    CategoryLabel = "old category"
                });
            full.Items.Add(new TexIndexEntry { Bundle = "monster.bundle", BundlePath = bundle, Name = "BGM", Kind = ResourceKinds.Audio, CategoryId = "music", CategoryLabel = "音乐" });
            full.BundleStamps["monster.bundle"] = $"{bundle}|{new FileInfo(bundle).Length}|{File.GetLastWriteTimeUtc(bundle).Ticks}";
            service.Save(full);
            var migrated = service.Load(root, false, false, false);
            check(migrated != null && migrated.BuiltAt == full.BuiltAt && migrated.Items.Count == full.Items.Count,
                "old full index is reused without rescanning monster bundles");
            CheckCategories(migrated, check, "full cache");
            check(migrated.TextureCategoryCounts[ResourceCategories.CharacterId] == 4 && migrated.TextureCategoryCounts[ResourceCategories.AllId] == 5,
                "cached category counts are recomputed without duplicate monsters");
            check(migrated.Items.Single(i => i.Name == "BGM").CategoryId == "music", "non-texture categories remain unchanged");
            var incremental = service.Build(root, heroOnly: false, includeHotCache: false);
            CheckCategories(incremental, check, "incremental reuse");
            check(incremental.Items.Count == full.Items.Count, "unchanged bundle metadata survives incremental classification");

            var light = new GameIndex
            {
                GameDir = root, HeroOnly = false, SourceStamp = stamp, Lightweight = true,
                LightweightTextureCount = names.Length,
                TextureNamesByBundle = new() { ["monster.bundle"] = names.ToList() },
                BundlePathsByName = new() { ["monster.bundle"] = bundle },
                TextureCategoryCounts = new() { ["other"] = 3 },
                TextureRefsByCategory = new()
            };
            service.Save(light);
            var lightLoaded = service.Load(root, false, false, false);
            check(lightLoaded != null && lightLoaded.Lightweight && lightLoaded.TextureCategoryCounts[ResourceCategories.CharacterId] == 4,
                "old lightweight index is reclassified without a bundle scan");
            check(lightLoaded.TextureRefsByCategory[ResourceCategories.CharacterId].Count(r => r.Name.StartsWith("UT_Monster_")) == 3,
                "lightweight character tree includes every cached monster variant");
            check(lightLoaded.FindTextureTarget("monster.bundle", names[0]).CategoryId == ResourceCategories.CharacterId &&
                lightLoaded.FindTextureTargets(names[0]).Single().BundlePath == bundle,
                "monster replacement lookup preserves bundle location and target name");
            check(lightLoaded.EnumerateLightweightTextures().Count(i => i.CategoryId == ResourceCategories.CharacterId) == 4,
                "lightweight browser enumeration agrees with category counts");
            service.Save(lightLoaded);
            var reloaded = service.Load(root, false, false, false);
            check(reloaded.TextureCategoryCounts[ResourceCategories.CharacterId] == 4,
                "repeated cache loads do not accumulate monster entries");
        }
        finally
        {
            if (File.Exists(cache)) File.Delete(cache);
            Directory.Delete(root, true);
        }
    }

    private static void CheckCategories(GameIndex index, Action<bool, string> check, string mode)
    {
        var monsters = index.Items.Where(i => i.Name.StartsWith("UT_Monster_")).ToArray();
        check(monsters.Length == 3 && monsters.All(i => i.CategoryId == ResourceCategories.CharacterId &&
            i.CategoryLabel == ResourceCategories.Label(ResourceKinds.Texture, ResourceCategories.CharacterId)),
            "monster categories and labels repaired in " + mode);
    }

    public static void Smoke(string source, string output, Action<bool, string> check)
    {
        source = Path.GetFullPath(source);
        output = Path.GetFullPath(output);
        if (Directory.Exists(output)) throw new IOException("Monster smoke output must be new.");
        Directory.CreateDirectory(output);
        string before = SkillMovieEngine.Hash(source);
        string copy = Path.Combine(output, Path.GetFileName(source));
        File.Copy(source, copy);
        var service = new IndexService();
        var index = service.Build(output, heroOnly: false, includeHotCache: false);
        var entry = index.Items.Single(i => i.Name == "UT_Monster_Card_1071");
        check(entry.CategoryId == ResourceCategories.CharacterId && NameParser.Parse(entry.Name).GroupKey == "怪兽",
            "real UT_Monster_Card_1071 is indexed in the visible monster group");
        var heroIndex = service.Build(output, heroOnly: true, includeHotCache: false);
        check(heroIndex.Items.Any(i => i.Name == entry.Name), "character-only scans include real monster portraits");
        string png = Path.Combine(output, "UT_Monster_Card_1071.png");
        File.WriteAllBytes(png, new ModEngine().DecodePng(copy, entry.PathId));
        using (var image = new Bitmap(png))
            check(image.Width == entry.Width && image.Height == entry.Height && image.Width > 0,
                $"real monster preview decodes at native size {entry.Width}x{entry.Height}");
        check(SkillMovieEngine.Hash(source) == before, "live game resource remains byte-identical");
        BrowserUi(index, output, check);
    }

    private sealed class BrowserForm : MainForm
    {
        protected override void OnShown(EventArgs e) { }
    }

    private static void BrowserUi(GameIndex index, string output, Action<bool, string> check)
    {
        Exception failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
                Application.EnableVisualStyles();
                Control.CheckForIllegalCrossThreadCalls = true;
                WindowsFormsSynchronizationContext.AutoInstall = false;
                using var context = new WindowsFormsSynchronizationContext();
                SynchronizationContext.SetSynchronizationContext(context);
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                foreach (float scale in new[] { 1f, 1.5f })
                {
                    using var form = new BrowserForm { StartPosition = FormStartPosition.Manual, Location = new(-4000, -4000) };
                    object Field(string name) => typeof(MainForm).GetField(name, flags).GetValue(form);
                    object Call(string name, params object[] args) => typeof(MainForm).GetMethod(name, flags).Invoke(form, args);
                    foreach (var (field, value) in new (string, object)[]
                    {
                        ("_index", index), ("_folder", output), ("_activePage", "browse"),
                        ("_selectedCategoryId", ResourceCategories.CharacterId), ("_selectedHeroGroupKey", "怪兽")
                    }) typeof(MainForm).GetField(field, flags).SetValue(form, value);
                    form.Show();
                    form.Scale(new SizeF(scale, scale));
                    form.MinimumSize = Size.Empty;
                    form.Size = new(1400, 900);
                    var args = new object[] { ResourceKinds.Texture, ResourceCategories.CharacterId, "1071", 0, 48, 0 };
                    var rows = (List<TexIndexEntry>)Call("GetFullIndexRows", args);
                    check(rows.Any(i => i.Name == "UT_Monster_Card_1071"), "monster category and search 1071 return the new portrait at " + scale);
                    var groups = (System.Collections.IList)Call("GetCharacterGroups");
                    check(groups.Count == 1, "monster portrait creates a character navigation group at " + scale);
                    Call("Navigate", "browse");
                    var asset = (TexRef)Call("ToTexRef", rows.Single(i => i.Name == "UT_Monster_Card_1071"));
                    check(asset.Display == "1071 · 卡面", "monster tile has a distinct label at " + scale);
                    Call("SelectAsset", asset, null);
                    var preview = (PictureBox)Field("_preview");
                    var timer = System.Diagnostics.Stopwatch.StartNew();
                    while (preview.Image == null && timer.Elapsed < TimeSpan.FromSeconds(15)) { Application.DoEvents(); Thread.Sleep(15); }
                    check(preview.Image != null, "real monster portrait renders in detail preview at " + scale);
                    check(((Button)Field("_replaceTextureBtn")).Enabled, "monster static replacement stays enabled at " + scale);
                    check(!((Button)Field("_replaceAnimationBtn")).Enabled, "monster selection does not expose an unsupported video patch at " + scale);
                    using var bitmap = new Bitmap(form.Width, form.Height);
                    form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                    bitmap.Save(Path.Combine(output, $"monster-browser-{scale}.png"));
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
