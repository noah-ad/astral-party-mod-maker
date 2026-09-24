using System.Text;
using System.Text.Json;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using Mono.Cecil;

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
