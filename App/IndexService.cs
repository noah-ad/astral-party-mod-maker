using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace JixModMaker;

/// <summary>索引中的一个 Unity 资源。</summary>
public class TexIndexEntry
{
    public string Bundle { get; set; }
    public string BundlePath { get; set; }
    public string Source { get; set; }
    public long PathId { get; set; }
    public string Name { get; set; }
    public string Kind { get; set; } = ResourceKinds.Texture;
    public string CategoryId { get; set; }
    public string CategoryLabel { get; set; }
    public string Format { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}

public class LightweightTextureRef
{
    public string Bundle { get; set; }
    public string Name { get; set; }
}

/// <summary>一个资源目录的多类型索引，可缓存。</summary>
public class GameIndex
{
    public string GameDir { get; set; }
    public string HotDir { get; set; }
    public string BuiltAt { get; set; }
    public string SourceStamp { get; set; }
    public Dictionary<string, string> BundleStamps { get; set; } = new();
    public bool HeroOnly { get; set; } = true;
    public bool IncludeHotCache { get; set; }
    public bool Recursive { get; set; }
    public bool Lightweight { get; set; }
    public int LightweightTextureCount { get; set; }
    public Dictionary<string, List<string>> TextureNamesByBundle { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> BundlePathsByName { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> BundleSourcesByName { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> TextureCategoryCounts { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, List<LightweightTextureRef>> TextureRefsByCategory { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<TexIndexEntry> Items { get; set; } = new();

    public int AssetCount => Lightweight ? LightweightTextureCount : Items.Count;

    public int BundleCount => Lightweight
        ? TextureNamesByBundle?.Count ?? 0
        : Items.Select(i => i.BundlePath).Distinct(StringComparer.OrdinalIgnoreCase).Count();

    public int CountKind(string kind)
    {
        if (Lightweight)
            return kind == ResourceKinds.Texture ? LightweightTextureCount : 0;
        return Items.Count(i => i.Kind == kind);
    }

    public void AddTextureCategoryCount(string categoryId)
    {
        TextureCategoryCounts ??= new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        categoryId = string.IsNullOrWhiteSpace(categoryId) ? ResourceCategories.AllId : categoryId;
        TextureCategoryCounts[categoryId] = TextureCategoryCounts.GetValueOrDefault(categoryId) + 1;
        TextureCategoryCounts[ResourceCategories.AllId] = TextureCategoryCounts.GetValueOrDefault(ResourceCategories.AllId) + 1;
    }

    public void AddTextureCategoryRef(string categoryId, string bundle, string name)
    {
        TextureRefsByCategory ??= new Dictionary<string, List<LightweightTextureRef>>(StringComparer.OrdinalIgnoreCase);
        categoryId = string.IsNullOrWhiteSpace(categoryId) ? ResourceCategories.AllId : categoryId;
        if (!TextureRefsByCategory.TryGetValue(categoryId, out var list))
        {
            list = new List<LightweightTextureRef>();
            TextureRefsByCategory[categoryId] = list;
        }
        list.Add(new LightweightTextureRef { Bundle = bundle, Name = name });
    }

    public IEnumerable<TexIndexEntry> EnumerateLightweightTextures()
    {
        if (!Lightweight || TextureNamesByBundle == null) yield break;

        foreach (var pair in TextureNamesByBundle)
        {
            var bundleName = pair.Key;
            if (pair.Value == null || pair.Value.Count == 0) continue;

            string bundlePath = "";
            string source = "";
            BundlePathsByName?.TryGetValue(bundleName, out bundlePath);
            BundleSourcesByName?.TryGetValue(bundleName, out source);
            foreach (var name in pair.Value)
            {
                var catId = ResourceCategories.Categorize(ResourceKinds.Texture, name);
                yield return new TexIndexEntry
                {
                    Bundle = bundleName,
                    BundlePath = bundlePath,
                    Source = source,
                    PathId = 0,
                    Name = name,
                    Kind = ResourceKinds.Texture,
                    CategoryId = catId,
                    CategoryLabel = ResourceCategories.Label(ResourceKinds.Texture, catId),
                    Format = "点选解析"
                };
            }
        }
    }

    public IEnumerable<TexIndexEntry> FindTextureTargets(string textureName)
    {
        if (string.IsNullOrWhiteSpace(textureName)) yield break;

        if (!Lightweight)
        {
            foreach (var item in Items.Where(i =>
                i.Kind == ResourceKinds.Texture &&
                string.Equals(i.Name, textureName, StringComparison.Ordinal)))
            {
                yield return item;
            }
            yield break;
        }

        if (TextureNamesByBundle == null) yield break;
        foreach (var pair in TextureNamesByBundle)
        {
            if (pair.Value == null || !pair.Value.Any(n => string.Equals(n, textureName, StringComparison.Ordinal)))
                continue;

            string bundlePath = "";
            string source = "";
            BundlePathsByName?.TryGetValue(pair.Key, out bundlePath);
            BundleSourcesByName?.TryGetValue(pair.Key, out source);
            var catId = ResourceCategories.Categorize(ResourceKinds.Texture, textureName);
            yield return new TexIndexEntry
            {
                Bundle = pair.Key,
                BundlePath = bundlePath,
                Source = source,
                PathId = 0,
                Name = textureName,
                Kind = ResourceKinds.Texture,
                CategoryId = catId,
                CategoryLabel = ResourceCategories.Label(ResourceKinds.Texture, catId),
                Format = "点选解析"
            };
        }
    }

    public TexIndexEntry FindTextureTarget(string bundleName, string textureName, long pathId = 0)
    {
        if (string.IsNullOrWhiteSpace(bundleName)) return null;

        if (!Lightweight)
            return Items.FirstOrDefault(i =>
                string.Equals(i.Bundle, bundleName, StringComparison.OrdinalIgnoreCase)
                && (pathId > 0 && i.PathId == pathId
                    || pathId == 0 && string.Equals(i.Name, textureName, StringComparison.Ordinal)));

        if (TextureNamesByBundle == null
            || !TextureNamesByBundle.TryGetValue(bundleName, out var names)
            || names == null
            || !names.Any(n => string.Equals(n, textureName, StringComparison.Ordinal)))
            return null;

        string bundlePath = "";
        string source = "";
        BundlePathsByName?.TryGetValue(bundleName, out bundlePath);
        BundleSourcesByName?.TryGetValue(bundleName, out source);
        var catId = ResourceCategories.Categorize(ResourceKinds.Texture, textureName);
        return new TexIndexEntry
        {
            Bundle = bundleName,
            BundlePath = bundlePath,
            Source = source,
            PathId = 0,
            Name = textureName,
            Kind = ResourceKinds.Texture,
            CategoryId = catId,
            CategoryLabel = ResourceCategories.Label(ResourceKinds.Texture, catId),
            Format = "点选解析"
        };
    }
}

/// <summary>扫描基础 .bundle 和可选 Addressables 热更 __data，建立多类型资源索引。</summary>
public class IndexService
{
    private readonly ModEngine _engine = new();
    private const string CacheVersion = "v7";
    private static readonly TimeSpan HotCacheTtl = TimeSpan.FromHours(12);

    public sealed class BundleScanEntry
    {
        public string Name { get; init; }
        public string Path { get; init; }
        public string Source { get; init; }
        public long Length { get; init; }
        public long LastWriteTicks { get; init; }
    }

    public GameIndex Build(string gameDir, Action<int, int> progress = null, bool heroOnly = true,
                           bool includeHotCache = false, bool recursive = false, bool includeAdvancedTypes = false)
    {
        gameDir = ResourceLocator.NormalizeGameDirectory(gameDir);
        var idx = new GameIndex
        {
            GameDir = gameDir,
            HotDir = includeHotCache ? HotCacheDir() : "",
            BuiltAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            HeroOnly = heroOnly,
            IncludeHotCache = includeHotCache,
            Recursive = recursive,
            Lightweight = false
        };
        var bundles = EnumerateBundleEntries(gameDir, includeHotCache, recursive).ToList();
        idx.SourceStamp = ComputeStamp(bundles);
        int done = 0;
        GameIndex previous = null;
        try { previous = JsonSerializer.Deserialize<GameIndex>(File.ReadAllText(CachePath(gameDir, heroOnly, includeHotCache, recursive))); } catch { }
        var oldRows = previous?.Items.GroupBy(x => x.Bundle).ToDictionary(g => g.Key, g => g.ToList());

        foreach (var b in bundles)
        {
            string stamp = $"{b.Path}|{b.Length}|{b.LastWriteTicks}";
            if (!includeAdvancedTypes && previous?.BundleStamps?.GetValueOrDefault(b.Name) == stamp)
            {
                if (oldRows.TryGetValue(b.Name, out var reused)) idx.Items.AddRange(reused);
                idx.BundleStamps[b.Name] = stamp;
                progress?.Invoke(++done, bundles.Count);
                continue;
            }
            try
            {
                var assets = includeAdvancedTypes ? _engine.ListAssets(b.Path) : _engine.ListTextureHeaders(b.Path);
                foreach (var asset in assets)
                {
                    if (heroOnly)
                    {
                        if (asset.Kind != ResourceKinds.Texture) continue;
                        var parsed = NameParser.Parse(asset.Name);
                        if (!parsed.IsHero && !parsed.IsHandCard) continue;
                    }

                    var catId = ResourceCategories.Categorize(asset.Kind, asset.Name, asset.Width, asset.Height);
                    if (asset.Kind == ResourceKinds.Texture)
                        idx.AddTextureCategoryCount(catId);
                    idx.Items.Add(new TexIndexEntry
                    {
                        Bundle = b.Name,
                        BundlePath = b.Path,
                        Source = b.Source,
                        PathId = asset.PathId,
                        Name = asset.Name,
                        Kind = asset.Kind,
                        CategoryId = catId,
                        CategoryLabel = ResourceCategories.Label(asset.Kind, catId),
                        Format = asset.Format,
                        Width = asset.Width,
                        Height = asset.Height
                    });
                }
                idx.BundleStamps[b.Name] = stamp;
            }
            catch { }

            progress?.Invoke(++done, bundles.Count);
        }
        return idx;
    }

    public static IReadOnlyList<BundleScanEntry> EnumerateBundles(string gameDir, bool includeHotCache, bool recursive)
        => EnumerateBundleEntries(gameDir, includeHotCache, recursive).ToList();

    private static IEnumerable<BundleScanEntry> EnumerateBundleEntries(string gameDir, bool includeHotCache, bool recursive)
    {
        gameDir = ResourceLocator.NormalizeGameDirectory(gameDir);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (includeHotCache)
        {
            foreach (var f in EnumerateHotDataFilesCached())
            {
                var logical = Path.GetFileName(Path.GetDirectoryName(f)) + ".bundle";
                if (seen.Add(logical))
                    yield return MakeBundleEntry(logical, f, "热更缓存");
            }
        }

        if (!Directory.Exists(gameDir)) yield break;

        foreach (var f in ResourceLocator.EnumerateResourceFiles(gameDir))
        {
            if (!recursive && !ResourceLocator.IsWrappedData(f)
                && !string.Equals(Path.GetDirectoryName(f), gameDir, StringComparison.OrdinalIgnoreCase))
                continue;

            var logical = ResourceLocator.RelativePath(gameDir, f);
            if (seen.Add(logical))
                yield return MakeBundleEntry(logical, f, ResourceLocator.IsWrappedData(f) ? "B服资源" : "基础包");
        }
    }

    private sealed class HotDataCache
    {
        public string HotDir { get; set; }
        public long CreatedUtcTicks { get; set; }
        public List<string> DataFiles { get; set; } = new();
    }

    public static void ClearHotBundleCache()
    {
        try
        {
            var p = HotDataCachePath();
            if (File.Exists(p)) File.Delete(p);
        }
        catch { }
    }

    private static IEnumerable<string> EnumerateHotDataFilesCached()
    {
        var hot = HotCacheDir();
        if (!Directory.Exists(hot)) yield break;

        List<string> cached = null;
        if (cached == null)
        {
            cached = Directory.GetFiles(hot, "__data", SearchOption.AllDirectories).ToList();
            TrySaveHotDataCache(hot, cached);
        }

        foreach (var f in cached)
            yield return f;
    }

    private static List<string> TryLoadHotDataCache(string hotDir)
    {
        try
        {
            var p = HotDataCachePath();
            if (!File.Exists(p)) return null;
            var cache = JsonSerializer.Deserialize<HotDataCache>(File.ReadAllText(p));
            if (cache?.DataFiles == null || cache.DataFiles.Count == 0) return null;
            if (!string.Equals(cache.HotDir, hotDir, StringComparison.OrdinalIgnoreCase)) return null;
            if (DateTime.UtcNow.Ticks - cache.CreatedUtcTicks > HotCacheTtl.Ticks) return null;

            return cache.DataFiles.Take(8).All(File.Exists) ? cache.DataFiles : null;
        }
        catch { return null; }
    }

    private static void TrySaveHotDataCache(string hotDir, List<string> dataFiles)
    {
        try
        {
            var cache = new HotDataCache
            {
                HotDir = hotDir,
                CreatedUtcTicks = DateTime.UtcNow.Ticks,
                DataFiles = dataFiles
            };
            File.WriteAllText(HotDataCachePath(), JsonSerializer.Serialize(cache));
        }
        catch { }
    }

    private static string HotDataCachePath()
    {
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JixModMaker");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "hot_data_files.json");
    }

    private static BundleScanEntry MakeBundleEntry(string logical, string path, string source)
    {
        var info = new FileInfo(path);
        return new() { Name = logical, Path = path, Source = source, Length = info.Length, LastWriteTicks = info.LastWriteTimeUtc.Ticks };
    }

    public static string ComputeSourceStamp(string gameDir, bool includeHotCache, bool recursive)
        => ComputeStamp(EnumerateBundleEntries(gameDir, includeHotCache, recursive));

    private static string ComputeStamp(IEnumerable<BundleScanEntry> bundles)
    {
        var sb = new StringBuilder();
        foreach (var b in bundles.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Path, StringComparer.OrdinalIgnoreCase))
            sb.Append(b.Name).Append('|').Append(b.Path).Append('|').Append(b.Length).Append('|').Append(b.LastWriteTicks).Append('\n');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }

    public static string HotCacheDir()
    {
        var profile = Environment.GetEnvironmentVariable("USERPROFILE");
        if (string.IsNullOrWhiteSpace(profile))
            profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(profile, "AppData", "LocalLow", "feimo", "AstralParty_CN",
            "com.unity.addressables", "AssetBundles");
    }

    public static string CachePath(string gameDir, bool heroOnly = true, bool includeHotCache = false, bool recursive = false)
    {
        gameDir = ResourceLocator.NormalizeGameDirectory(gameDir);
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JixModMaker");
        Directory.CreateDirectory(dir);
        string h = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(gameDir.ToLowerInvariant())))[..8];
        string mode = includeHotCache ? "hot" : "local";
        string depth = recursive ? "recursive" : "flat";
        return Path.Combine(dir, $"index_{CacheVersion}_{(heroOnly ? "hero" : "full")}_{mode}_{depth}_{h}.json");
    }

    public void Save(GameIndex idx)
        => File.WriteAllText(CachePath(idx.GameDir, idx.HeroOnly, idx.IncludeHotCache, idx.Recursive), JsonSerializer.Serialize(idx));

    public GameIndex Load(string gameDir, bool heroOnly = true, bool includeHotCache = false, bool recursive = false)
    {
        gameDir = ResourceLocator.NormalizeGameDirectory(gameDir);
        var cached = TryLoadSavedIndex(gameDir, heroOnly, includeHotCache, recursive);
        return cached;
    }

    private GameIndex TryLoadSavedIndex(string gameDir, bool heroOnly, bool includeHotCache, bool recursive)
    {
        var p = CachePath(gameDir, heroOnly, includeHotCache, recursive);
        if (!File.Exists(p)) return null;
        try
        {
            var idx = JsonSerializer.Deserialize<GameIndex>(File.ReadAllText(p));
            if (idx == null) return null;
            if ((idx.Lightweight ? idx.LightweightTextureCount == 0 : idx.Items.Count == 0)
                && ResourceLocator.HasWrappedResources(gameDir))
                return null;
            bool validShape = idx.Lightweight
                ? idx.TextureNamesByBundle != null && idx.BundlePathsByName != null && idx.TextureRefsByCategory != null
                : !idx.Items.Any(i => string.IsNullOrWhiteSpace(i.Kind) || string.IsNullOrWhiteSpace(i.BundlePath));
            if (validShape
                && string.Equals(idx.SourceStamp, ComputeSourceStamp(gameDir, includeHotCache, recursive), StringComparison.OrdinalIgnoreCase))
                return idx;
        }
        catch { }
        return null;
    }

    private GameIndex LoadReferenceTextureIndex(string gameDir, bool includeHotCache, bool recursive, bool heroOnly)
    {
        var source = ReferenceTextureIndexCandidates().FirstOrDefault(File.Exists);
        if (source == null) return null;

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(source));
            if (!doc.RootElement.TryGetProperty("texture", out var texture) || texture.ValueKind != JsonValueKind.Object)
                return null;

            var bundles = EnumerateBundleEntries(gameDir, includeHotCache, recursive).ToList();
            var bundleMap = bundles
                .GroupBy(b => b.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            if (bundleMap.Count == 0) return null;

            var idx = new GameIndex
            {
                GameDir = gameDir,
                HotDir = includeHotCache ? HotCacheDir() : "",
                BuiltAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " / 参考轻索引",
                SourceStamp = ComputeStamp(bundles),
                HeroOnly = heroOnly,
                IncludeHotCache = includeHotCache,
                Recursive = recursive,
                Lightweight = true
            };

            foreach (var b in bundleMap.Values)
            {
                idx.BundlePathsByName[b.Name] = b.Path;
                idx.BundleSourcesByName[b.Name] = b.Source + "/参考轻索引";
            }

            foreach (var bundleProp in texture.EnumerateObject())
            {
                if (!bundleMap.TryGetValue(bundleProp.Name, out var bundle)) continue;
                if (bundleProp.Value.ValueKind != JsonValueKind.Array) continue;
                var names = new List<string>();
                foreach (var nameNode in bundleProp.Value.EnumerateArray())
                {
                    var name = nameNode.GetString();
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    if (heroOnly)
                    {
                        var parsed = NameParser.Parse(name);
                        if (!parsed.IsHero && !parsed.IsHandCard) continue;
                    }
                    var catId = ResourceCategories.Categorize(ResourceKinds.Texture, name);
                    idx.AddTextureCategoryCount(catId);
                    if (ShouldStoreLightweightCategoryRef(catId))
                        idx.AddTextureCategoryRef(catId, bundle.Name, name);
                    names.Add(name);
                }
                if (names.Count == 0) continue;
                idx.TextureNamesByBundle[bundle.Name] = names;
                idx.LightweightTextureCount += names.Count;
            }

            return idx.LightweightTextureCount > 0 ? idx : null;
        }
        catch { return null; }
    }

    private static IEnumerable<string> ReferenceTextureIndexCandidates()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in RootCandidates())
        {
            if (string.IsNullOrWhiteSpace(root)) continue;
            foreach (var rel in new[]
            {
                Path.Combine("JiXingModHelper-win64", "modkit_data", "texture_index.json"),
                Path.Combine("modkit_data", "texture_index.json")
            })
            {
                var p = Path.Combine(root, rel);
                if (seen.Add(p)) yield return p;
            }
        }
    }

    private static IEnumerable<string> RootCandidates()
    {
        yield return Environment.CurrentDirectory;
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 7 && !string.IsNullOrWhiteSpace(dir); i++)
        {
            yield return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }
    }

    private static bool ShouldStoreLightweightCategoryRef(string categoryId)
        => !string.IsNullOrWhiteSpace(categoryId)
        && categoryId != ResourceCategories.AllId
        && !string.Equals(categoryId, "other", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(categoryId, "fx", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(categoryId, "lightmap", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(categoryId, "sprite_anim", StringComparison.OrdinalIgnoreCase);
}
