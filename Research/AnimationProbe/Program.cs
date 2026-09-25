using System.Text;
using System.Text.Json;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using Mono.Cecil;

if (args[0] == "sprite-dump")
{
    var manager = new AssetsManager();
    manager.LoadClassPackage(Path.GetFullPath("libs/classdata.tpk"));
    var file = manager.LoadAssetsFileFromBundle(manager.LoadBundleFile(args[1]), 0, false);
    manager.LoadClassDatabaseFromPackage(file.file.Metadata.UnityVersion);
    var info = file.file.GetAssetsOfType(AssetClassID.Sprite)
        .Single(item => manager.GetBaseField(file, item)["m_Name"].AsString == args[2]);
    void Dump(AssetTypeValueField field, int depth)
    {
        bool binary = field.Value != null && field.Value.ValueType == AssetValueType.ByteArray;
        string value = binary ? Convert.ToHexString(field.AsByteArray) : field.Value?.ToString();
        Console.WriteLine(new string(' ', depth) + field.FieldName + " (" + field.TypeName + ") " + value);
        foreach (var child in field.Children) Dump(child, depth + 1);
    }
    Dump(manager.GetBaseField(file, info)["m_RD"], 0);
    manager.UnloadAll();
    return;
}

if (args[0] == "sprite-series")
{
    var manager = new AssetsManager();
    manager.LoadClassPackage(Path.GetFullPath("libs/classdata.tpk"));
    var file = manager.LoadAssetsFileFromBundle(manager.LoadBundleFile(args[1]), 0, false);
    manager.LoadClassDatabaseFromPackage(file.file.Metadata.UnityVersion);
    foreach (var info in file.file.GetAssetsOfType(AssetClassID.Sprite))
    {
        var field = manager.GetBaseField(file, info);
        string name = field["m_Name"].AsString;
        if (!name.StartsWith(args[2], StringComparison.OrdinalIgnoreCase)) continue;
        var source = field["m_Rect"];
        var packed = field["m_RD"]["textureRect"];
        Console.WriteLine($"{name}\tsource={source["x"].AsFloat},{source["y"].AsFloat},{source["width"].AsFloat},{source["height"].AsFloat}" +
            $"\tpacked={packed["x"].AsFloat},{packed["y"].AsFloat},{packed["width"].AsFloat},{packed["height"].AsFloat}" +
            $"\ttexture={field["m_RD"]["texture"]["m_PathID"].AsLong}");
    }
    manager.UnloadAll();
    return;
}

if (args[0] == "catalog-inverse")
{
    using var index = JsonDocument.Parse(File.ReadAllText(args[2]));
    var wanted = index.RootElement.GetProperty("Items").EnumerateArray()
        .Where(item => item.GetProperty("CategoryId").GetString() == "sprite_anim" &&
            item.GetProperty("Name").GetString()?.StartsWith("Talent", StringComparison.OrdinalIgnoreCase) == true)
        .Select(item => Path.GetFileName(item.GetProperty("Bundle").GetString()))
        .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    if (args.Length > 3) wanted = wanted.Where(bundle => bundle.Equals(args[3], StringComparison.OrdinalIgnoreCase)).ToArray();
    using var catalog = JsonDocument.Parse(File.ReadAllText(args[1]));
    var root = catalog.RootElement;
    byte[] Bytes(string name) => Convert.FromBase64String(root.GetProperty(name).GetString());
    var keys = Bytes("m_KeyDataString");
    var bucketData = Bytes("m_BucketDataString");
    var entryData = Bytes("m_EntryDataString");
    int Int(byte[] data, int offset) => BitConverter.ToInt32(data, offset);
    var buckets = new List<(string Key, int[] Entries)>();
    int position = 4;
    for (int i = 0; i < Int(bucketData, 0); i++)
    {
        int offset = Int(bucketData, position); position += 4;
        int count = Int(bucketData, position); position += 4;
        string key = keys[offset] switch {
            0 => Encoding.ASCII.GetString(keys, offset + 5, Int(keys, offset + 1)),
            1 => Encoding.Unicode.GetString(keys, offset + 5, Int(keys, offset + 1)),
            _ => $"<key:{i}>"
        };
        int[] ids = new int[count];
        for (int n = 0; n < count; n++) { ids[n] = Int(bucketData, position); position += 4; }
        buckets.Add((key, ids));
    }
    var internalIds = root.GetProperty("m_InternalIds").EnumerateArray().Select(item => item.GetString()).ToArray();
    int entryCount = Int(entryData, 0);
    int[] dependencies = Enumerable.Range(0, entryCount).Select(id => Int(entryData, 4 + id * 28 + 8)).ToArray();
    var direct = new Dictionary<int, HashSet<string>>();
    for (int id = 0; id < entryCount; id++)
    {
        string internalId = internalIds[Int(entryData, 4 + id * 28)];
        foreach (string bundle in wanted)
            if (internalId.Contains(bundle, StringComparison.OrdinalIgnoreCase))
                (direct.TryGetValue(id, out var set) ? set : direct[id] = new(StringComparer.OrdinalIgnoreCase)).Add(bundle);
    }
    var memo = new Dictionary<int, HashSet<string>>();
    HashSet<string> BundlesFor(int id, HashSet<int> visiting)
    {
        if (memo.TryGetValue(id, out var cached)) return cached;
        var result = direct.TryGetValue(id, out var own) ? new HashSet<string>(own, StringComparer.OrdinalIgnoreCase) : new(StringComparer.OrdinalIgnoreCase);
        if (!visiting.Add(id)) return result;
        int dependency = dependencies[id];
        if (dependency >= 0 && dependency < buckets.Count)
            foreach (int child in buckets[dependency].Entries) result.UnionWith(BundlesFor(child, visiting));
        visiting.Remove(id);
        return memo[id] = result;
    }
    var owners = wanted.ToDictionary(bundle => bundle, _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
    foreach (var bucket in buckets)
    {
        bool usefulKey = args.Length > 3 || bucket.Key.Any(char.IsDigit) && (bucket.Key.Contains("Skill", StringComparison.OrdinalIgnoreCase) ||
            bucket.Key.Contains("Hero", StringComparison.OrdinalIgnoreCase) || bucket.Key.Contains("Talent", StringComparison.OrdinalIgnoreCase));
        if (!usefulKey) continue;
        var related = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (int id in bucket.Entries) related.UnionWith(BundlesFor(id, new()));
        foreach (string bundle in related) owners[bundle].Add(bucket.Key);
    }
    foreach (var pair in owners.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        Console.WriteLine(pair.Key + "\t" + string.Join(" | ", pair.Value.OrderBy(key => key, StringComparer.OrdinalIgnoreCase)));
    return;
}

if (args[0] == "type")
{
    using var module = ModuleDefinition.ReadModule(args[1]);
    var type = module.GetType(args[2]) ?? throw new Exception("Missing type");
    Console.WriteLine(type.FullName + " : " + type.BaseType + " " + type.Attributes);
    foreach (var field in type.Fields) Console.WriteLine("FIELD " + field.Attributes + " " + field.FieldType + " " + field.Name);
    foreach (var property in type.Properties) Console.WriteLine("PROPERTY " + property.PropertyType + " " + property.Name);
    foreach (var method in type.Methods) Console.WriteLine("METHOD " + method.Attributes + " " + method.FullName);
    return;
}

if (args[0] == "verify-native-slot")
{
    using var module = ModuleDefinition.ReadModule(args[1]);
    if (module.Types.Any(type => type.Namespace is "Jix.DynamicPortrait" or "Jix.DynamicRendering"))
        throw new Exception("Independent dynamic-resource types are still present");
    var config = module.Types.Single(type => type.FullName == "SkinStandingPaintingConfigureItem");
    var helper = config.Methods.Single(method => method.Name == "JixMapAnimatedPortrait");
    var strings = helper.Body.Instructions.Where(instruction => instruction.OpCode == Mono.Cecil.Cil.OpCodes.Ldstr)
        .Select(instruction => instruction.Operand as string).ToArray();
    if (strings.Length != 2 || strings[1] != "VHandCard_13021002")
        throw new Exception("Unexpected native-slot mapping");
    Console.WriteLine("PASS native slot mapping: " + strings[0] + " -> " + strings[1]);
    return;
}

if (args[0] == "structure")
{
    var manager = new AssetsManager();
    var bundle = manager.LoadBundleFile(args[1]);
    var file = manager.LoadAssetsFileFromBundle(bundle, 0, false);
    var jsonOptions = new JsonSerializerOptions { IncludeFields = true };
    Console.WriteLine("LIB " + typeof(AssetsFile).Assembly.FullName);
    Console.WriteLine("BUNDLE " + JsonSerializer.Serialize(bundle.file.Header, jsonOptions));
    Console.WriteLine("HEADER " + JsonSerializer.Serialize(file.file.Header, jsonOptions));
    Console.WriteLine("META " + file.file.Metadata.UnityVersion + " target=" + file.file.Metadata.TargetPlatform);
    foreach (var t in file.file.Metadata.TypeTreeTypes)
        Console.WriteLine("TYPE " + JsonSerializer.Serialize(t, jsonOptions));
    foreach (var info in file.file.AssetInfos)
        Console.WriteLine("OBJECT " + JsonSerializer.Serialize(info, jsonOptions));
    if (args.Length > 2)
    {
        file.file.Reader.Position = 0;
        File.WriteAllBytes(args[2], file.file.Reader.ReadBytes((int)file.file.Header.FileSize));
    }
    manager.UnloadAll();
    return;
}

if (args[0] == "find")
{
    var manager = new AssetsManager();
    int scanned = 0, failed = 0;
    foreach (string path in Directory.EnumerateFiles(args[1], "__data", SearchOption.AllDirectories))
    {
        if (path.Contains("_原始备份")) continue;
        long size = new FileInfo(path).Length;
        if (args.Length > 3 && size < long.Parse(args[3])) continue;
        try
        {
            var bundle = manager.LoadBundleFile(path);
            var file = manager.LoadAssetsFileFromBundle(bundle, 0, false);
            foreach (var info in file.file.AssetInfos.Where(i => i.TypeId == (int)AssetClassID.TextAsset || i.TypeId == (int)AssetClassID.MonoBehaviour))
            {
                var reader = file.file.Reader;
                reader.Position = info.GetAbsoluteByteStart(file.file) + (info.TypeId == (int)AssetClassID.MonoBehaviour ? 28 : 0);
                string name = reader.ReadCountStringInt32();
                if (name == args[2])
                {
                    Console.WriteLine($"FOUND {name} PLATFORM={file.file.Metadata.TargetPlatform} {path}");
                    return;
                }
            }
            scanned++;
        }
        catch { failed++; }
        finally { manager.UnloadAll(); }
    }
    Console.WriteLine($"NOT FOUND; scanned={scanned}; failed={failed}");
    return;
}

if (args[0] is "il" or "refs")
{
    using var module = ModuleDefinition.ReadModule(args[1]);
    IEnumerable<TypeDefinition> Walk(IEnumerable<TypeDefinition> types)
    {
        foreach (var type in types)
        {
            yield return type;
            foreach (var nested in Walk(type.NestedTypes)) yield return nested;
        }
    }
    foreach (var type in Walk(module.Types))
    {
        foreach (var method in type.Methods)
        {
            if (args[0] == "refs")
            {
                if (!method.HasBody || !method.Body.Instructions.Any(i =>
                    i.Operand?.ToString()?.Contains(args[2], StringComparison.OrdinalIgnoreCase) == true)) continue;
            }
            else if (!method.FullName.Contains(args[2], StringComparison.OrdinalIgnoreCase)) continue;
            Console.WriteLine(method.FullName);
            if (args.Length > 3 && args[3] == "names") continue;
            if (method.HasBody)
                foreach (var instruction in method.Body.Instructions) Console.WriteLine("  " + instruction);
        }
    }
    return;
}

if (args[0] == "catalog")
{
    using var doc = JsonDocument.Parse(File.ReadAllText(args[1]));
    var root = doc.RootElement;
    byte[] Bytes(string name) => Convert.FromBase64String(root.GetProperty(name).GetString());
    var keys = Bytes("m_KeyDataString");
    var buckets = Bytes("m_BucketDataString");
    var entries = Bytes("m_EntryDataString");
    int Int(byte[] data, int offset) => BitConverter.ToInt32(data, offset);
    var all = new List<(string key, int[] entries)>();
    int pos = 4;
    for (int i = 0; i < Int(buckets, 0); i++)
    {
        int offset = Int(buckets, pos); pos += 4;
        int count = Int(buckets, pos); pos += 4;
        string key = keys[offset] switch {
            0 => Encoding.ASCII.GetString(keys, offset + 5, Int(keys, offset + 1)),
            1 => Encoding.Unicode.GetString(keys, offset + 5, Int(keys, offset + 1)),
            _ => $"<key:{i}>"
        };
        int[] ids = new int[count];
        for (int n = 0; n < count; n++) { ids[n] = Int(buckets, pos); pos += 4; }
        all.Add((key, ids));
    }
    void Show(int id, int depth)
    {
        var fields = Enumerable.Range(0, 7).Select(c => Int(entries, 4 + id * 28 + c * 4)).ToArray();
        Console.WriteLine(new string(' ', depth * 2) + $"{id}: {root.GetProperty("m_InternalIds")[fields[0]]} TYPE={root.GetProperty("m_resourceTypes")[fields[6]]}");
        if (fields[2] >= 0 && depth < 2)
            foreach (int dep in all[fields[2]].entries) Show(dep, depth + 1);
    }
    foreach (var item in all.Where(x => x.key.Contains(args[2], StringComparison.OrdinalIgnoreCase)))
    {
        Console.WriteLine("KEY " + item.key);
        if (args.Length < 4 || args[3] != "keys")
            foreach (int id in item.entries) Show(id, 0);
    }
}
else
{
    var am = new AssetsManager();
    am.LoadClassPackage(Path.GetFullPath("libs/classdata.tpk"));
    var bundle = am.LoadBundleFile(args[1]);
    var file = am.LoadAssetsFileFromBundle(bundle, 0, false);
    am.LoadClassDatabaseFromPackage(file.file.Metadata.UnityVersion);
    foreach (var info in file.file.AssetInfos)
    {
        if (args[0] == "texts")
        {
            if (info.TypeId != (int)AssetClassID.TextAsset) continue;
            var reader = file.file.Reader;
            reader.Position = info.GetAbsoluteByteStart(file.file);
            string name = reader.ReadCountStringInt32();
            reader.Align();
            int length = reader.ReadInt32();
            byte[] content = reader.ReadBytes(length);
            Console.WriteLine($"{name}: {length} bytes; {Convert.ToHexString(content.AsSpan(0, Math.Min(8, content.Length)))}");
            if (args.Length > 2)
            {
                Directory.CreateDirectory(args[2]);
                string safeName = Path.GetFileName(name);
                foreach (char c in Path.GetInvalidFileNameChars()) safeName = safeName.Replace(c, '_');
                using var output = new FileStream(Path.Combine(args[2], safeName), FileMode.CreateNew);
                output.Write(content);
            }
            continue;
        }
        Console.WriteLine($"OBJECT {info.PathId} TYPE {(AssetClassID)info.TypeId}");
        try
        {
            var field = am.GetBaseField(file, info);
            void Dump(AssetTypeValueField f, int depth)
            {
                if (depth > 8) return;
                if (f.Children.Count > 100) { Console.WriteLine($"{new string(' ', depth)}{f.FieldName}: [{f.Children.Count} children]"); return; }
                bool binary = f.Value != null && f.Value.ValueType == AssetValueType.ByteArray;
                string value = binary ? "[binary]" : f.Value?.ToString();
                if (value?.Length > 300) value = value[..300] + "...";
                Console.WriteLine($"{new string(' ', depth)}{f.FieldName} ({f.TypeName}) {value}");
                if (f.TypeName == "ManagedReferencesRegistry")
                {
                    var registry = f.Value.AsManagedReferencesRegistry;
                    foreach (var reference in registry.references)
                    {
                        Console.WriteLine($"REFERENCE {reference.rid} {reference.type.ClassName}");
                        Dump(reference.data, depth + 1);
                    }
                }
                if (binary)
                {
                    byte[] bytes = f.AsByteArray;
                    Console.WriteLine($"PAYLOAD {bytes.Length} bytes; header {Convert.ToHexString(bytes.AsSpan(0, Math.Min(16, bytes.Length)))}");
                    if (args.Length > 2 && bytes.Length >= 4 && Encoding.ASCII.GetString(bytes, 0, 4) == "CRID")
                    {
                        using var output = new FileStream(args[2], FileMode.CreateNew);
                        output.Write(bytes);
                        Console.WriteLine("EXTRACTED " + Path.GetFullPath(args[2]));
                    }
                }
                foreach (var child in f.Children) Dump(child, depth + 1);
            }
            Dump(field, 0);
        }
        catch (Exception ex) { Console.WriteLine(ex.Message); }
    }
    am.UnloadAll();
}
