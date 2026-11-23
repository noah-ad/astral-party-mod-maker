using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using JixModMaker;

internal static class InternationalEditionChecks
{
    public static void Layout(string root, Action<bool, string> check)
    {
        string install = Path.Combine(root, "Astral Party");
        string BuildEdition(string launcher, string product)
        {
            string data = Path.Combine(install, launcher, product + "_Data");
            string aa = Path.Combine(data, "StreamingAssets", "aa");
            string baseDir = Directory.CreateDirectory(Path.Combine(aa, "StandaloneWindows64")).FullName;
            File.WriteAllText(Path.Combine(aa, "settings.json"), "{}");
            Directory.CreateDirectory(Path.Combine(data, "com.unity.addressables", "AssetBundles"));
            return baseDir;
        }

        string cn = BuildEdition("8vJXn6CN", "AstralParty_CN"), intl = BuildEdition("8vJXnINT", "AstralParty_INT");
        string cache = RuntimeBundleLocator.GameDataCache(intl);
        check(GameLocator.ResolvePcResources(install, GameEdition.China) == cn &&
            GameLocator.ResolvePcResources(install, GameEdition.International) == intl, "both Steam branches are selectable without falling back to each other");
        check(GameLocator.ResolvePcResources(Path.GetDirectoryName(cache)) == intl &&
            GameLocator.ResolvePcResources(cache) == intl && GameLocator.ResolvePcResources(intl) == intl,
            "international Addressables parent and cache both resolve to the same base library");
        check(GameLocator.ResolvePcResources(cache, GameEdition.China) == null &&
            RuntimeBundleLocator.SamePath(IndexService.UserCacheFor(intl), IndexService.HotCacheDir(GameEdition.International)),
            "international edition uses its own LocalLow tree");
        string android = Directory.CreateDirectory(Path.Combine(root, "android", "com.unity.addressables", "AssetBundles")).FullName;
        check(GameLocator.ResolvePcResources(android) == null && IndexService.UserCacheFor(android) == null,
            "an isolated Android cache is not promoted into a Steam installation");
    }

    public static void Live(string selection, string pack, string output, Action<bool, string> check)
    {
        string game = GameLocator.ResolvePcResources(selection, GameEdition.International)
            ?? throw new DirectoryNotFoundException(selection);
        check(GameLocator.Find(CancellationToken.None, GameEdition.International)?.Edition == GameEdition.International,
            "Steam auto-detection finds the real international player");
        var timer = Stopwatch.StartNew();
        var service = new IndexService();
        var index = service.Load(game, false, true, false);
        if (index == null)
        {
            index = service.Build(game, (done, total) =>
            {
                if (done % 1000 == 0 || done == total) Console.WriteLine($"SCAN {done}/{total} {timer.ElapsedMilliseconds}ms");
            }, false, true);
            service.Save(index);
            Console.WriteLine($"BUILT {timer.ElapsedMilliseconds}ms");
        }
        timer.Restart();
        index = service.Load(game, false, true, false) ?? throw new IOException("Index cache did not reopen.");
        Console.WriteLine($"WARM {timer.ElapsedMilliseconds}ms");
        var textures = index.Items.Where(row => row.Kind == ResourceKinds.Texture).ToArray();
        var baseTextures = textures.Where(row => row.Source == "基础包").ToArray();
        var cachedTextures = textures.Where(row => row.Source == "游戏安装缓存").ToArray();
        var heroes = textures.Select(row => NameParser.Parse(row.Name)).Where(row => row.IsHero && !row.IsMonster)
            .Select(row => row.HeroId).Distinct().Order().ToArray();
        check(baseTextures.Length > 10000 && cachedTextures.Length > 1000 && heroes.Length > 20,
            $"international combined index: {textures.Length} textures, {baseTextures.Length} base, {cachedTextures.Length} installation cache, {heroes.Length} heroes");
        check(!textures.Any(row => row.BundlePath.Contains("AstralParty_CN", StringComparison.OrdinalIgnoreCase)),
            "real international index contains no China-edition paths");
        check(heroes.Contains("101") && heroes.Contains("301") && textures.Any(row => row.Name == "UT_Hero_Bust_113_04_1_sfw"),
            "original roles, 300-series roles and the reported SFW portrait are available together");

        var byName = textures.ToLookup(row => row.Name, StringComparer.OrdinalIgnoreCase);
        using var archive = ZipFile.OpenRead(pack);
        using var manifest = archive.GetEntry("manifest.json")!.Open();
        var mod = JsonSerializer.Deserialize<ModManifest>(manifest)!;
        string Alias(string name) => name.EndsWith("_sfw", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name + "_sfw";
        var explicitNames = mod.Entries.Select(row => row.TextureName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var named = mod.Entries.Where(row => !string.IsNullOrEmpty(row.TextureName)).ToArray();
        var matched = named.Where(row => byName.Contains(row.TextureName) || byName.Contains(Alias(row.TextureName))).ToArray();
        Console.WriteLine($"PACK MATCH {matched.Length}/{mod.Entries.Count}; exact {named.Count(row => byName.Contains(row.TextureName))}");
        Console.WriteLine("Unmatched: " + string.Join(", ", named.Except(matched).Select(row => row.TextureName).Take(30)));
        string work = Directory.CreateDirectory(Path.Combine(Path.GetFullPath(output), "copy-" + Guid.NewGuid().ToString("N"))).FullName;
        string dataDir = Path.Combine(work, "AstralParty_INT_Data");
        string clone = Directory.CreateDirectory(Path.Combine(dataDir, "StreamingAssets", "aa", "StandaloneWindows64")).FullName;
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(clone)!, "settings.json"), "{}");
        string installedCache = RuntimeBundleLocator.GameDataCache(clone);
        var oldProfile = Environment.GetEnvironmentVariable("USERPROFILE");
        try
        {
            Environment.SetEnvironmentVariable("USERPROFILE", Path.Combine(work, "profile"));
            var candidates = new[]
            {
                matched.First(row => byName[row.TextureName].Any(target => target.Source == "基础包" && target.Name.StartsWith("UT_Hero_Card"))),
                matched.First(row => byName[row.TextureName].Any(target => target.Source == "游戏安装缓存") ||
                    byName[Alias(row.TextureName)].Any(target => target.Source == "游戏安装缓存")),
                matched.First(row => !explicitNames.Contains(Alias(row.TextureName)) && byName.Contains(Alias(row.TextureName))),
            }.DistinctBy(row => row.TextureName).ToArray();
            var selected = candidates.Select(row => row.TextureName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var copies = new Dictionary<string, (string Copy, string Hash)>(StringComparer.OrdinalIgnoreCase);
            foreach (var img in candidates)
            {
                var targets = byName[img.TextureName].Concat(selected.Contains(Alias(img.TextureName)) ? Enumerable.Empty<TexIndexEntry>() : byName[Alias(img.TextureName)]);
                foreach (var asset in targets.Where(asset => asset.Source is "基础包" or "游戏安装缓存"))
                {
                    if (copies.ContainsKey(asset.BundlePath)) continue;
                    string relative = Path.GetRelativePath(asset.Source == "基础包" ? game : index.GameCacheDir, asset.BundlePath);
                    string copied = Path.Combine(asset.Source == "基础包" ? clone : installedCache, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(copied)!);
                    File.Copy(asset.BundlePath, copied);
                    var metadata = Path.Combine(Path.GetDirectoryName(asset.BundlePath)!, "__info");
                    if (ResourceLocator.IsWrappedData(asset.BundlePath))
                        File.Copy(metadata, Path.Combine(Path.GetDirectoryName(copied)!, "__info"));
                    copies.Add(asset.BundlePath, (copied, FileIntegrity.Hash(asset.BundlePath)));
                }
            }
            var images = candidates.Select(entry =>
            {
                using var input = archive.GetEntry(entry.Image.Replace('\\', '/'))!.Open();
                using var stream = new MemoryStream();
                input.CopyTo(stream);
                return new NamedImage { TextureName = entry.TextureName, Png = stream.ToArray(), Width = entry.Width, Height = entry.Height };
            }).ToArray();
            string path = Path.Combine(work, "international-test.jxpack");
            PackService.ExportV2(images, path, "International test", "", "PC", "");
            var copiedIndex = service.Build(clone, heroOnly: false, includeHotCache: true);
            var workspace = new ModManifest();
            var backup = Path.Combine(clone, "_原始备份");
            int progress = -1;
            var engine = new ModEngine();
            var result = PackService.ImportV2(path, copiedIndex, clone, engine, backup, workspace, (done, _) => progress = done);
            check(result.Applied == candidates.Length && result.Failed == 0 && result.Missing == 0 && result.VariantTargets > 0
                && progress == images.Length, "real .jxpack writes base, hot-cache and SFW copies with progress");
            check(result.WrittenBundles.Any(bundle => bundle.StartsWith(clone)) &&
                result.WrittenBundles.Any(bundle => bundle.StartsWith(installedCache)), "both international sources were modified in the isolated copy");
            foreach (var entry in workspace.Entries)
            {
                var asset = copiedIndex.Items.Single(row => row.Bundle == entry.Bundle && row.PathId == entry.PathId);
                using var pixels = new MemoryStream(engine.DecodePng(asset.BundlePath, entry.PathId));
                using var decoded = Image.FromStream(pixels);
                var imported = images.SingleOrDefault(image => image.TextureName == entry.TextureName)
                    ?? images.Single(image => Alias(image.TextureName) == entry.TextureName);
                using var decodedBitmap = new Bitmap(decoded);
                using var source = new MemoryStream(imported.Png);
                using var img = new Bitmap(source);
                check(decoded.Size == img.Size && decodedBitmap.GetPixel(decoded.Width / 2, decoded.Height / 2) ==
                    img.GetPixel(img.Width / 2, img.Height / 2), "round-trip pixels: " + entry.TextureName);
                check(copies.Values.Any(copy => copy.Copy == asset.BundlePath &&
                    FileIntegrity.Hash(ResourceLocator.BackupPath(asset.BundlePath, backup, asset.Bundle)) == copy.Hash),
                    "original bundle backed up before writing: " + entry.TextureName);
            }
            check(copies.All(pair => pair.Value.Hash == FileIntegrity.Hash(pair.Key)), "all real international game files used by the test are unchanged");
        }
        finally { Environment.SetEnvironmentVariable("USERPROFILE", oldProfile); }
    }
}
