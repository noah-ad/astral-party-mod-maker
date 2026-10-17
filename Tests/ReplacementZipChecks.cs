using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using JixModMaker;

internal static class ReplacementZipChecks
{
    public static void Run(Action<bool, string> check)
    {
        string work = Path.Combine(Path.GetTempPath(), "JixSplitZip-" + Guid.NewGuid().ToString("N"));
        string game = Path.Combine(work, "game");
        string hot = Path.Combine(work, "hot");
        string output = Path.Combine(work, "exports");
        Directory.CreateDirectory(game);
        Directory.CreateDirectory(Path.Combine(hot, "hash", "inner"));
        Directory.CreateDirectory(output);
        try
        {
            string baseFile = Path.Combine(game, "test.bundle");
            string hotFile = Path.Combine(hot, "hash", "inner", "__data");
            File.WriteAllText(baseFile, "game bytes");
            File.WriteAllText(hotFile, "hot bytes");
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(hotFile)!, "__info"), "hot metadata");
            var sources = new[] { new ReplacementZip.Source(game, "基础资源"), new ReplacementZip.Source(hot, "热更新缓存") };
            string destination = Path.Combine(output, "我的Mod.zip");
            var parts = ReplacementZip.Plan(sources, new[] { baseFile, hotFile, baseFile.ToUpperInvariant() }, destination);
            check(parts.Count == 2 && parts.Sum(part => part.Paths.Count) == 2, "mixed export splits sources and deduplicates resource paths");
            check(parts.Select(part => Path.GetFileName(part.Destination)).Order().SequenceEqual(new[] { "我的Mod-基础资源.zip", "我的Mod-热更新缓存.zip" }.Order()),
                "mixed export uses distinct source-labelled ZIP filenames");
            var result = ReplacementZip.Export(parts);
            check(result.Count == 2 && result.Sum(part => part.Files) == 3 && !File.Exists(destination), "mixed export creates two direct-replacement archives");
            foreach (var part in parts)
            {
                using var zip = ZipFile.OpenRead(part.Destination);
                string[] expected = part.Root == game ? new[] { "test.bundle" } : new[] { "hash/inner/__data", "hash/inner/__info" };
                check(zip.Entries.Select(entry => entry.FullName).Order().SequenceEqual(expected.Order()), "split ZIP retains relative paths without wrapper directories: " + part.Label);
                foreach (var entry in zip.Entries)
                {
                    using var stream = entry.Open();
                    using var reader = new StreamReader(stream);
                    check(reader.ReadToEnd() == File.ReadAllText(Path.Combine(part.Root, entry.FullName)), "split export preserves source bytes: " + entry.FullName);
                }
            }

            var single = ReplacementZip.Plan(sources, new[] { hotFile }, destination);
            check(single.Count == 1 && single[0].Destination == destination, "single-source export retains the chosen ZIP filename");
            check(ReplacementZip.Export(single).Single().Files == 2, "single hot-cache export includes its __info");
            var nested = ReplacementZip.Plan(new[] { new ReplacementZip.Source(work, "parent"), sources[1] }, new[] { hotFile }, destination);
            check(nested.Single().Root == hot, "the most specific resource root wins over its parent");
            var repeated = ReplacementZip.Plan(new[] { sources[0], new ReplacementZip.Source(game.ToUpperInvariant() + "\\", "duplicate") }, new[] { baseFile }, destination);
            check(repeated.Count == 1, "Windows path aliases do not create duplicate source archives");

            string outside = Path.Combine(work, "outside.bundle");
            File.WriteAllText(outside, "outside");
            try { ReplacementZip.Plan(sources, new[] { outside }, destination); throw new Exception("Unrelated root accepted."); }
            catch (InvalidOperationException) { check(true, "resources outside the known roots are rejected before export"); }
            try { ReplacementZip.Plan(sources, new[] { baseFile }, baseFile); throw new Exception("Source overwrite accepted."); }
            catch (InvalidOperationException) { check(File.ReadAllText(baseFile) == "game bytes", "export cannot overwrite its input resource"); }

            File.WriteAllText(parts[0].Destination, "old first archive");
            File.WriteAllText(parts[1].Destination, "old second archive");
            using (var locked = new FileStream(parts[1].Destination, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                try { ReplacementZip.Export(parts); throw new Exception("Locked output accepted."); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { check(true, "locked second output reports failure"); }
            }
            check(File.ReadAllText(parts[0].Destination) == "old first archive" && File.ReadAllText(parts[1].Destination) == "old second archive",
                "failure committing the second ZIP restores both prior outputs");
            check(!Directory.EnumerateFiles(output).Any(path => path.EndsWith(".tmp") || path.EndsWith(".previous")), "failed mixed export cleans its staging files");
            var missing = parts.Select(part => part.Root == hot ? part with { Paths = new[] { Path.Combine(hot, "missing") } } : part).ToArray();
            try { ReplacementZip.Export(missing); throw new Exception("Missing source accepted."); }
            catch (FileNotFoundException) { check(true, "missing later source aborts split export"); }
            check(File.ReadAllText(parts[0].Destination) == "old first archive", "preparation failure leaves existing outputs untouched");
            File.Delete(parts[0].Destination);
            using (var locked = new FileStream(parts[1].Destination, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                try { ReplacementZip.Export(parts); throw new Exception("Locked output accepted."); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { check(!File.Exists(parts[0].Destination), "failed export removes a newly created first ZIP"); }
            }
        }
        finally { Directory.Delete(work, true); }
    }

    public static void Smoke(string indexPath, string output, Action<bool, string> check)
    {
        output = Path.GetFullPath(output);
        if (Directory.Exists(output)) throw new IOException("Smoke output must be a new directory.");
        Directory.CreateDirectory(output);
        var index = JsonSerializer.Deserialize<GameIndex>(File.ReadAllText(indexPath))!;
        var workspace = PackService.LoadWorkspace(index.GameDir);
        var assets = workspace.Entries.Select(entry => index.FindTextureTarget(entry.Bundle, entry.TextureName, entry.PathId))
            .Where(entry => entry != null && File.Exists(entry.BundlePath)).DistinctBy(entry => entry.BundlePath, StringComparer.OrdinalIgnoreCase).ToList();
        var sources = new[] { new ReplacementZip.Source(index.GameDir, "基础资源"), new ReplacementZip.Source(index.HotDir, "热更新缓存") };
        var all = ReplacementZip.Plan(sources, assets.Select(entry => entry.BundlePath), Path.Combine(output, "all.zip"));
        check(all.Count == 2, "current real workspace export is separated into base resources and hot cache");
        var samples = all.Select(part => part.Paths.OrderBy(path => new FileInfo(path).Length).First()).ToArray();
        string HashFile(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
        var hashes = samples.ToDictionary(path => path, HashFile);
        var plan = ReplacementZip.Plan(sources, samples, Path.Combine(output, "mixed.zip"));
        var result = ReplacementZip.Export(plan);
        check(result.Count == 2, "real mixed-source resources export together without error");
        foreach (var part in plan)
        {
            using var zip = ZipFile.OpenRead(part.Destination);
            foreach (var entry in zip.Entries)
            {
                using var stream = entry.Open();
                check(Convert.ToHexString(SHA256.HashData(stream)) == HashFile(Path.Combine(part.Root, entry.FullName)), "real archive bytes match the game resource: " + entry.FullName);
            }
            check(zip.Entries.All(entry => !entry.FullName.StartsWith("current/") && !entry.FullName.StartsWith("original/") && entry.FullName != "manifest.json"),
                "real exported ZIP has no project-only wrapper entries: " + part.Label);
        }
        check(hashes.All(pair => HashFile(pair.Key) == pair.Value), "real export leaves game resources unchanged");
        File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { Sources = all.Select(part => new { part.Label, Bundles = part.Paths.Count }), Samples = result }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
