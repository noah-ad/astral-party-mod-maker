using System.Buffers.Binary;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace JixModMaker;

public sealed class AndroidTextureCatalog
{
    public sealed record Location(string Name, int AssetEntry, int BundleEntry, string InternalId, string BundleName, string Hash)
    {
        public string BundleFile => InternalId.Replace('\\', '/').Split('/').Last();
        public string CachePath => BundleName + "/" + Hash + "/__data";
        public bool InApk => InternalId.StartsWith("{UnityEngine.AddressableAssets.Addressables.RuntimePath}/Android/", StringComparison.Ordinal);
        public string ModPath
        {
            get
            {
                const string prefix = "{UnityEngine.Application.persistentDataPath}/com.unity.addressables/";
                string path = InternalId.StartsWith(prefix, StringComparison.Ordinal) ? InternalId[prefix.Length..] : null;
                return path != null && Regex.IsMatch(path, @"^JixModResources/[0-9]{14}-[0-9a-f]{8}/[0-9a-f]{32}\.bundle$") ? path : null;
            }
        }
    }

    private readonly JsonObject _root;
    private readonly byte[] _entries;
    private readonly byte[] _extra;
    private readonly string[] _internalIds;
    private readonly Dictionary<string, Location> _textures = new(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, Location> Textures => _textures;

    public AndroidTextureCatalog(byte[] json)
    {
        _root = JsonNode.Parse(json)?.AsObject() ?? throw Invalid("JSON");
        byte[] Data(string name) => Convert.FromBase64String(_root[name]?.GetValue<string>() ?? throw Invalid(name));
        byte[] keys = Data("m_KeyDataString"), buckets = Data("m_BucketDataString");
        _entries = Data("m_EntryDataString");
        _extra = Data("m_ExtraDataString");
        _internalIds = _root["m_InternalIds"]?.AsArray().Select(node => node?.GetValue<string>() ?? throw Invalid("InternalId")).ToArray()
            ?? throw Invalid("InternalIds");
        string[] prefixes = _root["m_InternalIdPrefixes"]?.AsArray().Select(node => node!.GetValue<string>()).ToArray();
        for (int i = 0; prefixes != null && i < _internalIds.Length; i++)
        {
            int separator = _internalIds[i].IndexOf('#');
            if (separator > 0 && int.TryParse(_internalIds[i][..separator], out int prefix))
            {
                if (prefix < 0 || prefix >= prefixes.Length) throw Invalid("InternalId prefix");
                _internalIds[i] = prefixes[prefix] + _internalIds[i][(separator + 1)..];
            }
        }
        int entryCount = Read(_entries, 0);
        if (entryCount < 0 || 4L + entryCount * 28L != _entries.Length) throw Invalid("entry table");
        var table = new List<(string Key, int[] Entries)>();
        int bucketCount = Read(buckets, 0), cursor = 4;
        if (bucketCount < 0 || bucketCount > buckets.Length / 8) throw Invalid("bucket count");
        for (int i = 0; i < bucketCount; i++)
        {
            int keyOffset = Read(buckets, cursor), count = Read(buckets, cursor + 4);
            cursor += 8;
            if (count < 0 || cursor + 4L * count > buckets.Length) throw Invalid("bucket entries");
            int[] ids = new int[count];
            for (int n = 0; n < count; n++)
            {
                ids[n] = Read(buckets, cursor);
                cursor += 4;
                if (ids[n] < 0 || ids[n] >= entryCount) throw Invalid("entry index");
            }
            string key = "";
            if (keyOffset >= 0 && keyOffset < keys.Length && keys[keyOffset] is 0 or 1)
            {
                int length = Read(keys, keyOffset + 1);
                if (length < 0 || keyOffset + 5L + length > keys.Length || (keys[keyOffset] == 1 && length % 2 != 0)) throw Invalid("key");
                key = (keys[keyOffset] == 0 ? Encoding.UTF8 : Encoding.Unicode).GetString(keys, keyOffset + 5, length);
            }
            table.Add((key, ids));
        }
        if (cursor != buckets.Length) throw Invalid("bucket length");
        var types = _root["m_resourceTypes"]?.AsArray() ?? throw Invalid("types");
        var providers = _root["m_ProviderIds"]?.AsArray() ?? throw Invalid("providers");
        foreach (var bucket in table.Where(bucket => bucket.Key.StartsWith("UT_", StringComparison.Ordinal)))
        {
            int[] textures = bucket.Entries.Where(id =>
            {
                int type = Field(id, 6);
                return type >= 0 && type < types.Count && types[type]?["m_ClassName"]?.GetValue<string>() == "UnityEngine.Texture2D";
            }).ToArray();
            if (textures.Length == 0) continue;
            if (textures.Length != 1) throw Invalid("duplicate texture: " + bucket.Key);
            int asset = textures[0], dependency = Field(asset, 2);
            if (dependency < 0 || dependency >= table.Count || table[dependency].Entries.Length == 0) throw Invalid("texture dependency");
            int bundleEntry = table[dependency].Entries[0], internalIndex = Field(bundleEntry, 0), provider = Field(bundleEntry, 1);
            if (internalIndex < 0 || internalIndex >= _internalIds.Length || provider < 0 || provider >= providers.Count
                || providers[provider]?.GetValue<string>() != "UnityEngine.ResourceManagement.ResourceProviders.AssetBundleProvider")
                throw Invalid("bundle provider");
            var options = Options(bundleEntry).Json;
            string bundle = options["m_BundleName"]?.GetValue<string>(), hash = options["m_Hash"]?.GetValue<string>();
            if (!Hex(bundle) || !Hex(hash)) throw Invalid("bundle hash");
            string internalId = _internalIds[internalIndex];
            // Some local filenames use a content hash distinct from the options hash.
            string file = internalId.Replace('\\', '/').Split('/').Last();
            if (file.Length != 39 || !file.EndsWith(".bundle", StringComparison.Ordinal) || !Hex(file[..32])) throw Invalid("bundle filename");
            if (!_textures.TryAdd(bucket.Key, new(bucket.Key, asset, bundleEntry, internalId, bundle, hash))) throw Invalid("duplicate key");
        }
    }

    public byte[] Redirect(IReadOnlyDictionary<int, (string Path, long Size)> replacements)
    {
        var root = (JsonObject)_root.DeepClone();
        byte[] entries = (byte[])_entries.Clone();
        using var extra = new MemoryStream();
        extra.Write(_extra);
        using var writer = new BinaryWriter(extra, Encoding.UTF8, true);
        var allowed = _textures.Values.Select(location => location.BundleEntry).ToHashSet();
        foreach (var pair in replacements.OrderBy(pair => pair.Key))
        {
            if (!allowed.Contains(pair.Key) || pair.Value.Size <= 0) throw Invalid("unselected bundle");
            string path = FileIntegrity.Relative(pair.Value.Path);
            if (!path.StartsWith("JixModResources/", StringComparison.Ordinal) || !path.EndsWith(".bundle", StringComparison.Ordinal)) throw Invalid("mod path");
            var ids = root["m_InternalIds"]!.AsArray();
            int index = ids.Count;
            ids.Add("{UnityEngine.Application.persistentDataPath}/com.unity.addressables/" + path);
            BinaryPrimitives.WriteInt32LittleEndian(entries.AsSpan(4 + pair.Key * 28, 4), index);
            var options = Options(pair.Key);
            options.Json["m_Crc"] = 0;
            options.Json["m_BundleSize"] = pair.Value.Size;
            options.Json["m_UseUWRForLocalBundles"] = false;
            int offset = checked((int)extra.Length);
            writer.Write((byte)7);
            WritePart(options.Assembly);
            WritePart(options.Type);
            byte[] json = Encoding.Unicode.GetBytes(options.Json.ToJsonString());
            writer.Write(json.Length);
            writer.Write(json);
            BinaryPrimitives.WriteInt32LittleEndian(entries.AsSpan(4 + pair.Key * 28 + 16, 4), offset);
        }
        root["m_EntryDataString"] = Convert.ToBase64String(entries);
        root["m_ExtraDataString"] = Convert.ToBase64String(extra.ToArray());
        byte[] result = Encoding.UTF8.GetBytes(root.ToJsonString());
        _ = new AndroidTextureCatalog(result);
        return result;

        void WritePart(string text)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            if (bytes.Length > 255) throw Invalid("type name");
            writer.Write((byte)bytes.Length);
            writer.Write(bytes);
        }
    }

    public static void VerifyUpdate(byte[] original, byte[] updated)
    {
        var before = new AndroidTextureCatalog(original);
        var after = new AndroidTextureCatalog(updated);
        foreach (var pair in before._root)
            if (pair.Key is not "m_InternalIds" and not "m_EntryDataString" and not "m_ExtraDataString"
                && !JsonNode.DeepEquals(pair.Value, after._root[pair.Key])) throw Invalid("unrelated catalog property changed");
        if (before._root.Count != after._root.Count || before._entries.Length != after._entries.Length
            || !after._extra.AsSpan().StartsWith(before._extra)) throw Invalid("catalog layout changed");
        var oldIds = before._root["m_InternalIds"]!.AsArray();
        var newIds = after._root["m_InternalIds"]!.AsArray();
        if (newIds.Count < oldIds.Count || oldIds.Where((node, index) => !JsonNode.DeepEquals(node, newIds[index])).Any()) throw Invalid("existing internal IDs changed");
        var allowed = before._textures.Values.Select(location => location.BundleEntry).ToHashSet();
        var redirected = after._textures.Values.Where(location => location.ModPath != null).Select(location => location.BundleEntry).ToHashSet();
        int count = Read(before._entries, 0);
        for (int entry = 0; entry < count; entry++)
        {
            bool changed = false;
            for (int column = 0; column < 7; column++)
            {
                if (before.Field(entry, column) == after.Field(entry, column)) continue;
                if (column is not 0 and not 4 || !allowed.Contains(entry) || !redirected.Contains(entry)) throw Invalid("non-texture entry changed");
                changed = true;
            }
            if (!changed) continue;
            if (after.Field(entry, 0) < oldIds.Count || after.Field(entry, 4) < before._extra.Length) throw Invalid("replacement reused an existing ID or option record");
            var previous = before.Options(entry);
            var current = after.Options(entry);
            static bool UnchangedOption(string key) => key is not "m_Crc" and not "m_BundleSize" and not "m_UseUWRForLocalBundles";
            if (previous.Assembly != current.Assembly || previous.Type != current.Type
                || !previous.Json.Select(pair => pair.Key).Where(UnchangedOption).ToHashSet(StringComparer.Ordinal)
                    .SetEquals(current.Json.Select(pair => pair.Key).Where(UnchangedOption))
                || previous.Json.Any(pair => pair.Key is not "m_Crc" and not "m_BundleSize" and not "m_UseUWRForLocalBundles"
                    && !JsonNode.DeepEquals(pair.Value, current.Json[pair.Key]))
                || current.Json["m_Crc"]?.GetValue<uint>() != 0 || current.Json["m_BundleSize"]?.GetValue<long>() <= 0
                || current.Json["m_UseUWRForLocalBundles"]?.GetValue<bool>() != false) throw Invalid("unrelated bundle options changed");
        }
    }

    private (string Assembly, string Type, JsonObject Json) Options(int entry)
    {
        int cursor = Field(entry, 4);
        if (cursor < 0 || cursor >= _extra.Length || _extra[cursor++] != 7) throw Invalid("options type");
        string Part()
        {
            if (cursor >= _extra.Length) throw Invalid("options type length");
            int length = _extra[cursor++];
            if (cursor + length > _extra.Length) throw Invalid("options type data");
            string value = Encoding.UTF8.GetString(_extra, cursor, length);
            cursor += length;
            return value;
        }
        string assembly = Part(), type = Part();
        if (type != "UnityEngine.ResourceManagement.ResourceProviders.AssetBundleRequestOptions") throw Invalid("options class");
        int count = Read(_extra, cursor);
        cursor += 4;
        if (count < 0 || count % 2 != 0 || cursor + (long)count > _extra.Length) throw Invalid("options JSON");
        return (assembly, type, JsonNode.Parse(Encoding.Unicode.GetString(_extra, cursor, count))?.AsObject() ?? throw Invalid("options JSON"));
    }

    private int Field(int entry, int column) => Read(_entries, checked(4 + entry * 28 + column * 4));
    private static bool Hex(string value) => value?.Length == 32 && value.All(Uri.IsHexDigit);
    private static int Read(byte[] bytes, int offset)
    {
        if (offset < 0 || offset > bytes.Length - 4) throw Invalid("bounds");
        return BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset, 4));
    }
    private static InvalidDataException Invalid(string field) => new("安卓资源表无效，未修改设备文件：" + field);
}
