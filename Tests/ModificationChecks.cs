using System.Drawing;
using System.Reflection;
using System.Text.Json;
using JixModMaker;

internal static class ModificationChecks
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;

    private sealed class FixtureForm : MainForm
    {
        public FixtureForm(string folder) : base(folder) { }
        protected override void OnShown(EventArgs e) { }
    }

    public static void Run(Action<bool, string> check)
    {
        string root = Path.Combine(Path.GetTempPath(), "JixModification-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            check(PackService.LoadWorkspace(root).Entries.Count == 0, "missing workspace does not invent modifications");
            var time = DateTimeOffset.Parse("2026-08-01T10:20:00+08:00");
            var entry = new ModEntry { Bundle = "sub/test.bundle", PathId = -10, TextureName = "UT_Hero_Card_101", ModifiedAt = time, Label = "old label" };
            var workspace = new ModManifest { Entries = new() { entry } };
            PackService.SaveWorkspace(root, workspace);
            check(PackService.LoadWorkspace(root).Entries.Single().ModifiedAt == time, "workspace round trip preserves old modification times");
            check(PackService.Contains(workspace, "SUB\\TEST.BUNDLE", -90, entry.TextureName), "stable name survives changed path ID and Windows path aliases");
            check(!PackService.Contains(workspace, entry.Bundle, -10, "other texture"), "reused path ID cannot mark a different named texture as modified");
            check(PackService.MergeRecovered(workspace, new[] { new ModEntry { Bundle = "SUB\\TEST.BUNDLE", PathId = -90,
                TextureName = entry.TextureName, Width = 200, Height = 300 } }) == 1, "backup recovery updates stale path IDs without duplicating records");
            check(entry.ModifiedAt == time && entry.Label == "old label" && workspace.Entries.Count == 1, "recovery preserves the user's labels and timestamps");
            check(PackService.MergeRecovered(workspace, new[] { entry }) == 0, "repeating recovery does not rewrite an unchanged workspace");
            PackService.Upsert(workspace, new ModEntry { Bundle = "SUB\\TEST.BUNDLE", PathId = -100, TextureName = entry.TextureName });
            check(workspace.Entries.Count == 1 && workspace.Entries[0].PathId == -100, "replacement updates a stable record instead of accumulating old IDs");
            PackService.SaveWorkspace(root, workspace);
            string primary = PackService.WorkspacePath(root);
            check(File.Exists(primary + ".bak"), "atomic save retains the previous good workspace");
            File.WriteAllText(primary, "{broken");
            var recovered = PackService.LoadWorkspace(root);
            check(recovered.Entries.Single().PathId == -10 && recovered.LoadNotice != null, "broken primary loads the last good record with a visible notice");
            PackService.SaveWorkspace(root, recovered);
            check(File.ReadAllText(Directory.GetFiles(root, ".jixmod.json.unreadable-*.json").Single()) == "{broken",
                "saving a recovered workspace preserves the unreadable original");
            check(PackService.LoadWorkspace(root).Entries.Count == 1 && File.Exists(primary + ".bak"), "repair never replaces the good backup with malformed JSON");
            File.WriteAllText(primary, "{\"entries\":null}");
            check(PackService.LoadWorkspace(root).Entries.Count == 1, "null entry list cannot silently masquerade as an empty workspace");
            File.WriteAllText(primary, "{}");
            check(PackService.LoadWorkspace(root).LoadNotice != null, "missing entry list is reported rather than accepted as clean");
            File.Delete(primary + ".bak");
            check(PackService.LoadWorkspace(root).LoadNotice != null && File.ReadAllText(primary) == "{}", "unreadable records without backup stay intact");
            File.WriteAllText(primary, "{\"entries\":[{\"bundle\":\"test.bundle\",\"textureName\":\"legacy\",\"pathId\":\"-42\",\"width\":\"200\",\"modifiedAt\":\"\"}]}");
            var legacy = PackService.LoadWorkspace(root).Entries.Single();
            check(legacy.PathId == -42 && legacy.Width == 200 && legacy.ModifiedAt == null, "legacy casing, numeric strings and empty timestamps load correctly");
            string savedContent = File.ReadAllText(primary);
            using (var locked = new FileStream(primary, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                try { PackService.SaveWorkspace(root, new ModManifest()); throw new Exception("Locked workspace overwritten."); }
                catch (IOException) { check(File.ReadAllText(primary) == savedContent, "failed atomic save leaves the previous workspace untouched"); }
            }

            string target = Path.Combine(root, "test.bundle");
            string backup = Path.Combine(root, "_原始备份", "test.bundle");
            Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
            File.WriteAllText(target, "same resource");
            File.Copy(target, backup);
            var asset = new TexIndexEntry { Bundle = "test.bundle", BundlePath = target, PathId = -42, Name = "legacy", Format = "RGBA32" };
            var index = new GameIndex { GameDir = root, Items = new() { asset } };
            string cache = Path.Combine(root, "state.json");
            var catalog = new ModificationCatalog(index, cache);
            check(catalog.GetState(target, "legacy", false) == ModificationState.Unverified, "original RGBA32 format is not proof of a Mod");
            check(catalog.GetState(target, "legacy", true) == ModificationState.Recorded, "existing records remain visible before verification");
            var scan = catalog.Scan();
            check(scan.VerifiedBundles == 1 && scan.Recovered.Count == 0 && scan.FailedBundles == 0, "identical backup confirms original bytes without recovering fake Mods");
            check(catalog.GetState(target, "legacy", true) == ModificationState.Original, "backup proof overrides a stale modification record");
            var current = new ModManifest { Entries = new() { legacy, new() { Bundle = "removed.bundle", TextureName = "history" } } };
            check(catalog.CurrentEntries(current).Count == 0 && current.Entries.Count == 2, "active export excludes original and obsolete entries without deleting history");
            check(catalog.Find(new ModEntry { Bundle = "TEST.BUNDLE", TextureName = "legacy", PathId = -10 }) == asset,
                "catalog resolves signed changed IDs by stable name");
            check(index.FindTextureTarget("TEST.BUNDLE", "legacy", -10) == asset, "legacy index lookup also resolves changed IDs");
            check(index.FindTextureTarget("test.bundle", "wrong name", -42) == null, "index lookup cannot select an unrelated reused ID");
            var reopened = new ModificationCatalog(index, cache);
            check(reopened.GetState(target, "legacy", true) == ModificationState.Original && reopened.Scan().ReusedBundles == 1,
                "reopening reuses verified states without reparsing bundles");
            File.WriteAllText(target, "a different resource");
            check(reopened.GetState(target, "legacy", true) == ModificationState.Recorded, "changed target invalidates previous original proof");
            scan = reopened.Scan();
            check(scan.FailedBundles == 1 && reopened.GetState(target, "legacy", false) == ModificationState.Unverified,
                "unreadable changed resource remains unknown instead of being labelled original");
            File.Copy(backup, target, true);
            reopened.Scan();
            File.WriteAllText(backup, "changed baseline");
            check(reopened.GetState(target, "legacy", false) == ModificationState.Unverified, "changed baseline invalidates cached proof");
            File.Delete(backup);
            check(new ModificationCatalog(index, cache).GetState(target, "legacy", true) == ModificationState.Recorded,
                "missing baseline keeps the old Mod record, not a false original label");
            check(new ModificationCatalog(index, cache).CurrentEntries(current).Single().TextureName == "legacy",
                "recorded modifications without a baseline remain exportable");
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            try { catalog.Scan(token: cancelled.Token); throw new Exception("Cancelled scan completed."); }
            catch (OperationCanceledException) { check(true, "background verification can be cancelled"); }
            File.Copy(target, backup);
            string obstructed = Path.Combine(root, "not-a-directory");
            File.WriteAllText(obstructed, "fixture");
            var noCache = new ModificationCatalog(index, Path.Combine(obstructed, "state.json"));
            check(noCache.Scan().CacheNotice != null && noCache.GetState(target, "legacy", true) == ModificationState.Original,
                "unwritable optional cache does not discard valid verification results");

            string hot = Path.Combine(root, "hot");
            string wrapped = Path.Combine(hot, "hash", "revision", "__data");
            Directory.CreateDirectory(Path.GetDirectoryName(wrapped)!);
            File.WriteAllText(wrapped, "wrapped");
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(wrapped)!, "__info"), "metadata");
            string wrappedBackup = ResourceLocator.BackupPath(wrapped, Path.Combine(hot, "_原始备份"), "logical.bundle");
            Directory.CreateDirectory(Path.GetDirectoryName(wrappedBackup)!);
            File.Copy(wrapped, wrappedBackup);
            var hotIndex = new GameIndex { GameDir = root, HotDir = hot, Items = new() { new() { Bundle = "logical.bundle", BundlePath = wrapped, Name = "wrapped" } } };
            var wrappedCatalog = new ModificationCatalog(hotIndex, Path.Combine(root, "wrapped-state.json"));
            check(wrappedCatalog.FindBackup(wrapped, "logical.bundle") == wrappedBackup && wrappedCatalog.Scan().VerifiedBundles == 1,
                "wrapped hot resources find their exact nested original backup");
            File.Delete(wrappedBackup);
            File.WriteAllText(Path.Combine(root, "_原始备份", "__data"), "ambiguous backup");
            check(wrappedCatalog.FindBackup(wrapped, "logical.bundle") == null,
                "a generic __data backup cannot be mistaken for an unrelated hot resource");
            string externalBackup = Path.Combine(root, "_原始备份", "logical.bundle");
            File.Copy(wrapped, externalBackup);
            check(wrappedCatalog.FindBackup(wrapped, "logical.bundle") == externalBackup, "external hot resources retain compatibility with logical-name backups in the base folder");
            check(!Directory.EnumerateFiles(root, "*.tmp", SearchOption.AllDirectories).Any(), "workspace and state writes leave no temporary files");
        }
        finally { Directory.Delete(root, true); }
    }

    public static void RunUi(string root, Action<bool, string> check)
    {
        string folder = Path.Combine(root, "modification-ui");
        Directory.CreateDirectory(folder);
        string target = Path.Combine(folder, "current.bundle");
        File.WriteAllText(target, "fixture");
        var entry = new ModEntry { Bundle = "current.bundle", PathId = -10, TextureName = "UT_Hero_Card_101" };
        PackService.SaveWorkspace(folder, new ModManifest { Entries = new() { entry } });
        var index = new GameIndex { GameDir = folder, Items = new() { new() { Bundle = entry.Bundle, BundlePath = target,
            Name = entry.TextureName, PathId = -40, Width = 880, Height = 1205 } } };
        using var form = new FixtureForm(folder) { StartPosition = FormStartPosition.Manual, Location = new(-5000, -5000) };
        void Call(string method, params object[] arguments) => typeof(MainForm).GetMethod(method, Flags)!.Invoke(form, arguments);
        object Field(string name) => typeof(MainForm).GetField(name, Flags)!.GetValue(form)!;
        typeof(MainForm).GetField("_index", Flags)!.SetValue(form, index);
        typeof(MainForm).GetField("_modifications", Flags)!.SetValue(form, new ModificationCatalog(index, Path.Combine(folder, "state.json")));
        form.Show();
        Call("Navigate", "browse");
        var asset = (TexRef)typeof(MainForm).GetMethod("ToTexRef", Flags)!.Invoke(form, new object[] { index.Items[0] })!;
        check(asset.Modded && asset.Modification == ModificationState.Recorded, "UI keeps a named legacy Mod marked after its path ID changes");
        Call("UpdateDetailText", asset);
        var status = (Label)Field("_detailState");
        check(status.Text.Contains("有修改记录") && status.Bottom <= ((Button)Field("_replaceTextureBtn")).Parent.Top,
            "details expose recorded but unverified state above the replacement actions");
        var card = (RoundedCard)typeof(MainForm).GetMethod("MakeCard", Flags)!.Invoke(form, new object[] { asset, null })!;
        ((Panel)Field("_flow")).Controls.Add(card);
        int top = card.Top;
        string backups = Path.Combine(folder, "_原始备份");
        Directory.CreateDirectory(backups);
        File.Copy(target, Path.Combine(backups, entry.Bundle));
        ((ModificationCatalog)Field("_modifications")).Scan();
        Call("RefreshModificationViews");
        check(!asset.Modded && asset.Modification == ModificationState.Original && card.Controls[1].Text.Contains("备份一致"),
            "background proof refreshes visible cards without leaving the stale Mod marker");
        check(card.Top == top && !card.IsDisposed, "state refresh does not rebuild the browsing grid");
        check(((List<ModEntry>)typeof(MainForm).GetMethod("CurrentModEntries", Flags)!.Invoke(form, null)!).Count == 0,
            "export candidates use the same verified state as the browser");
        Call("VerifyModificationsAsync", false);
        string other = Path.Combine(folder, "other");
        Directory.CreateDirectory(other);
        PackService.SaveWorkspace(other, new ModManifest { Entries = new() { new() { Bundle = "other.bundle", TextureName = "other" } } });
        string otherRecord = File.ReadAllText(PackService.WorkspacePath(other));
        Call("SetFolder", other, false, false);
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (timer.ElapsedMilliseconds < 300) { Application.DoEvents(); Thread.Sleep(10); }
        check(File.ReadAllText(PackService.WorkspacePath(other)) == otherRecord && ((ModManifest)Field("_ws")).Entries.Single().TextureName == "other"
            && ((string)Field("_modVerificationInfo")).StartsWith("载入索引"), "cancelled old-folder verification cannot change the next workspace or its status");
        Call("SetFolder", folder, false, false);
        check(((ModManifest)Field("_ws")).Entries.Count == 1, "switching and reopening a folder preserves its old record");
        form.Close();
        Application.DoEvents();
    }

    public static void Smoke(string source, string output, Action<bool, string> check)
    {
        output = Path.GetFullPath(output);
        if (Directory.Exists(output)) throw new IOException("Smoke output must be a new directory.");
        Directory.CreateDirectory(output);
        var engine = new ModEngine();
        string originalHash = SkillMovieEngine.Hash(source);
        string current = Path.Combine(output, "fixture.bundle");
        string backups = Path.Combine(output, "_原始备份");
        Directory.CreateDirectory(backups);
        File.Copy(source, current);
        File.Copy(source, Path.Combine(backups, "fixture.bundle"));
        var headers = engine.ListTextureHeaders(current);
        check(headers.Count >= 2, "real fixture has multiple textures for asset-level discrimination");
        var chosen = headers.OrderBy(texture => (long)texture.Width * texture.Height).First();
        var index = new GameIndex { GameDir = output, Items = headers.Select(texture => new TexIndexEntry { Bundle = "fixture.bundle", BundlePath = current,
            PathId = texture.PathId, Name = texture.Name, Width = texture.Width, Height = texture.Height, Format = texture.Format }).ToList() };
        var before = engine.ReadTextureSignatures(current);
        using (var bitmap = new Bitmap(chosen.Width, chosen.Height))
        using (var graphics = Graphics.FromImage(bitmap))
        using (var stream = new MemoryStream())
        {
            graphics.Clear(Color.FromArgb(255, 197, 36, 136));
            bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
            engine.ReplaceInPlaceFromBytes(current, chosen.PathId, stream.ToArray(), backups, "fixture.bundle");
        }
        var signatures = engine.ReadTextureSignatures(current);
        check(signatures.Single(texture => texture.Name == chosen.Name).Hash != before.Single(texture => texture.Name == chosen.Name).Hash,
            "raw texture signatures see replacement data without PNG decoding");
        var catalog = new ModificationCatalog(index, Path.Combine(output, "state.json"));
        var scan = catalog.Scan();
        check(scan.FailedBundles == 0 && scan.Recovered.Count == 1 && scan.Recovered[0].TextureName == chosen.Name,
            "missing record is reconstructed only for the modified texture in a mixed bundle");
        check(headers.Where(texture => texture.Name != chosen.Name).All(texture => catalog.GetState(current, texture.Name, true) == ModificationState.Original),
            "untouched neighbours in a modified bundle are still identified as original");
        check(scan.Recovered[0].ModifiedAt == null, "recovery never invents a recent modification date");
        var workspace = new ModManifest();
        PackService.MergeRecovered(workspace, scan.Recovered);
        PackService.SaveWorkspace(output, workspace);
        var reopened = new ModificationCatalog(index, Path.Combine(output, "state.json"));
        check(reopened.GetState(current, chosen.Name, false) == ModificationState.Modified && reopened.Scan().ReusedBundles == 1,
            "verified Mod state survives reopening and cache reuse");
        check(reopened.CurrentEntries(PackService.LoadWorkspace(output)).Count == 1, "recovered Mod is available to selective export after reopening");
        File.Copy(Path.Combine(backups, "fixture.bundle"), current, true);
        reopened.Scan();
        check(reopened.CurrentEntries(workspace).Count == 0 && workspace.Entries.Count == 1, "restored real bundle drops out of active exports without losing history");
        check(SkillMovieEngine.Hash(source) == originalHash, "all texture tests leave the real game resource byte-identical");
    }

    public static void Audit(string indexPath, string output, bool repair, Action<bool, string> check)
    {
        output = Path.GetFullPath(output);
        Directory.CreateDirectory(output);
        var index = JsonSerializer.Deserialize<GameIndex>(File.ReadAllText(indexPath))!;
        var workspace = PackService.LoadWorkspace(index.GameDir);
        int oldCount = workspace.Entries.Count;
        var catalog = new ModificationCatalog(index, Path.Combine(output, "state.json"));
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var scan = catalog.Scan((done, total) => { if (done % 200 == 0 || done == total) Console.WriteLine($"AUDIT {done}/{total} {timer.ElapsedMilliseconds}ms"); });
        int changes = PackService.MergeRecovered(workspace, scan.Recovered);
        if (repair && changes > 0)
        {
            string saved = Path.Combine(output, "workspace-before.json");
            if (!File.Exists(saved) && File.Exists(PackService.WorkspacePath(index.GameDir))) File.Copy(PackService.WorkspacePath(index.GameDir), saved);
            PackService.SaveWorkspace(index.GameDir, workspace);
        }
        var entries = workspace.Entries.Select(entry => (Entry: entry, Asset: catalog.Find(entry))).ToList();
        var resolved = entries.Where(pair => pair.Asset != null).ToList();
        var states = resolved.GroupBy(pair => catalog.GetState(pair.Asset.BundlePath, pair.Asset.Name, true)).ToDictionary(group => group.Key.ToString(), group => group.Count());
        var active = catalog.CurrentEntries(workspace);
        var cached = new ModificationCatalog(index, Path.Combine(output, "state.json"));
        var second = cached.Scan();
        check(second.VerifiedBundles == 0 && second.ReusedBundles == scan.VerifiedBundles + scan.ReusedBundles,
            "real unchanged backup verification reuses cached results");
        check(workspace.Entries.Count >= oldCount, "real repair preserves every existing record");
        check(active.All(entry => catalog.GetState(catalog.Find(entry)!.BundlePath, entry.TextureName, true) != ModificationState.Original),
            "real active export excludes confirmed backup-identical resources");
        var formats = scan.Recovered.GroupBy(entry => index.Items.FirstOrDefault(asset => asset.Bundle == entry.Bundle && asset.Name == entry.TextureName)?.Format ?? "unknown")
            .ToDictionary(group => group.Key, group => group.Count());
        var report = new { index.GameDir, OldRecords = oldCount, Records = workspace.Entries.Count, UpdatedRecords = changes, Repaired = repair,
            ActiveRecords = active.Count, UnmatchedHistory = entries.Count - resolved.Count, States = states, ModifiedFormats = formats,
            ElapsedMs = timer.ElapsedMilliseconds, scan.VerifiedBundles, scan.ReusedBundles, scan.FailedBundles, scan.Errors, scan.CacheNotice };
        string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(output, "audit.json"), json);
        Console.WriteLine(json);
    }

    public static void LiveUi(string folder, string output, Action<bool, string> check)
    {
        output = Path.GetFullPath(output);
        Directory.CreateDirectory(output);
        Exception failure = null;
        var thread = new Thread(() =>
        {
            WindowsFormsSynchronizationContext.AutoInstall = false;
            using var context = new WindowsFormsSynchronizationContext();
            SynchronizationContext.SetSynchronizationContext(context);
            try
            {
                using var form = new FixtureForm(folder) { StartPosition = FormStartPosition.Manual, Location = new(-5000, -5000) };
                object Field(string name) => typeof(MainForm).GetField(name, Flags)!.GetValue(form)!;
                object Call(string method, params object[] arguments) => typeof(MainForm).GetMethod(method, Flags)!.Invoke(form, arguments)!;
                form.Show();
                Call("LoadIndexAsync", false, false);
                var timer = System.Diagnostics.Stopwatch.StartNew();
                while (((bool)Field("_loadingIndex") || Field("_modVerification") != null) && timer.Elapsed < TimeSpan.FromSeconds(45))
                { Application.DoEvents(); Thread.Sleep(10); }
                check(!(bool)Field("_loadingIndex") && Field("_modVerification") == null && Field("_modifications") != null,
                    "real main window loads the index and completes cached verification");
                var current = (List<ModEntry>)Call("CurrentModEntries");
                check(current.Count > 1000 && ((ModManifest)Field("_ws")).Entries.Count > current.Count,
                    "real main window distinguishes current Mods from preserved historical records");
                void Snapshot(string name)
                {
                    Application.DoEvents();
                    using var bitmap = new Bitmap(form.Width, form.Height);
                    form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                    bitmap.Save(Path.Combine(output, name + ".png"));
                }
                Call("Navigate", "pack");
                Snapshot("pack");
                Call("Navigate", "browse");
                var entry = current.First(entry => entry.TextureName.StartsWith("UT_Hero_Card_", StringComparison.Ordinal));
                var asset = (TexRef)Call("FindAssetForEntry", entry);
                Call("SelectAsset", asset, null);
                check(asset.Modification == ModificationState.Modified && ((Label)Field("_detailState")).Text.StartsWith("已修改"),
                    "real reopened Mod is identified as verified modified in the details panel");
                timer.Restart();
                while (timer.ElapsedMilliseconds < 1500) { Application.DoEvents(); Thread.Sleep(10); }
                Snapshot("browse");
                form.Close();
                Application.DoEvents();
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure != null) throw failure;
    }
}
