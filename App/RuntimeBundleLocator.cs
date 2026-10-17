using System.Text;
using System.Text.Json;

namespace JixModMaker;

public static class RuntimeBundleLocator
{
    public sealed record Target(string Path, string Catalog, string Bundle, string Hash, string CacheRoot);

    public static string GameDataCache(string gameDirectory)
    {
        if (string.IsNullOrWhiteSpace(gameDirectory)) return null;
        for (var directory = new DirectoryInfo(Path.GetFullPath(gameDirectory)); directory != null; directory = directory.Parent)
            if (directory.Name.EndsWith("_Data", StringComparison.OrdinalIgnoreCase) &&
                File.Exists(Path.Combine(directory.FullName, "StreamingAssets", "aa", "settings.json")))
                return Path.Combine(directory.FullName, "com.unity.addressables", "AssetBundles");
        return null;
    }

    public static bool UsesDataPath(string cacheRoot, string gameDirectory = null) =>
        SamePath(cacheRoot, GameDataCache(gameDirectory ?? cacheRoot));

    public static IReadOnlyList<string> CacheRoots(string cacheRoot, string gameDirectory = null)
    {
        string dataCache = GameDataCache(gameDirectory ?? cacheRoot);
        string dataDirectory = dataCache == null ? null : Directory.GetParent(Directory.GetParent(dataCache)!.FullName)!.FullName;
        string executable = dataDirectory == null ? null : Path.Combine(Directory.GetParent(dataDirectory)!.FullName,
            Path.GetFileName(dataDirectory)[..^"_Data".Length] + ".exe");
        // A real local player shares its user catalog with the installation cache. Isolated resource copies do not.
        string userCache = File.Exists(executable) ? IndexService.HotCacheDir() : null;
        return new[] { cacheRoot, dataCache, userCache }.Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path))).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static Target Resolve(string cacheRoot, string gameDirectory = null)
    {
        cacheRoot = Path.GetFullPath(cacheRoot);
        if (string.IsNullOrWhiteSpace(gameDirectory))
        {
            string dataCache = GameDataCache(cacheRoot);
            if (dataCache != null)
                gameDirectory = Path.Combine(Directory.GetParent(Directory.GetParent(dataCache)!.FullName)!.FullName,
                    "StreamingAssets", "aa", "StandaloneWindows64");
            else if (SamePath(cacheRoot, IndexService.HotCacheDir())) gameDirectory = GameLocator.Find()?.AaDir;
        }
        string settings = SettingsPath(gameDirectory);
        var cacheRoots = CacheRoots(cacheRoot, gameDirectory);
        var roots = cacheRoots.SelectMany(root => new[] { Directory.GetParent(root)?.FullName, root })
            .Concat(new[] { gameDirectory, string.IsNullOrWhiteSpace(gameDirectory) ? null : Directory.GetParent(gameDirectory)?.FullName })
            .Where(Directory.Exists).Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        string catalog;
        if (settings != null)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(settings));
            var names = document.RootElement.GetProperty("m_CatalogLocations").EnumerateArray()
                .Where(location => location.GetProperty("m_Keys").EnumerateArray().Any(key => key.GetString() == "AddressablesMainContentCatalogCacheHash"))
                .Select(location => Path.ChangeExtension(Path.GetFileName(location.GetProperty("m_InternalId").GetString()!.Replace('\\', '/')), ".json"))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (names.Length != 1) throw new InvalidDataException("无法确定当前游戏的资源版本，未修改任何资源。");
            var catalogs = roots.Select(root => Path.Combine(root, names[0])).Where(File.Exists).ToArray();
            if (catalogs.Length == 0)
                throw new FileNotFoundException("缺少当前游戏的 " + names[0] + "，请先进入游戏完成资源更新；不会使用旧版本缓存。");
            if (catalogs.Skip(1).Any(path => !File.ReadAllBytes(path).AsSpan().SequenceEqual(File.ReadAllBytes(catalogs[0]))))
                throw new InvalidDataException("同版本 catalog 内容不一致，无法安全确定当前程序集。未修改任何资源。");
            catalog = catalogs[0];
        }
        else
        {
            var candidates = roots.SelectMany(root => Directory.EnumerateFiles(root, "catalog*.json", SearchOption.TopDirectoryOnly))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (candidates.Length == 0) return null;
            if (candidates.Length != 1) throw new InvalidDataException("发现多个版本的 catalog，但缺少当前游戏 settings.json，无法安全确定程序集。请从当前游戏目录进入动态替换。");
            catalog = candidates[0];
        }
        return ResolveCatalog(cacheRoot, catalog, gameDirectory);
    }

    public static Target ResolveCatalog(string cacheRoot, string catalog, string gameDirectory = null)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(catalog));
        var root = document.RootElement;
        byte[] Data(string name) => Convert.FromBase64String(root.GetProperty(name).GetString()!);
        byte[] keys = Data("m_KeyDataString"), buckets = Data("m_BucketDataString"), entries = Data("m_EntryDataString");
        int Read(byte[] bytes, int offset)
        {
            if (offset < 0 || offset > bytes.Length - 4) throw new InvalidDataException("当前 catalog 表不完整。");
            return BitConverter.ToInt32(bytes, offset);
        }
        var table = new List<int[]>();
        int position = 4, runtimeBucket = -1;
        int count = Read(buckets, 0);
        if (count < 0 || count > buckets.Length / 8) throw new InvalidDataException("当前 catalog bucket 表无效。");
        for (int i = 0; i < count; i++)
        {
            int keyOffset = Read(buckets, position), length = Read(buckets, position + 4); position += 8;
            if (length < 0 || position + length * 4L > buckets.Length) throw new InvalidDataException("当前 catalog 依赖表无效。");
            var ids = new int[length];
            for (int n = 0; n < length; n++) { ids[n] = Read(buckets, position); position += 4; }
            table.Add(ids);
            if (keyOffset < 0 || keyOffset > keys.Length - 5) continue;
            int size = Read(keys, keyOffset + 1);
            if (size < 0 || keyOffset + 5L + size > keys.Length) throw new InvalidDataException("当前 catalog 资源名无效。");
            string key = keys[keyOffset] switch
            {
                0 => Encoding.UTF8.GetString(keys, keyOffset + 5, size),
                1 => Encoding.Unicode.GetString(keys, keyOffset + 5, size),
                _ => ""
            };
            if (key == "AstralParty.Runtime.dll")
            {
                if (runtimeBucket >= 0) throw new InvalidDataException("当前 catalog 中的程序集资源不唯一。");
                runtimeBucket = i;
            }
        }
        int entryCount = Read(entries, 0);
        if (entryCount < 0 || 4L + entryCount * 28L > entries.Length || runtimeBucket < 0 || table[runtimeBucket].Length != 1)
            throw new InvalidDataException("当前 catalog 缺少唯一的 AstralParty.Runtime.dll。");
        int Field(int id, int column)
        {
            if (id < 0 || id >= entryCount) throw new InvalidDataException("当前 catalog 资源索引无效。");
            return Read(entries, 4 + id * 28 + column * 4);
        }
        int dependency = Field(table[runtimeBucket][0], 2);
        if (dependency < 0 || dependency >= table.Count || table[dependency].Length == 0)
            throw new InvalidDataException("当前 catalog 缺少程序集资源包依赖。");
        int primary = table[dependency][0];
        string internalId = root.GetProperty("m_InternalIds")[Field(primary, 0)].GetString()!;
        string bundleFile = Path.GetFileName(internalId.Replace('\\', '/'));
        if (bundleFile.Length != 39 || !bundleFile.EndsWith(".bundle", StringComparison.OrdinalIgnoreCase) || !bundleFile[..32].All(Uri.IsHexDigit))
            throw new InvalidDataException("当前程序集资源包名称无效。");

        byte[] extra = Data("m_ExtraDataString");
        int cursor = Field(primary, 4);
        if (cursor < 0 || cursor >= extra.Length || extra[cursor++] != 7) throw new InvalidDataException("缺少程序集缓存定位信息。");
        string ReadTypePart()
        {
            if (cursor >= extra.Length) throw new InvalidDataException("程序集缓存定位信息不完整。");
            int size = extra[cursor++];
            if (cursor + size > extra.Length) throw new InvalidDataException("程序集缓存类型信息无效。");
            string value = Encoding.UTF8.GetString(extra, cursor, size); cursor += size; return value;
        }
        ReadTypePart();
        if (ReadTypePart() != "UnityEngine.ResourceManagement.ResourceProviders.AssetBundleRequestOptions")
            throw new InvalidDataException("程序集资源包的缓存类型已变化。");
        int jsonSize = Read(extra, cursor); cursor += 4;
        if (jsonSize < 0 || cursor + (long)jsonSize > extra.Length) throw new InvalidDataException("程序集缓存选项无效。");
        using var options = JsonDocument.Parse(Encoding.Unicode.GetString(extra, cursor, jsonSize));
        string bundle = options.RootElement.GetProperty("m_BundleName").GetString()!;
        string hash = options.RootElement.GetProperty("m_Hash").GetString()!;
        bool Valid(string name) => name?.Length == 32 && name.All(Uri.IsHexDigit);
        if (!Valid(bundle) || !Valid(hash)) throw new InvalidDataException("程序集缓存名称或版本哈希无效。");
        var candidates = CacheRoots(cacheRoot, gameDirectory)
            .Select(root => (Root: root, Path: Path.Combine(root, bundle, hash, "__data"))).ToArray();
        var installed = candidates.Where(candidate => File.Exists(candidate.Path)).ToArray();
        if (installed.Length > 1)
            throw new InvalidDataException("用户缓存与游戏安装缓存均存在当前程序集，无法确定游戏的读取位置。已停止写入，请保留两处资源：\n" +
                string.Join("\n", installed.Select(candidate => candidate.Path)));
        if (installed.Length == 0)
            throw new FileNotFoundException($"当前 {Path.GetFileNameWithoutExtension(catalog)} 的程序集在用户缓存和游戏安装缓存中均未找到。请先进入游戏完成资源更新。不会写入旧包。\n" +
                string.Join("\n", candidates.Select(candidate => candidate.Path)), candidates[0].Path);
        return new(installed[0].Path, Path.GetFullPath(catalog), bundleFile, hash, installed[0].Root);
    }

    private static string SettingsPath(string gameDirectory)
    {
        if (string.IsNullOrWhiteSpace(gameDirectory)) return null;
        foreach (string directory in new[] { gameDirectory, Directory.GetParent(Path.GetFullPath(gameDirectory))?.FullName })
        {
            if (directory == null) continue;
            string path = Path.Combine(directory, "settings.json");
            if (File.Exists(path)) return path;
        }
        return null;
    }

    public static bool SamePath(string first, string second) => !string.IsNullOrWhiteSpace(first) && !string.IsNullOrWhiteSpace(second)
        && string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(first)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(second)), StringComparison.OrdinalIgnoreCase);
}
