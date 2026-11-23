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
    public bool IsSkillAnimation { get; set; }
    public string OwnerHeroId { get; set; }
    public string OwnerVariant { get; set; }
    public bool OwnerIsMonster { get; set; }
}

public class LightweightTextureRef
{
    public string Bundle { get; set; }
    public string Name { get; set; }
    public bool IsSkillAnimation { get; set; }
    public string OwnerHeroId { get; set; }
    public string OwnerVariant { get; set; }
    public bool OwnerIsMonster { get; set; }
}

/// <summary>一个资源目录的多类型索引，可缓存。</summary>
public class GameIndex
{
    public string GameDir { get; set; }
    public string HotDir { get; set; }
    public string GameCacheDir { get; set; }
    public string BuiltAt { get; set; }
    public string SourceStamp { get; set; }
    public string QuickSourceStamp { get; set; }
    public Dictionary<string, string> BundleStamps { get; set; } = new();
    public bool HeroOnly { get; set; } = true;
    public bool IncludeHotCache { get; set; }
    public bool Recursive { get; set; }
    public bool Lightweight { get; set; }
    public bool IncludesAdvancedTypes { get; set; }
    public int LightweightTextureCount { get; set; }
    public Dictionary<string, List<string>> TextureNamesByBundle { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> BundlePathsByName { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> BundleSourcesByName { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> TextureCategoryCounts { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, List<LightweightTextureRef>> TextureRefsByCategory { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, CharacterAssetOwner> CharacterOwnersByBundle { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<TexIndexEntry> Items { get; set; } = new();

    public int AssetCount => Lightweight ? LightweightTextureCount + Items.Count : Items.Count;

    public int BundleCount => Lightweight
        ? TextureNamesByBundle?.Count ?? 0
        : Items.Select(i => i.BundlePath).Distinct(StringComparer.OrdinalIgnoreCase).Count();

    public int CountKind(string kind)
    {
        if (Lightweight)
            return kind == ResourceKinds.Texture ? LightweightTextureCount + Items.Count(i => i.Kind == ResourceKinds.SkillMovie) : 0;
        return Items.Count(i => ResourceKinds.InBrowser(i.Kind, kind));
    }

    public void AddTextureCategoryCount(string categoryId)
    {
        TextureCategoryCounts ??= new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        categoryId = string.IsNullOrWhiteSpace(categoryId) ? ResourceCategories.AllId : categoryId;
        TextureCategoryCounts[categoryId] = TextureCategoryCounts.GetValueOrDefault(categoryId) + 1;
        TextureCategoryCounts[ResourceCategories.AllId] = TextureCategoryCounts.GetValueOrDefault(ResourceCategories.AllId) + 1;
    }

    public void AddTextureCategoryRef(string categoryId, string bundle, string name, TexIndexEntry metadata = null)
    {
        TextureRefsByCategory ??= new Dictionary<string, List<LightweightTextureRef>>(StringComparer.OrdinalIgnoreCase);
        categoryId = string.IsNullOrWhiteSpace(categoryId) ? ResourceCategories.AllId : categoryId;
        if (!TextureRefsByCategory.TryGetValue(categoryId, out var list))
        {
            list = new List<LightweightTextureRef>();
            TextureRefsByCategory[categoryId] = list;
        }
        list.Add(new LightweightTextureRef
        {
            Bundle = bundle,
            Name = name,
            IsSkillAnimation = metadata?.IsSkillAnimation == true,
            OwnerHeroId = metadata?.OwnerHeroId,
            OwnerVariant = metadata?.OwnerVariant,
            OwnerIsMonster = metadata?.OwnerIsMonster == true
        });
    }

    public void ApplyCharacterOwnership(TexIndexEntry entry)
    {
        if (entry == null || entry.Kind != ResourceKinds.Texture || !ResourceCategories.IsSkillAnimationAtlas(entry.Name)) return;
        string bundle = Path.GetFileName((entry.Bundle ?? "").Replace('/', Path.DirectorySeparatorChar));
        if (CharacterOwnersByBundle == null || !CharacterOwnersByBundle.TryGetValue(bundle, out var owner)) return;
        entry.IsSkillAnimation = true;
        entry.OwnerHeroId = owner.HeroId;
        entry.OwnerVariant = owner.Variant;
        entry.OwnerIsMonster = owner.IsMonster;
        entry.CategoryId = ResourceCategories.CharacterId;
        entry.CategoryLabel = ResourceCategories.Label(ResourceKinds.Texture, ResourceCategories.CharacterId);
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
                var entry = new TexIndexEntry
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
                ApplyCharacterOwnership(entry);
                yield return entry;
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
            var entry = new TexIndexEntry
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
            ApplyCharacterOwnership(entry);
            yield return entry;
        }
    }

    public TexIndexEntry FindTextureTarget(string bundleName, string textureName, long pathId = 0)
    {
        if (string.IsNullOrWhiteSpace(bundleName)) return null;
        var candidates = Items.Where(i => string.Equals(PackService.BundleKey(i.Bundle), PackService.BundleKey(bundleName), StringComparison.OrdinalIgnoreCase)
            && (string.IsNullOrWhiteSpace(textureName) ? pathId != 0 && i.PathId == pathId : i.Name == textureName)).ToList();
        var exact = candidates.FirstOrDefault(i => pathId != 0 && i.PathId == pathId) ?? (candidates.Count == 1 ? candidates[0] : null);
        if (exact != null || !Lightweight) return exact;

        var bundle = TextureNamesByBundle?.FirstOrDefault(pair => string.Equals(PackService.BundleKey(pair.Key),
            PackService.BundleKey(bundleName), StringComparison.OrdinalIgnoreCase)) ?? default;
        var names = bundle.Value;
        if (names == null
            || !names.Any(n => string.Equals(n, textureName, StringComparison.Ordinal)))
            return null;

        string bundlePath = "";
        string source = "";
        BundlePathsByName?.TryGetValue(bundle.Key, out bundlePath);
        BundleSourcesByName?.TryGetValue(bundle.Key, out source);
        var catId = ResourceCategories.Categorize(ResourceKinds.Texture, textureName);
        var entry = new TexIndexEntry
        {
            Bundle = bundle.Key,
            BundlePath = bundlePath,
            Source = source,
            PathId = 0,
            Name = textureName,
            Kind = ResourceKinds.Texture,
            CategoryId = catId,
            CategoryLabel = ResourceCategories.Label(ResourceKinds.Texture, catId),
            Format = "点选解析"
        };
        ApplyCharacterOwnership(entry);
        return entry;
    }
}

/// <summary>扫描基础 .bundle 和可选 Addressables 热更 __data，建立多类型资源索引。</summary>
public class IndexService
{
    private readonly ModEngine _engine = new();
    private const string CacheVersion = "v7";

    public sealed class BundleScanEntry
    {
        public string Name { get; init; }
        public string Path { get; init; }
        public string Source { get; init; }
        public long Length { get; init; }
        public long LastWriteTicks { get; init; }
    }

    public GameIndex Build(string gameDir, Action<int, int> progress = null, bool heroOnly = true,
                           bool includeHotCache = false, bool recursive = false, bool includeAdvancedTypes = false,
                           CancellationToken token = default)
    {
        gameDir = ResourceLocator.NormalizeGameDirectory(gameDir);
        var idx = new GameIndex
        {
            GameDir = gameDir,
            HotDir = includeHotCache ? UserCacheFor(gameDir) ?? "" : "",
            GameCacheDir = includeHotCache ? RuntimeBundleLocator.GameDataCache(gameDir) : "",
            BuiltAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            HeroOnly = heroOnly,
            IncludeHotCache = includeHotCache,
            Recursive = recursive,
            IncludesAdvancedTypes = includeAdvancedTypes,
            Lightweight = false
        };
        var bundles = EnumerateBundleEntries(gameDir, includeHotCache, recursive, token).ToList();
        token.ThrowIfCancellationRequested();
        progress?.Invoke(0, bundles.Count);
        idx.SourceStamp = ComputeStamp(bundles);
        idx.QuickSourceStamp = ComputeQuickSourceStamp(gameDir, includeHotCache, recursive);
        var byStamp = new Dictionary<string, List<TexIndexEntry>>(StringComparer.OrdinalIgnoreCase);
        void Reuse(string folder, bool hot, bool nested)
        {
            token.ThrowIfCancellationRequested();
            var cache = ReadCache(folder, heroOnly, hot, nested);
            if (cache?.BundleStamps == null || cache.Items == null || cache.Lightweight
                || (includeAdvancedTypes && !cache.IncludesAdvancedTypes)) return;
            var oldRows = cache.Items.Where(row => row.Kind != ResourceKinds.SkillMovie)
                .ToLookup(row => row.Bundle, StringComparer.OrdinalIgnoreCase);
            foreach (var pair in cache.BundleStamps)
                byStamp.TryAdd(pair.Value, oldRows[pair.Key].ToList());
        }
        Reuse(gameDir, includeHotCache, recursive);
        // Earlier versions could open the installed cache separately. Its unchanged headers are still useful.
        if (includeHotCache && Directory.Exists(idx.GameCacheDir) && !RuntimeBundleLocator.SamePath(gameDir, idx.GameCacheDir))
            Reuse(idx.GameCacheDir, false, true);

        var rows = new List<TexIndexEntry>[bundles.Count];
        var unknown = new List<int>();
        for (int i = 0; i < bundles.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            var b = bundles[i];
            if (byStamp.TryGetValue(BundleStamp(b), out var reused))
            {
                foreach (var entry in reused) { entry.Bundle = b.Name; entry.Source = b.Source; entry.BundlePath = b.Path; }
                rows[i] = reused;
            }
            else unknown.Add(i);
        }
        int done = bundles.Count - unknown.Count;
        if (done > 0) progress?.Invoke(done, bundles.Count);
        var options = new ParallelOptions { MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount / 2, 1, 4), CancellationToken = token };
        var progressLock = new object();
        Parallel.ForEach(unknown, options, () => new ModEngine.BundleHeaderReader(_engine), (i, _, reader) =>
        {
            var b = bundles[i];
            try
            {
                var entries = new List<TexIndexEntry>();
                var assets = reader.Read(b.Path, includeAdvancedTypes);
                foreach (var asset in assets)
                {
                    if (heroOnly)
                    {
                        if (asset.Kind != ResourceKinds.Texture) continue;
                        var parsed = NameParser.Parse(asset.Name);
                        if (!parsed.IsHero && !parsed.IsHandCard) continue;
                    }

                    var catId = ResourceCategories.Categorize(asset.Kind, asset.Name, asset.Width, asset.Height);
                    entries.Add(new TexIndexEntry
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
                rows[i] = entries;
            }
            catch (Exception ex) when (ex is not (OperationCanceledException or OutOfMemoryException)) { }
            lock (progressLock) progress?.Invoke(++done, bundles.Count);
            return reader;
        }, _ => { });
        for (int i = 0; i < bundles.Count; i++)
        {
            if (rows[i] == null) continue;
            idx.Items.AddRange(rows[i]);
            idx.BundleStamps[bundles[i].Name] = BundleStamp(bundles[i]);
        }
        token.ThrowIfCancellationRequested();
        ApplySkillOwnership(idx);
        return idx;
    }

    private static string BundleStamp(BundleScanEntry b) => $"{b.Path}|{b.Length}|{b.LastWriteTicks}";

    private static GameIndex ReadCache(string gameDir, bool heroOnly, bool hot, bool recursive)
    {
        try
        {
            using var input = new FileStream(CachePath(gameDir, heroOnly, hot, recursive), FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            return JsonSerializer.Deserialize<GameIndex>(input);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    public static IReadOnlyList<BundleScanEntry> EnumerateBundles(string gameDir, bool includeHotCache, bool recursive)
        => EnumerateBundleEntries(gameDir, includeHotCache, recursive).ToList();

    private static IEnumerable<BundleScanEntry> EnumerateBundleEntries(string gameDir, bool includeHotCache, bool recursive, CancellationToken token = default)
    {
        gameDir = ResourceLocator.NormalizeGameDirectory(gameDir);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        BundleScanEntry Add(string logical, string path, string source, string prefix)
        {
            if (!paths.Add(Path.GetFullPath(path))) return null;
            if (!seen.Add(logical))
            {
                logical = prefix + "/" + logical.Replace('\\', '/');
                if (!seen.Add(logical))
                {
                    logical = prefix + "/" + Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(path))) + "/" + Path.GetFileName(logical);
                    if (!seen.Add(logical)) return null;
                }
            }
            return MakeBundleEntry(logical, path, source);
        }

        if (includeHotCache)
        {
            foreach (var cache in new[] { (Root: UserCacheFor(gameDir), Source: "热更缓存", Prefix: "user-cache"),
                (Root: RuntimeBundleLocator.GameDataCache(gameDir), Source: "游戏安装缓存", Prefix: "install-cache") })
            {
                foreach (var f in EnumerateHotDataFiles(cache.Root, token))
                {
                    token.ThrowIfCancellationRequested();
                    string logical = Path.GetFileName(Path.GetDirectoryName(f)) + ".bundle";
                    var entry = Add(logical, f, cache.Source, cache.Prefix);
                    if (entry != null) yield return entry;
                }
            }
        }

        if (!Directory.Exists(gameDir)) yield break;

        bool installationDirectory = RuntimeBundleLocator.UsesDataPath(gameDir);
        foreach (var f in ResourceLocator.EnumerateResourceFiles(gameDir, token, recursive))
        {
            token.ThrowIfCancellationRequested();
            var logical = Path.GetRelativePath(gameDir, f);
            string source = installationDirectory ? "游戏安装缓存"
                : ResourceLocator.IsWrappedData(f) ? "B服资源" : "基础包";
            var entry = Add(logical, f, source, "base");
            if (entry != null) yield return entry;
        }
    }

    public static string SourceLabel(string source) => string.IsNullOrWhiteSpace(source) ? "资源目录"
        : source.StartsWith("游戏安装缓存", StringComparison.Ordinal) ? "安装缓存"
        : source?.StartsWith("热更缓存", StringComparison.Ordinal) == true ? "用户缓存" : source?.Split('/')[0] ?? "资源目录";

    public static int SourcePriority(string source) => SourceLabel(source) switch
    {
        "安装缓存" => 0, "用户缓存" => 1, "B服资源" => 1, "基础包" => 2, _ => 3
    };

    private static IEnumerable<DirectoryInfo> HashFolders(string root)
    {
        if (!Directory.Exists(root)) return Enumerable.Empty<DirectoryInfo>();
        return new DirectoryInfo(root).EnumerateDirectories("*", new EnumerationOptions
        { IgnoreInaccessible = false, AttributesToSkip = FileAttributes.ReparsePoint })
            .Where(dir => dir.Name.Length == 32 && dir.Name.All(Uri.IsHexDigit)).OrderBy(dir => dir.Name, StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> EnumerateHotDataFiles(string hot, CancellationToken token)
    {
        foreach (var owner in HashFolders(hot))
        {
            token.ThrowIfCancellationRequested();
            foreach (var version in HashFolders(owner.FullName))
            {
                token.ThrowIfCancellationRequested();
                string path = Path.Combine(version.FullName, "__data");
                if (File.Exists(path) && File.Exists(Path.Combine(version.FullName, "__info"))) yield return path;
            }
        }
    }

    private static BundleScanEntry MakeBundleEntry(string logical, string path, string source)
    {
        var info = new FileInfo(path);
        return new() { Name = logical, Path = path, Source = source, Length = info.Length, LastWriteTicks = info.LastWriteTimeUtc.Ticks };
    }

    public static string ComputeSourceStamp(string gameDir, bool includeHotCache, bool recursive)
        => ComputeStamp(EnumerateBundleEntries(gameDir, includeHotCache, recursive));

    public static string ComputeQuickSourceStamp(string gameDir, bool includeHotCache, bool recursive)
    {
        gameDir = ResourceLocator.NormalizeGameDirectory(gameDir);
        var text = new StringBuilder();
        text.Append("cache-layout|4\n");
        AppendDirectory(text, "game", gameDir);
        text.Append("recursive|").Append(recursive).Append('\n');
        text.Append("hot|").Append(includeHotCache).Append('\n');
        if (includeHotCache)
        {
            string hot = UserCacheFor(gameDir);
            AppendDirectory(text, "hot-root", hot);
            string installed = RuntimeBundleLocator.GameDataCache(gameDir);
            AppendDirectory(text, "install-cache-root", installed);
            foreach (var root in new[] { hot, installed }.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
                foreach (var owner in HashFolders(root))
                    text.Append(owner.Name).Append('|').Append(owner.LastWriteTimeUtc.Ticks).Append('\n');
            foreach (string catalogRoot in new[] { string.IsNullOrWhiteSpace(hot) ? null : Directory.GetParent(hot)?.FullName,
                string.IsNullOrWhiteSpace(installed) ? null : Directory.GetParent(installed)?.FullName,
                Directory.GetParent(Path.GetFullPath(gameDir))?.FullName }.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                foreach (string path in Directory.EnumerateFiles(catalogRoot, "catalog*.json", SearchOption.TopDirectoryOnly)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                {
                    var info = new FileInfo(path);
                    text.Append("catalog|").Append(info.Name).Append('|').Append(info.Length).Append('|')
                        .Append(info.LastWriteTimeUtc.Ticks).Append('\n');
                }
            }
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    private static void AppendDirectory(StringBuilder text, string label, string path)
    {
        text.Append(label).Append('|').Append((path ?? "").ToLowerInvariant()).Append('|');
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            text.Append("missing\n");
            return;
        }
        var info = new DirectoryInfo(path);
        text.Append(info.CreationTimeUtc.Ticks).Append('|').Append(info.LastWriteTimeUtc.Ticks).Append('\n');
    }

    private static string ComputeStamp(IEnumerable<BundleScanEntry> bundles)
    {
        var sb = new StringBuilder();
        foreach (var b in bundles.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Path, StringComparer.OrdinalIgnoreCase))
            sb.Append(b.Name.ToLowerInvariant()).Append('|').Append(b.Path.ToLowerInvariant()).Append('|').Append(b.Length).Append('|').Append(b.LastWriteTicks).Append('\n');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }

    public static string HotCacheDir(GameEdition edition = GameEdition.China)
        => UserCachePath(edition == GameEdition.International ? "AstralParty_INT" : "AstralParty_CN");

    private static string UserCachePath(string product)
    {
        var profile = Environment.GetEnvironmentVariable("USERPROFILE");
        if (string.IsNullOrWhiteSpace(profile))
            profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(profile, "AppData", "LocalLow", "feimo", product,
            "com.unity.addressables", "AssetBundles");
    }

    public static string UserCacheFor(string gameDir)
    {
        if (string.IsNullOrWhiteSpace(gameDir)) return null;
        string product = GameLocator.ProductOf(RuntimeBundleLocator.GameDataDirectory(gameDir));
        if (product != null) return UserCachePath(product);
        foreach (string candidate in new[] { HotCacheDir(), HotCacheDir(GameEdition.International) })
            if (RuntimeBundleLocator.SamePath(gameDir, candidate)) return candidate;
        return null;
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

    public void Save(GameIndex idx, CancellationToken token = default)
    {
        string path = CachePath(idx.GameDir, idx.HeroOnly, idx.IncludeHotCache, idx.Recursive);
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = File.Create(temp)) JsonSerializer.Serialize(stream, idx);
            token.ThrowIfCancellationRequested();
            File.Move(temp, path, true);
        }
        finally { File.Delete(temp); }
    }

    public GameIndex Load(string gameDir, bool heroOnly = true, bool includeHotCache = false, bool recursive = false, CancellationToken token = default)
    {
        gameDir = ResourceLocator.NormalizeGameDirectory(gameDir);
        var cached = TryLoadSavedIndex(gameDir, heroOnly, includeHotCache, recursive, token);
        return cached;
    }

    private GameIndex TryLoadSavedIndex(string gameDir, bool heroOnly, bool includeHotCache, bool recursive, CancellationToken token)
    {
        var p = CachePath(gameDir, heroOnly, includeHotCache, recursive);
        if (!File.Exists(p)) return null;
        try
        {
            var idx = ReadCache(gameDir, heroOnly, includeHotCache, recursive);
            token.ThrowIfCancellationRequested();
            if (idx == null) return null;
            if ((idx.Lightweight ? idx.LightweightTextureCount == 0 : idx.Items.Count == 0)
                && ResourceLocator.HasWrappedResources(gameDir))
                return null;
            bool validShape = idx.Lightweight
                ? idx.TextureNamesByBundle != null && idx.BundlePathsByName != null && idx.TextureRefsByCategory != null
                : !idx.Items.Any(i => string.IsNullOrWhiteSpace(i.Kind) || string.IsNullOrWhiteSpace(i.BundlePath));
            string quickStamp = ComputeQuickSourceStamp(gameDir, includeHotCache, recursive);
            bool sourceMatches;
            if (includeHotCache && !recursive && !string.IsNullOrWhiteSpace(idx.QuickSourceStamp))
            {
                sourceMatches = string.Equals(idx.QuickSourceStamp, quickStamp, StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                sourceMatches = string.Equals(idx.SourceStamp,
                    ComputeStamp(EnumerateBundleEntries(gameDir, includeHotCache, recursive, token)), StringComparison.OrdinalIgnoreCase);
                if (sourceMatches && includeHotCache && !recursive)
                {
                    idx.QuickSourceStamp = quickStamp;
                    try { Save(idx, token); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                }
            }
            if (validShape && sourceMatches)
            {
                token.ThrowIfCancellationRequested();
                ApplySkillOwnership(idx);
                return idx;
            }
        }
        catch (OperationCanceledException) { throw; }
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
                HotDir = includeHotCache ? UserCacheFor(gameDir) ?? "" : "",
                GameCacheDir = includeHotCache ? RuntimeBundleLocator.GameDataCache(gameDir) : "",
                BuiltAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " / 参考轻索引",
                SourceStamp = ComputeStamp(bundles),
                QuickSourceStamp = ComputeQuickSourceStamp(gameDir, includeHotCache, recursive),
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

            if (idx.LightweightTextureCount <= 0) return null;
            ApplySkillOwnership(idx);
            return idx;
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

    private static void ApplySkillOwnership(GameIndex idx)
    {
        AddSkillMovies(idx);
        IEnumerable<string> bundleNames = idx.Lightweight
            ? idx.TextureNamesByBundle?.Keys ?? Enumerable.Empty<string>()
            : idx.Items?.Select(item => item.Bundle).Distinct(StringComparer.OrdinalIgnoreCase) ?? Enumerable.Empty<string>();
        var owners = CatalogOwnership.Load(idx.GameDir, bundleNames, idx.IncludeHotCache);
        if (owners.Count > 0) idx.CharacterOwnersByBundle = owners;
        idx.CharacterOwnersByBundle ??= new(StringComparer.OrdinalIgnoreCase);

        idx.TextureCategoryCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (idx.Lightweight)
        {
            idx.TextureRefsByCategory = new Dictionary<string, List<LightweightTextureRef>>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in idx.TextureNamesByBundle ?? new Dictionary<string, List<string>>())
            foreach (string name in pair.Value ?? new List<string>())
            {
                var entry = new TexIndexEntry
                {
                    Bundle = pair.Key,
                    Name = name,
                    Kind = ResourceKinds.Texture,
                    CategoryId = ResourceCategories.Categorize(ResourceKinds.Texture, name)
                };
                idx.ApplyCharacterOwnership(entry);
                idx.AddTextureCategoryCount(entry.CategoryId);
                if (ShouldStoreLightweightCategoryRef(entry.CategoryId))
                    idx.AddTextureCategoryRef(entry.CategoryId, pair.Key, name, entry);
            }
            foreach (var movie in idx.Items.Where(item => item.Kind == ResourceKinds.SkillMovie))
                idx.AddTextureCategoryCount(movie.CategoryId);
            return;
        }

        foreach (var entry in idx.Items ?? new List<TexIndexEntry>())
        {
            if (!ResourceKinds.InBrowser(entry.Kind, ResourceKinds.Texture)) continue;
            if (entry.IsSkillAnimation)
            {
                entry.IsSkillAnimation = false;
                entry.OwnerHeroId = null;
                entry.OwnerVariant = null;
                entry.OwnerIsMonster = false;
            }
            // Refresh naming rules from cached metadata without reopening unchanged bundles.
            if (entry.Kind == ResourceKinds.Texture)
            {
                entry.CategoryId = ResourceCategories.Categorize(entry.Kind, entry.Name, entry.Width, entry.Height);
                entry.CategoryLabel = ResourceCategories.Label(entry.Kind, entry.CategoryId);
            }
            idx.ApplyCharacterOwnership(entry);
            idx.AddTextureCategoryCount(entry.CategoryId);
        }
    }

    private static void AddSkillMovies(GameIndex idx)
    {
        idx.Items.RemoveAll(item => item.Kind == ResourceKinds.SkillMovie);
        var paths = new Dictionary<string, string>(idx.BundlePathsByName, StringComparer.OrdinalIgnoreCase);
        foreach (var stamp in idx.BundleStamps)
        {
            int separator = stamp.Value.IndexOf('|');
            if (separator <= 0) continue;
            string path = stamp.Value[..separator];
            string logical = Path.GetFileName(path) == "__data"
                ? Path.GetFileName(Path.GetDirectoryName(path)) + ".bundle" : Path.GetFileName(stamp.Key);
            paths[logical] = path;
        }
        foreach (var movie in CatalogOwnership.LoadSkillMovies(idx.GameDir, idx.IncludeHotCache))
        {
            string path = paths.GetValueOrDefault(movie.Bundle) ?? Path.Combine(idx.GameDir, movie.Bundle);
            if (!File.Exists(path)) continue;
            idx.Items.Add(new TexIndexEntry
            {
                Bundle = movie.Bundle, BundlePath = path, Name = movie.Name,
                Kind = ResourceKinds.SkillMovie, Format = "USM / 点选解析",
                CategoryId = ResourceCategories.CharacterId,
                CategoryLabel = ResourceCategories.Label(ResourceKinds.Texture, ResourceCategories.CharacterId),
                Source = !string.IsNullOrWhiteSpace(idx.GameCacheDir) && path.StartsWith(Path.TrimEndingDirectorySeparator(idx.GameCacheDir) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    ? "游戏安装缓存" : ResourceLocator.IsWrappedData(path) ? "热更缓存" : "基础包"
            });
        }
    }
}
