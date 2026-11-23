using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using JixModMaker;

internal static class MobileExportChecks
{
    public static void DeviceIntegrity(string adb, string work, Action<bool, string> check)
    {
        Directory.CreateDirectory(work);
        string file = Path.Combine(work, "integrity-probe.bin");
        File.WriteAllBytes(file, new byte[128 * 1024]);
        var device = new AndroidModInstaller(adb);
        string relative = "JixModResources/integrity-probe-" + Guid.NewGuid().ToString("N") + ".bin";
        string target = AndroidModInstaller.Addressables + "/" + relative;
        var expected = new[] { new FileIntegrity.Entry(relative, new FileInfo(file).Length, FileIntegrity.Hash(file)) };
        void Adb(params string[] args)
        {
            var info = new System.Diagnostics.ProcessStartInfo(adb) { UseShellExecute = false, CreateNoWindow = true };
            info.ArgumentList.Add("-s");
            info.ArgumentList.Add(device.Serial);
            foreach (string arg in args) info.ArgumentList.Add(arg);
            using var process = System.Diagnostics.Process.Start(info)!;
            if (!process.WaitForExit(30000)) { process.Kill(true); throw new TimeoutException("Probe timed out."); }
            if (process.ExitCode != 0) throw new IOException("Probe failed.");
        }
        try
        {
            Adb("push", file, target);
            device.VerifyInstalledFiles(expected);
            check(true, "real Android readback accepts the complete 128KB probe without changing game resources");
            Adb("shell", "dd if=/dev/zero of=" + target + " bs=1024 count=100 status=none");
            try { device.VerifyInstalledFiles(expected); throw new Exception("Truncated device file accepted."); }
            catch (IOException) { check(true, "real Android readback rejects the same file truncated to 100KB"); }
        }
        finally { Adb("shell", "rm -f " + target); }
    }

    public static void Run(Action<bool, string> check)
    {
        string work = Path.Combine(Path.GetTempPath(), "JixMobileChecks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            string source = Path.Combine(work, "catalog_3.3.0.json");
            RuntimeLocatorChecks.Catalog(source, new string('a', 32), new string('b', 32));
            var root = JsonNode.Parse(File.ReadAllBytes(source))!.AsObject();
            byte[] name = Encoding.UTF8.GetBytes("UT_Hero_Card_301");
            using var keys = new MemoryStream();
            using (var writer = new BinaryWriter(keys, Encoding.UTF8, true)) { writer.Write((byte)0); writer.Write(name.Length); writer.Write(name); }
            root["m_KeyDataString"] = Convert.ToBase64String(keys.ToArray());
            root["m_InternalIds"] = new JsonArray("texture", "{UnityEngine.AddressableAssets.Addressables.RuntimePath}/Android/" + new string('b', 32) + ".bundle");
            root["m_ProviderIds"] = new JsonArray("UnityEngine.ResourceManagement.ResourceProviders.AssetBundleProvider");
            root["m_resourceTypes"] = new JsonArray(new JsonObject { ["m_ClassName"] = "UnityEngine.Texture2D" }, new JsonObject { ["m_ClassName"] = "UnityEngine.AssetBundle" });
            byte[] baseline = Encoding.UTF8.GetBytes(root.ToJsonString());
            var catalog = new AndroidTextureCatalog(baseline);
            var location = catalog.Textures.Single().Value;
            check(location.InApk && location.BundleEntry == 1 && location.CachePath == new string('a', 32) + "/" + new string('b', 32) + "/__data",
                "mobile catalog resolves the exact current APK bundle instead of an old cached hash");
            string path = "JixModResources/20261020000000-aabbccdd/" + new string('b', 32) + ".bundle";
            byte[] updated = catalog.Redirect(new Dictionary<int, (string, long)> { [1] = (path, 128000) });
            var redirected = new AndroidTextureCatalog(updated);
            AndroidTextureCatalog.VerifyUpdate(baseline, updated);
            check(redirected.Textures.Single().Value.InternalId.EndsWith(path) && !redirected.Textures.Single().Value.InApk,
                "selected APK texture is redirected to an independent readable mobile file");
            check(redirected.Textures.Single().Value.ModPath == path, "existing independent mobile replacements can be resolved for a later update");
            var after = JsonNode.Parse(updated)!.AsObject();
            check(root.Where(pair => pair.Key is not "m_InternalIds" and not "m_EntryDataString" and not "m_ExtraDataString")
                .All(pair => JsonNode.DeepEquals(pair.Value, after[pair.Key])), "redirect preserves keys, providers, types and every unrelated catalog property");
            var hostile = (JsonObject)after.DeepClone();
            hostile["m_LocatorId"] = "changed initializer";
            try { AndroidTextureCatalog.VerifyUpdate(baseline, Encoding.UTF8.GetBytes(hostile.ToJsonString())); throw new Exception("Unrelated catalog mutation accepted"); }
            catch (InvalidDataException) { check(true, "installer rejects catalog changes outside selected texture bundle locations"); }
            byte[] oldEntries = Convert.FromBase64String(root["m_EntryDataString"]!.GetValue<string>());
            byte[] newEntries = Convert.FromBase64String(after["m_EntryDataString"]!.GetValue<string>());
            check(oldEntries.AsSpan(0, 32).SequenceEqual(newEntries.AsSpan(0, 32))
                && oldEntries.AsSpan(36, 12).SequenceEqual(newEntries.AsSpan(36, 12))
                && oldEntries.AsSpan(52).SequenceEqual(newEntries.AsSpan(52)), "redirect changes only the selected bundle internal-ID and options offsets");
            try { catalog.Redirect(new Dictionary<int, (string, long)> { [0] = (path, 1) }); throw new Exception("Unselected entry accepted"); }
            catch (InvalidDataException) { check(true, "unselected/non-bundle catalog entry cannot be redirected"); }
            try { catalog.Redirect(new Dictionary<int, (string, long)> { [1] = ("../escape.bundle", 1) }); throw new Exception("Path traversal accepted"); }
            catch (InvalidDataException) { check(true, "mobile redirect rejects escaping output paths"); }
            root["m_EntryDataString"] = Convert.ToBase64String(oldEntries[..^1]);
            try { _ = new AndroidTextureCatalog(Encoding.UTF8.GetBytes(root.ToJsonString())); throw new Exception("Truncated table accepted"); }
            catch (InvalidDataException) { check(true, "truncated catalog tables are rejected before device access"); }

            string original = Path.Combine(work, "one.bundle"), zip = Path.Combine(work, "one.zip");
            File.WriteAllText(original, "payload");
            var files = FileIntegrity.WriteZip(work, new[] { "one.bundle" }, zip);
            check(files.Single().Size == 7 && files.Single().Sha256 == FileIntegrity.Hash(original), "ZIP receipts include actual byte length and SHA256");
            using (var archive = ZipFile.Open(zip, ZipArchiveMode.Update))
            {
                archive.GetEntry("one.bundle")!.Delete();
                using var writer = new StreamWriter(archive.CreateEntry("one.bundle").Open());
                writer.Write("truncated");
            }
            try { FileIntegrity.VerifyZip(zip, files); throw new Exception("Corrupt ZIP accepted"); }
            catch (InvalidDataException) { check(true, "ZIP readback detects altered or truncated payloads"); }
            using var header = new MemoryStream();
            header.Write("UnityFS\0"u8);
            header.Write(new byte[] { 0, 0, 0, 8 });
            header.Write("2022.3\0revision\0"u8);
            header.Write(new byte[] { 0, 0, 0, 0, 0, 1, 0x90, 0 });
            header.Position = 0;
            try { FileIntegrity.CheckBundleLength(header, "100KB __data"); throw new Exception("Truncated bundle accepted"); }
            catch (InvalidDataException) { check(true, "truncated UnityFS is blocked even before ZIP compression"); }
            string mobile = Path.Combine(work, "AssetBundles");
            Directory.CreateDirectory(mobile);
            check(IndexService.UserCacheFor(mobile) == null && !IndexService.EnumerateBundles(mobile, true, true).Any(),
                "an Android resource copy never attaches this PC's installed game cache");
        }
        finally { Directory.Delete(work, true); }
    }

    public static void Repeat(string configPath, string work, Action<bool, string> check)
    {
        Directory.CreateDirectory(work);
        using var config = JsonDocument.Parse(File.ReadAllBytes(configPath));
        string Get(string key) => config.RootElement.GetProperty(key).GetString()!;
        var engine = new ModEngine();
        var images = new List<MobileTexturePackage.ImageSource>();
        using (var audit = JsonDocument.Parse(File.ReadAllBytes(Get("Audit"))))
        foreach (var bundle in audit.RootElement.GetProperty("Bundles").EnumerateArray())
        foreach (var texture in bundle.GetProperty("Textures").EnumerateArray())
        {
            string name = texture.GetProperty("Name").GetString()!;
            if (name is not "UT_Hero_Card_305" and not "UT_Hero_Card2_305") continue;
            string file = Path.Combine(Get("LegacyRoot"), bundle.GetProperty("Path").GetString()!);
            long id = texture.GetProperty("PathId").GetInt64();
            images.Add(new(name, file, () => engine.DecodePng(file, id)));
        }
        string first = Path.Combine(work, "first.zip");
        var original = MobileTexturePackage.Build(Get("Apk"), Get("Catalog"), images, first);
        string installed = Path.Combine(work, "installed-copy");
        ZipFile.ExtractToDirectory(first, installed);
        var second = MobileTexturePackage.Build(Get("Apk"), Path.Combine(installed, original.Manifest.CatalogName), images.Take(1),
            Path.Combine(work, "second.zip"), location => Path.Combine(installed, location.ModPath!));
        check(second.Manifest.Textures.Count == 1 && second.Manifest.RequiredFiles.Count == 1,
            "repeat export replaces only the selected image and records the untouched prior Mod as a required file");
        check(MobileTexturePackage.Read(second.Zip).RequiredFiles.Single().Sha256 == original.Manifest.Files
            .Single(file => file.Path == second.Manifest.RequiredFiles.Single().Path).Sha256, "incremental package preserves the previous texture byte-for-byte without putting it in the new ZIP");
        using (var archive = ZipFile.OpenRead(second.Zip))
            check(archive.Entries.Count == 2, "incremental mobile export contains only one selected bundle and its catalog");
        string invalid = Path.Combine(work, "missing-dependency.zip");
        File.Copy(second.Zip, invalid);
        File.WriteAllText(invalid + ".install.json", JsonSerializer.Serialize(second.Manifest with { RequiredFiles = new() }, MobileTexturePackage.Json));
        try { MobileTexturePackage.Read(invalid); throw new Exception("Dangling previous Mod accepted"); }
        catch (InvalidDataException) { check(true, "missing inherited dependencies are rejected instead of producing a misleading standalone package"); }
    }

    public static void Live(string configPath, bool sample, Action<bool, string> check)
    {
        using var config = JsonDocument.Parse(File.ReadAllBytes(configPath));
        string Get(string key) => config.RootElement.GetProperty(key).GetString()!;
        var engine = new ModEngine();
        var sources = new Dictionary<string, MobileTexturePackage.ImageSource>(StringComparer.Ordinal);
        using (var audit = JsonDocument.Parse(File.ReadAllBytes(Get("Audit"))))
        foreach (var bundle in audit.RootElement.GetProperty("Bundles").EnumerateArray())
        {
            string file = Path.Combine(Get("LegacyRoot"), bundle.GetProperty("Path").GetString()!);
            if (FileIntegrity.Hash(file) != bundle.GetProperty("ModifiedHash").GetString()) throw new InvalidDataException("Legacy Mod changed since audit: " + file);
            foreach (var texture in bundle.GetProperty("Textures").EnumerateArray().Where(texture => texture.GetProperty("Changed").GetBoolean()))
            {
                string name = texture.GetProperty("Name").GetString()!;
                long pathId = texture.GetProperty("PathId").GetInt64();
                sources.Add(name, new(name, file, () => engine.DecodePng(file, pathId)));
            }
        }
        string pc = Get("PcRoot");
        foreach (var entry in PackService.LoadWorkspace(pc).Entries.Where(entry => entry.Kind == ResourceKinds.Texture
            && System.Text.RegularExpressions.Regex.IsMatch(entry.TextureName ?? "", @"^UT_Hero_.*_30[1-6](?:_|$)")))
        {
            if (sources.ContainsKey(entry.TextureName)) continue;
            string path = Path.Combine(pc, entry.Bundle);
            if (!File.Exists(path)) continue;
            var texture = engine.FindTextureByName(path, entry.TextureName);
            if (texture != null) sources.Add(texture.Name, MobileTexturePackage.FromTexture(texture));
        }
        foreach (var source in MobileTexturePackage.FromPack(Get("SupplementPack")).Where(source =>
            System.Text.RegularExpressions.Regex.IsMatch(source.Name, @"^UT_Hero_.*_30[1-6](?:_|$)"))) sources.TryAdd(source.Name, source);
        string destination = Get(sample ? "SampleDestination" : "Destination");
        using var originals = ZipFile.OpenRead(Get("OriginalCacheZip"));
        string cache = Path.Combine(Path.GetDirectoryName(destination)!, "original-cache");
        Directory.CreateDirectory(cache);
        var catalog = new AndroidTextureCatalog(File.ReadAllBytes(Get("Catalog")));
        Console.WriteLine($"SOURCE {sources.Count}; current {sources.Keys.Count(catalog.Textures.ContainsKey)}; 300+ {sources.Keys.Count(name => System.Text.RegularExpressions.Regex.IsMatch(name, @"^UT_Hero_.*_30[1-6](?:_|$)"))}");
        var chosen = sample ? sources.Values.Where(source => source.Name == "UT_Hero_Card_305").ToArray() : sources.Values.ToArray();
        var result = MobileTexturePackage.Build(Get("Apk"), Get("Catalog"), chosen, destination, location =>
        {
            var entry = originals.GetEntry("AssetBundles/" + location.CachePath);
            if (entry == null) throw new FileNotFoundException("Current original not available in original cache archive: " + location.CachePath);
            string file = Path.Combine(cache, location.BundleFile);
            entry.ExtractToFile(file, true);
            return file;
        }, Console.WriteLine);
        check(result.Manifest.Textures.Count > 0 && result.Manifest.Files.Count > 1, "current-version Android Mod ZIP and installation receipt generated");
        check(MobileTexturePackage.Read(destination).InstalledCatalogSha256 == result.Manifest.InstalledCatalogSha256,
            "real mobile package passes complete ZIP readback validation");
        Console.WriteLine(JsonSerializer.Serialize(new { result.Zip, result.Receipt, Textures = result.Manifest.Textures.Count,
            Missing = result.Manifest.Missing, Roles300 = result.Manifest.Textures.Where(name => System.Text.RegularExpressions.Regex.IsMatch(name, @"^UT_Hero_.*_30[1-6](?:_|$)")) }, MobileTexturePackage.Json));
    }
}
