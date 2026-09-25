using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace JixModMaker;

public sealed class CharacterAssetOwner
{
    public string HeroId { get; set; }
    public string Variant { get; set; } = "";
    public bool IsMonster { get; set; }
    public string CatalogKey { get; set; }
}

public static class CatalogOwnership
{
    private const int OwnershipCacheVersion = 1;
    private static readonly Regex BundleName = new(@"(?<name>[0-9a-f]{32}\.bundle)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex HeroKey = new(@"^Hero(?<id>\d+)(?:_(?<skin>\d+|Max))?_PC$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex MonsterKey = new(@"^Monster(?<id>\d+)(?:_(?<skin>\d+|Max))?_PC$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex TalentFxKey = new(@"^Fx_(?<id>\d+)(?:_(?<skin>\d+|Max))?_Talent(?:_|$)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private sealed class OwnershipCache
    {
        public int Version { get; set; }
        public string CatalogPath { get; set; }
        public long CatalogLength { get; set; }
        public long CatalogWriteUtcTicks { get; set; }
        public Dictionary<string, CharacterAssetOwner> Owners { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public static Dictionary<string, CharacterAssetOwner> Load(string gameDir, IEnumerable<string> bundleNames)
    {
        var wanted = bundleNames
            .Select(name => Path.GetFileName((name ?? "").Replace('/', Path.DirectorySeparatorChar)))
            .Where(name => BundleName.IsMatch(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (wanted.Count == 0) return new(StringComparer.OrdinalIgnoreCase);

        var result = new Dictionary<string, CharacterAssetOwner>(StringComparer.OrdinalIgnoreCase);
        foreach (var catalog in CatalogCandidates(gameDir))
        {
            try
            {
                var owners = LoadAll(catalog);
                foreach (string bundle in wanted)
                    if (owners.TryGetValue(bundle, out var owner) &&
                        (!result.TryGetValue(bundle, out var old) || OwnerRank(owner) > OwnerRank(old)))
                        result[bundle] = owner;
            }
            catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException or FormatException) { }
        }
        return result;
    }

    public static CharacterAssetOwner ParseOwnerKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        foreach (var candidate in new[]
        {
            (Match: HeroKey.Match(key), Monster: false),
            (Match: MonsterKey.Match(key), Monster: true),
            (Match: TalentFxKey.Match(key), Monster: false)
        })
        {
            if (!candidate.Match.Success) continue;
            return new CharacterAssetOwner
            {
                HeroId = candidate.Match.Groups["id"].Value,
                Variant = candidate.Match.Groups["skin"].Value,
                IsMonster = candidate.Monster,
                CatalogKey = key
            };
        }
        return null;
    }

    private static Dictionary<string, CharacterAssetOwner> LoadAll(string path)
    {
        var catalog = new FileInfo(path);
        string fullPath = catalog.FullName;
        try
        {
            string cachePath = CachePath();
            if (File.Exists(cachePath))
            {
                var cache = JsonSerializer.Deserialize<OwnershipCache>(File.ReadAllText(cachePath));
                if (cache?.Version == OwnershipCacheVersion &&
                    string.Equals(cache.CatalogPath, fullPath, StringComparison.OrdinalIgnoreCase) &&
                    cache.CatalogLength == catalog.Length &&
                    cache.CatalogWriteUtcTicks == catalog.LastWriteTimeUtc.Ticks &&
                    cache.Owners?.Count > 0)
                    return new Dictionary<string, CharacterAssetOwner>(cache.Owners, StringComparer.OrdinalIgnoreCase);
            }
        }
        catch { }

        var owners = Parse(path);
        try
        {
            string cachePath = CachePath();
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
            File.WriteAllText(cachePath, JsonSerializer.Serialize(new OwnershipCache
            {
                Version = OwnershipCacheVersion,
                CatalogPath = fullPath,
                CatalogLength = catalog.Length,
                CatalogWriteUtcTicks = catalog.LastWriteTimeUtc.Ticks,
                Owners = owners
            }));
        }
        catch { }
        return owners;
    }

    private static Dictionary<string, CharacterAssetOwner> Parse(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        byte[] Bytes(string name) => Convert.FromBase64String(root.GetProperty(name).GetString() ?? "");
        var keyData = Bytes("m_KeyDataString");
        var bucketData = Bytes("m_BucketDataString");
        var entryData = Bytes("m_EntryDataString");
        int ReadInt(byte[] data, int offset)
        {
            if (offset < 0 || offset + 4 > data.Length) throw new InvalidDataException("Addressables catalog 数据不完整。");
            return BitConverter.ToInt32(data, offset);
        }

        var buckets = new List<(string Key, int[] Entries)>();
        int position = 4;
        int bucketCount = ReadInt(bucketData, 0);
        for (int i = 0; i < bucketCount; i++)
        {
            int keyOffset = ReadInt(bucketData, position); position += 4;
            int count = ReadInt(bucketData, position); position += 4;
            if (count < 0 || position + count * 4L > bucketData.Length)
                throw new InvalidDataException("Addressables bucket 表无效。");
            var ids = new int[count];
            for (int n = 0; n < count; n++) { ids[n] = ReadInt(bucketData, position); position += 4; }
            buckets.Add((ReadKey(keyData, keyOffset, ReadInt), ids));
        }

        var internalIds = root.GetProperty("m_InternalIds").EnumerateArray()
            .Select(item => item.GetString() ?? "").ToArray();
        int entryCount = ReadInt(entryData, 0);
        if (entryCount < 0 || 4 + entryCount * 28L > entryData.Length)
            throw new InvalidDataException("Addressables entry 表无效。");
        var dependencies = new int[entryCount];
        var direct = new Dictionary<int, HashSet<string>>();
        for (int id = 0; id < entryCount; id++)
        {
            int offset = 4 + id * 28;
            int internalId = ReadInt(entryData, offset);
            dependencies[id] = ReadInt(entryData, offset + 8);
            if (internalId < 0 || internalId >= internalIds.Length) continue;
            foreach (Match match in BundleName.Matches(internalIds[internalId]))
            {
                string bundle = match.Groups["name"].Value;
                if (!direct.TryGetValue(id, out var set)) direct[id] = set = new(StringComparer.OrdinalIgnoreCase);
                set.Add(bundle);
            }
        }

        var memo = new Dictionary<int, HashSet<string>>();
        HashSet<string> BundlesFor(int id, HashSet<int> visiting)
        {
            if (id < 0 || id >= entryCount) return new(StringComparer.OrdinalIgnoreCase);
            if (memo.TryGetValue(id, out var cached)) return cached;
            var result = direct.TryGetValue(id, out var own)
                ? new HashSet<string>(own, StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!visiting.Add(id)) return result;
            int dependency = dependencies[id];
            if (dependency >= 0 && dependency < buckets.Count)
                foreach (int child in buckets[dependency].Entries) result.UnionWith(BundlesFor(child, visiting));
            visiting.Remove(id);
            return memo[id] = result;
        }

        var owners = new Dictionary<string, CharacterAssetOwner>(StringComparer.OrdinalIgnoreCase);
        foreach (var bucket in buckets)
        {
            var owner = ParseOwnerKey(bucket.Key);
            if (owner == null) continue;
            var related = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (int id in bucket.Entries) related.UnionWith(BundlesFor(id, new()));
            foreach (string bundle in related)
            {
                if (!owners.TryGetValue(bundle, out var old) || OwnerRank(owner) > OwnerRank(old))
                    owners[bundle] = owner;
            }
        }
        return owners;
    }

    private static int OwnerRank(CharacterAssetOwner owner) => owner.CatalogKey.StartsWith("Hero", StringComparison.OrdinalIgnoreCase) ? 3
        : owner.IsMonster ? 2 : 1;

    private static string CachePath()
    {
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JixModMaker");
        return Path.Combine(dir, $"catalog_owners_v{OwnershipCacheVersion}.json");
    }

    private static string ReadKey(byte[] data, int offset, Func<byte[], int, int> readInt)
    {
        if (offset < 0 || offset + 5 > data.Length) return "";
        int length = readInt(data, offset + 1);
        if (length < 0) return "";
        return data[offset] switch
        {
            0 when offset + 5L + length <= data.Length => Encoding.ASCII.GetString(data, offset + 5, length),
            1 when offset + 5L + length <= data.Length => Encoding.Unicode.GetString(data, offset + 5, length),
            _ => ""
        };
    }

    private static IEnumerable<string> CatalogCandidates(string gameDir)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hotParent = Directory.GetParent(IndexService.HotCacheDir())?.FullName;
        foreach (var root in new[] { hotParent, gameDir, Directory.GetParent(gameDir ?? "")?.FullName })
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) continue;
            foreach (var path in Directory.EnumerateFiles(root, "catalog*.json", SearchOption.TopDirectoryOnly)
                .OrderByDescending(File.GetLastWriteTimeUtc))
                if (seen.Add(path)) yield return path;
        }
    }
}
