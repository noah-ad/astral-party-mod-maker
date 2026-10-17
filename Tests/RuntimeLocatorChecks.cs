using System.Text;
using System.Text.Json;
using System.IO.Compression;
using JixModMaker;

internal static class RuntimeLocatorChecks
{
    private const string Bundle = "11111111111111111111111111111111";
    private const string OldHash = "22222222222222222222222222222222";
    private const string NewHash = "33333333333333333333333333333333";

    internal static void Catalog(string path, string bundle, string hash)
    {
        static byte[] Bytes(Action<BinaryWriter> write)
        {
            using var buffer = new MemoryStream();
            using (var writer = new BinaryWriter(buffer, Encoding.UTF8, true)) write(writer);
            return buffer.ToArray();
        }
        string name = "AstralParty.Runtime.dll";
        byte[] keys = Bytes(writer => { writer.Write((byte)0); writer.Write(name.Length); writer.Write(Encoding.UTF8.GetBytes(name)); });
        byte[] buckets = Bytes(writer =>
        {
            writer.Write(2);
            writer.Write(0); writer.Write(1); writer.Write(0);
            writer.Write(-1); writer.Write(1); writer.Write(1);
        });
        byte[] entries = Bytes(writer =>
        {
            writer.Write(2);
            foreach (int value in new[] { 0, 0, 1, 0, -1, 0, 0, 1, 0, -1, 0, 0, 0, 1 }) writer.Write(value);
        });
        byte[] extra = Bytes(writer =>
        {
            writer.Write((byte)7);
            string assembly = "Unity.ResourceManager", type = "UnityEngine.ResourceManagement.ResourceProviders.AssetBundleRequestOptions";
            writer.Write((byte)assembly.Length); writer.Write(Encoding.UTF8.GetBytes(assembly));
            writer.Write((byte)type.Length); writer.Write(Encoding.UTF8.GetBytes(type));
            byte[] json = Encoding.Unicode.GetBytes(JsonSerializer.Serialize(new { m_BundleName = bundle, m_Hash = hash, m_Crc = 0 }));
            writer.Write(json.Length); writer.Write(json);
        });
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            m_KeyDataString = Convert.ToBase64String(keys), m_BucketDataString = Convert.ToBase64String(buckets),
            m_EntryDataString = Convert.ToBase64String(entries), m_ExtraDataString = Convert.ToBase64String(extra),
            m_InternalIds = new[] { "runtime", "{App.WebServerConfig.Path}\\" + hash + ".bundle" }
        }));
    }

    internal static string Settings(string root, string version)
    {
        string game = Path.Combine(root, "game", "AstralParty_CN_Data", "StreamingAssets", "aa", "StandaloneWindows64");
        Directory.CreateDirectory(game);
        File.WriteAllText(Path.Combine(Directory.GetParent(game)!.FullName, "settings.json"), JsonSerializer.Serialize(new
        {
            m_CatalogLocations = new[] { new { m_Keys = new[] { "AddressablesMainContentCatalogCacheHash" },
                m_InternalId = "{UnityEngine.Application.persistentDataPath}/com.unity.addressables/catalog_" + version + ".hash" } }
        }));
        return game;
    }

    public static void Run(Action<bool, string> check)
    {
        string root = Path.Combine(Path.GetTempPath(), "JixRuntimeLocator-" + Guid.NewGuid().ToString("N"));
        string cache = Path.Combine(root, "addressables", "AssetBundles");
        Directory.CreateDirectory(cache);
        try
        {
            string oldCatalog = Path.Combine(Directory.GetParent(cache)!.FullName, "catalog_3.2.0.json");
            string currentCatalog = Path.Combine(Directory.GetParent(cache)!.FullName, "catalog_3.3.0.json");
            Catalog(oldCatalog, Bundle, OldHash);
            Catalog(currentCatalog, Bundle, NewHash);
            string game = Settings(root, "3.3.0");
            string old = Path.Combine(cache, Bundle, OldHash, "__data"), current = Path.Combine(cache, Bundle, NewHash, "__data");
            Directory.CreateDirectory(Path.GetDirectoryName(old)!);
            Directory.CreateDirectory(Path.GetDirectoryName(current)!);
            File.WriteAllText(old, "old runtime");
            File.WriteAllText(current, "current runtime");
            File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddHours(2));
            File.SetLastWriteTimeUtc(oldCatalog, DateTime.UtcNow.AddHours(2));
            var target = RuntimeBundleLocator.Resolve(cache, game);
            check(target.Path == current && target.Catalog == currentCatalog, "active game settings win over recently modified old runtime and old catalog");
            check(AnimatedPortraitPatch.FindBundles(cache, CancellationToken.None, runtimeOnly: true, gameDirectory: game).Runtime == current,
                "dynamic discovery resolves the active cache path without scanning or a Unity 2021 restriction");
            var receipt = new PortraitReplacement.Receipt("UT_HandCard_20001", "JixVideo_UT_HandCard_20001", "input.mp4", false, DateTimeOffset.Now,
                null, null, null, null, new() { [Path.GetRelativePath(cache, old).Replace('\\', '/')] = SkillMovieEngine.Hash(old) },
                Mode: IndependentVideoPatch.Mode);
            check(PortraitReplacement.IsInstalled(cache, receipt) && !PortraitReplacement.CheckCurrentBinding(cache, receipt, game).Current,
                "matching old file hashes cannot masquerade as an active in-game binding");
            check(PortraitReplacement.CheckCurrentBinding(cache, receipt, game).Message.Contains("非当前版本"), "stale binding explains why the game still shows a static card");
            receipt.Hashes.Clear();
            receipt.Hashes[Path.GetRelativePath(cache, current).Replace('\\', '/')] = SkillMovieEngine.Hash(current);
            check(PortraitReplacement.CheckCurrentBinding(cache, receipt, game).Current, "binding status accepts a hash-verified current catalog target");
            Directory.CreateDirectory(Path.Combine(cache, IndependentVideoPatch.Folder));
            string disabled = Path.Combine(cache, IndependentVideoPatch.Folder, "disabled");
            File.WriteAllText(disabled, "");
            check(!PortraitReplacement.CheckCurrentBinding(cache, receipt, game).Current && PortraitReplacement.CheckCurrentBinding(cache, receipt, game).Message.Contains("暂停"),
                "disabled independent playback is reported instead of falsely showing success");
            File.Delete(disabled);
            string untouched = File.ReadAllText(old);
            try
            {
                IndependentVideoReplacement.Build(new(cache, old, null, "UT_HandCard_20001", "missing.usm", "", true, game), null, Path.Combine(root, "rejected"));
                throw new Exception("Stale DLL accepted.");
            }
            catch (IOException)
            {
                check(File.ReadAllText(old) == untouched && !Directory.Exists(Path.Combine(root, "rejected")), "direct API rejects a stale runtime before conversion, backups or game writes");
            }
            File.Delete(current);
            try { RuntimeBundleLocator.Resolve(cache, game); throw new Exception("Missing current runtime replaced by old one."); }
            catch (FileNotFoundException ex) { check(ex.Message.Contains("不会写入旧包"), "missing active runtime never falls back to an installed older version"); }
            check(!PortraitReplacement.CheckCurrentBinding(cache, receipt, game).Current, "game update invalidates the displayed dynamic binding");
            File.WriteAllText(current, "current runtime");
            File.Delete(currentCatalog);
            try { RuntimeBundleLocator.Resolve(cache, game); throw new Exception("Missing current catalog replaced by old one."); }
            catch (FileNotFoundException) { check(true, "missing required catalog never falls back to an older catalog"); }
            Catalog(currentCatalog, Bundle, NewHash);
            try { RuntimeBundleLocator.Resolve(cache); throw new Exception("Ambiguous catalogs accepted without game settings."); }
            catch (InvalidDataException) { check(true, "multiple catalog versions require an explicit game context"); }
            Catalog(currentCatalog, "../unsafe", NewHash);
            try { RuntimeBundleLocator.Resolve(cache, game); throw new Exception("Unsafe bundle path accepted."); }
            catch (InvalidDataException) { check(true, "catalog path injection is rejected"); }
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            try { AnimatedPortraitPatch.FindBundles(cache, cancelled.Token, runtimeOnly: true, gameDirectory: game); throw new Exception("Cancelled locator completed."); }
            catch (OperationCanceledException) { check(true, "runtime lookup honors cancellation before opening catalogs"); }
            DataCacheLayout(Path.Combine(root, "split-cache"), check);
        }
        finally { Directory.Delete(root, true); }
    }

    public static void LiveAudit(string game, string cache, Action<bool, string> check)
    {
        var target = RuntimeBundleLocator.Resolve(cache, game);
        check(target != null && File.Exists(target.Path), "real game settings and catalog resolve an existing version-specific runtime");
        check(RuntimeBundleLocator.Resolve(target.CacheRoot, game).Path == target.Path,
            "actual installation root can revalidate against the user's current catalog without scanning");
        check(PortraitReplacement.FindRoot(cache, game) == target.CacheRoot || PortraitReplacement.ReadReceipt(cache) != null,
            "actual UI uses the active cache or preserves a legacy restoration record");
        Console.WriteLine(JsonSerializer.Serialize(target));
    }

    private static void DataCacheLayout(string root, Action<bool, string> check)
    {
        string profile = Environment.GetEnvironmentVariable("USERPROFILE");
        try
        {
            Environment.SetEnvironmentVariable("USERPROFILE", Path.Combine(root, "user"));
            string cache = IndexService.HotCacheDir(), game = Settings(root, "3.3.0");
            string dataCache = RuntimeBundleLocator.GameDataCache(game)!;
            File.WriteAllText(Path.Combine(root, "game", "AstralParty_CN.exe"), "local player fixture");
            Directory.CreateDirectory(Directory.GetParent(cache)!.FullName);
            string catalog = Path.Combine(Directory.GetParent(cache)!.FullName, "catalog_3.3.0.json");
            Catalog(catalog, Bundle, NewHash);
            string old = Path.Combine(cache, Bundle, OldHash, "__data");
            string current = Path.Combine(dataCache, Bundle, NewHash, "__data");
            Directory.CreateDirectory(Path.GetDirectoryName(old)!);
            Directory.CreateDirectory(Path.GetDirectoryName(current)!);
            File.WriteAllText(old, "old user runtime");
            File.WriteAllText(current, "current installation runtime");
            var target = RuntimeBundleLocator.Resolve(cache, game);
            check(target.Path == current && target.CacheRoot == dataCache,
                "current runtime in game Data cache is found while user cache contains only an old version");
            check(RuntimeBundleLocator.Resolve(dataCache, game).Catalog == catalog,
                "game Data cache revalidates using the real player's separate user catalog");
            check(RuntimeBundleLocator.Resolve(dataCache).Path == current,
                "installation cache infers its own game settings when no explicit game context is supplied");
            check(RuntimeBundleLocator.Resolve(dataCache + Path.DirectorySeparatorChar, game).Path == current &&
                RuntimeBundleLocator.UsesDataPath(dataCache + Path.DirectorySeparatorChar, game),
                "trailing cache separators cannot create duplicate targets or select the wrong video storage");
            check(AnimatedPortraitPatch.FindBundles(cache, CancellationToken.None, runtimeOnly: true, gameDirectory: game).Runtime == current,
                "dynamic discovery probes the catalog's exact target across both caches");
            check(PortraitReplacement.FindRoot(cache, game) == dataCache && RuntimeBundleLocator.UsesDataPath(dataCache, game),
                "UI and video path choose the installation cache together");
            string relative = Path.GetRelativePath(dataCache, current).Replace('\\', '/');
            var receipt = new PortraitReplacement.Receipt("UT_HandCard_21001", "JixVideo_UT_HandCard_21001", "video.mp4", false,
                DateTimeOffset.Now, null, null, null, null, new() { [relative] = SkillMovieEngine.Hash(current) },
                Mode: IndependentVideoPatch.Mode, UseDataPath: true);
            check(PortraitReplacement.CheckCurrentBinding(dataCache, receipt, game).Current,
                "installation cache binding validates both active runtime and dataPath video storage");
            check(!PortraitReplacement.CheckCurrentBinding(dataCache, receipt with { UseDataPath = false }, game).Current,
                "matching runtime bytes do not hide an incorrect persistentDataPath video binding");
            Directory.CreateDirectory(Path.GetDirectoryName(PortraitReplacementState(cache))!);
            File.WriteAllText(PortraitReplacementState(cache), JsonSerializer.Serialize(receipt));
            check(PortraitReplacement.FindRoot(cache, game) == cache,
                "legacy user-cache replacement keeps its restoration entry instead of disappearing");
            try
            {
                IndependentVideoReplacement.Build(new(dataCache, current, null, receipt.Texture, "missing.usm", "", true, game), null, Path.Combine(root, "legacy-conflict"));
                throw new Exception("Legacy record was silently abandoned.");
            }
            catch (IOException ex) { check(ex.Message.Contains("另一处缓存") && !Directory.Exists(Path.Combine(root, "legacy-conflict")),
                "direct API refuses to abandon another cache's existing dynamic restoration record"); }
            File.Delete(PortraitReplacementState(cache));
            try
            {
                IndependentVideoReplacement.Build(new(cache, current, null, receipt.Texture, "missing.usm", "", true, game), null, Path.Combine(root, "wrong-root"));
                throw new Exception("Mismatched installation root accepted.");
            }
            catch (IOException) { check(!Directory.Exists(Path.Combine(root, "wrong-root")), "direct API rejects a runtime and video installation root mismatch before creating backups"); }
            string duplicate = Path.Combine(cache, Bundle, NewHash, "__data");
            Directory.CreateDirectory(Path.GetDirectoryName(duplicate)!);
            File.Copy(current, duplicate);
            try { RuntimeBundleLocator.Resolve(cache, game); throw new Exception("Ambiguous current runtime accepted."); }
            catch (InvalidDataException ex) { check(ex.Message.Contains("均存在"), "duplicate current runtime across caches is blocked even when bytes match"); }
            File.Delete(duplicate);
            string otherCatalog = Path.Combine(Directory.GetParent(dataCache)!.FullName, "catalog_3.3.0.json");
            Catalog(otherCatalog, Bundle, OldHash);
            try { RuntimeBundleLocator.Resolve(dataCache, game); throw new Exception("Conflicting catalogs accepted."); }
            catch (InvalidDataException ex) { check(ex.Message.Contains("内容不一致"), "conflicting same-version catalogs cannot silently select a stale runtime"); }
            check(File.ReadAllText(old) == "old user runtime" && File.ReadAllText(current) == "current installation runtime",
                "cross-cache discovery and rejected writes preserve every game resource");
        }
        finally { Environment.SetEnvironmentVariable("USERPROFILE", profile); }
    }

    private static string PortraitReplacementState(string root) => Path.Combine(root, "_原始备份", "动态立绘", "current.json");

    public static void DataBundleSmoke(string sourceRuntime, string usm, string destination, Action<bool, string> check)
    {
        destination = Path.GetFullPath(destination);
        if (Directory.Exists(destination)) throw new IOException("Runtime smoke output must be a new directory.");
        string game = Settings(destination, "3.3.0");
        string cache = Path.Combine(destination, "user-cache", "AssetBundles");
        string dataCache = RuntimeBundleLocator.GameDataCache(game)!;
        string old = Path.Combine(cache, Bundle, OldHash, "__data"), current = Path.Combine(dataCache, Bundle, NewHash, "__data");
        Directory.CreateDirectory(Path.GetDirectoryName(old)!);
        Directory.CreateDirectory(Path.GetDirectoryName(current)!);
        string originalHash = SkillMovieEngine.Hash(sourceRuntime);
        File.Copy(sourceRuntime, old); File.Copy(sourceRuntime, current);
        Catalog(Path.Combine(Directory.GetParent(game)!.FullName, "catalog_3.3.0.json"), Bundle, NewHash);
        var found = RuntimeBundleLocator.Resolve(cache, game);
        check(found.Path == current && found.CacheRoot == dataCache, "real current bundle copy resolves to installation cache, never old user cache");
        var request = new AnimatedPortraitPatch.Request(found.CacheRoot, found.Path, null, "UT_HandCard_21001", usm, "", true, game);
        var first = PortraitReplacement.Apply(request, sourceName: "card.mp4");
        check(first.UseDataPath && PortraitReplacement.CheckCurrentBinding(dataCache, first, game).Current,
            "real hand-card patch records and validates dataPath video playback");
        var second = PortraitReplacement.Apply(request with { TextureName = "UT_Hero_Card_101" }, sourceName: "portrait.mp4");
        var third = PortraitReplacement.Apply(request with { TextureName = "UT_Event_1001" }, sourceName: "event.mp4");
        check(third.Portraits.Count == 3 && third.UseDataPath && PortraitReplacement.CheckCurrentBinding(dataCache, third, game).Current,
            "installation-cache hand card, portrait and event coexist after clean-baseline rebuilds");
        check(SkillMovieEngine.Hash(old) == originalHash && SkillMovieEngine.Hash(current) != originalHash,
            "installation-cache replacement leaves the inactive user-cache runtime untouched");
        string extraction = Path.Combine(destination, "patched-texts");
        ExtractRuntime(current, extraction);
        using (var module = Mono.Cecil.ModuleDefinition.ReadModule(extraction))
        {
            var helper = module.GetType("UI.CriMovieManager").Methods.Single(method => method.Name == "JixIndependentVideoPath");
            check(helper.Body.Instructions.Any(i => i.Operand is Mono.Cecil.MethodReference m && m.Name == "get_dataPath") &&
                !helper.Body.Instructions.Any(i => i.Operand is Mono.Cecil.MethodReference m && m.Name == "get_persistentDataPath"),
                "re-read packaged current DLL reads video from the same dataPath cache as the generated files");
        }
        using (var zip = ZipFile.OpenRead(third.ReplacementZip))
            check(zip.Entries.Any(entry => entry.FullName == Bundle + "/" + NewHash + "/__data") &&
                third.Portraits.Keys.All(texture => zip.GetEntry(IndependentVideoPatch.RelativeFile(texture)) != null) &&
                !zip.Entries.Any(entry => entry.FullName.Contains(OldHash)), "direct ZIP contains only the current runtime and independent video files in one cache layout");
        PortraitReplacement.Restore(dataCache);
        check(SkillMovieEngine.Hash(current) == originalHash && SkillMovieEngine.Hash(old) == originalHash &&
            third.Portraits.Keys.All(texture => !File.Exists(Path.Combine(dataCache, IndependentVideoPatch.RelativeFile(texture)))) &&
            PortraitReplacement.ReadReceipt(dataCache) == null, "installation-cache restore returns exact originals and removes only its added videos and active record");
        check(SkillMovieEngine.Hash(sourceRuntime) == originalHash, "data-cache smoke never writes to the real current game runtime");
    }

    private static void ExtractRuntime(string bundle, string destination)
    {
        var manager = new AssetsTools.NET.Extra.AssetsManager();
        try
        {
            var file = manager.LoadAssetsFileFromBundle(manager.LoadBundleFile(bundle), 0, false);
            var reader = file.file.Reader;
            foreach (var info in file.file.GetAssetsOfType(AssetsTools.NET.Extra.AssetClassID.TextAsset))
            {
                reader.Position = info.GetAbsoluteByteStart(file.file);
                string name = reader.ReadCountStringInt32();
                reader.Align();
                int length = reader.ReadInt32();
                if (name == "AstralParty.Runtime.dll") { File.WriteAllBytes(destination, reader.ReadBytes(length)); return; }
            }
            throw new InvalidDataException("Missing current runtime DLL");
        }
        finally { manager.UnloadAll(); }
    }

    public static void BundleSmoke(string sourceRuntime, string usm, string destination, Action<bool, string> check)
    {
        destination = Path.GetFullPath(destination);
        if (Directory.Exists(destination)) throw new IOException("Runtime smoke output must be a new directory.");
        string cache = Path.Combine(destination, "addressables", "AssetBundles");
        string old = Path.Combine(cache, Bundle, OldHash, "__data"), current = Path.Combine(cache, Bundle, NewHash, "__data");
        Directory.CreateDirectory(Path.GetDirectoryName(old)!);
        Directory.CreateDirectory(Path.GetDirectoryName(current)!);
        string originalHash = SkillMovieEngine.Hash(sourceRuntime);
        File.Copy(sourceRuntime, old); File.Copy(sourceRuntime, current);
        File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddHours(2));
        string game = Settings(destination, "3.3.0");
        Catalog(Path.Combine(Directory.GetParent(cache)!.FullName, "catalog_3.2.0.json"), Bundle, OldHash);
        Catalog(Path.Combine(Directory.GetParent(cache)!.FullName, "catalog_3.3.0.json"), Bundle, NewHash);
        var found = AnimatedPortraitPatch.FindBundles(cache, CancellationToken.None, runtimeOnly: true, gameDirectory: game);
        check(found.Runtime == current, "real bundle copy discovery chooses the exact active version despite a newer stale cache");
        var request = new AnimatedPortraitPatch.Request(cache, found.Runtime, null, "UT_HandCard_20001", usm, "", true, game);
        var first = PortraitReplacement.Apply(request, sourceName: "card.mp4");
        check(PortraitReplacement.CheckCurrentBinding(cache, first, game).Current, "real hand-card replacement verifies both current catalog target and installed bytes");
        check(SkillMovieEngine.Hash(old) == originalHash && SkillMovieEngine.Hash(current) != originalHash,
            "only the active runtime copy is modified; older cached game versions stay untouched");
        var second = PortraitReplacement.Apply(request with { TextureName = "UT_Hero_Card_101" }, sourceName: "portrait.mp4");
        check(second.Portraits.Count == 2 && PortraitReplacement.CheckCurrentBinding(cache, second, game).Current,
            "catalog-aware card and portrait mappings coexist after rebuilding from the original backup");
        using (var zip = ZipFile.OpenRead(second.ReplacementZip))
            check(zip.Entries.Any(entry => entry.FullName == Bundle + "/" + NewHash + "/__data") &&
                !zip.Entries.Any(entry => entry.FullName.Contains(OldHash)), "export includes the active runtime, never an inactive old version");
        PortraitReplacement.Restore(cache);
        check(SkillMovieEngine.Hash(current) == originalHash && SkillMovieEngine.Hash(old) == originalHash && PortraitReplacement.ReadReceipt(cache) == null,
            "catalog-aware restore returns exact original active bytes and clears only its mappings");
        check(SkillMovieEngine.Hash(sourceRuntime) == originalHash, "version-selection smoke test never writes to the real game runtime");
    }
}
