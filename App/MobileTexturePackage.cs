using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace JixModMaker;

public static class MobileTexturePackage
{
    public sealed record ImageSource(string Name, string Origin, Func<byte[]> Read);
    public sealed record Manifest(int Format, string PackageId, string GameVersion, string CatalogName,
        string OriginalCatalogSha256, string InstalledCatalogSha256, string ApkSha256,
        List<FileIntegrity.Entry> Files, List<string> Textures, List<string> Missing,
        List<FileIntegrity.Entry> RequiredFiles = null);
    public sealed record Result(string Zip, string Receipt, Manifest Manifest);
    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public static IEnumerable<ImageSource> FromPack(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        var entry = archive.GetEntry("manifest.json") ?? throw new InvalidDataException("图包缺少 manifest.json。");
        using var stream = entry.Open();
        var manifest = JsonSerializer.Deserialize<ModManifest>(stream, Json) ?? throw new InvalidDataException("图包清单为空。");
        foreach (var image in manifest.Entries.Where(image => image.Kind == ResourceKinds.Texture && !string.IsNullOrWhiteSpace(image.TextureName)))
        {
            string imagePath = FileIntegrity.Relative(image.Image ?? "");
            if (archive.GetEntry(imagePath) == null) throw new InvalidDataException("图包缺少图片：" + imagePath);
            yield return new(image.TextureName, Path.GetFileName(path), () =>
            {
                using var source = ZipFile.OpenRead(path);
                using var input = source.GetEntry(imagePath)!.Open();
                using var buffer = new MemoryStream();
                input.CopyTo(buffer);
                return buffer.ToArray();
            });
        }
    }

    public static ImageSource FromTexture(TexRef texture) => new(texture.Name, texture.BundlePath,
        () => new ModEngine().DecodePng(texture.BundlePath, texture.PathId));

    public static Result Build(string apkPath, string catalogPath, IEnumerable<ImageSource> selected, string destination,
        Func<AndroidTextureCatalog.Location, string> resolveCache = null, Action<string> progress = null,
        CancellationToken cancellation = default)
    {
        string version = Regex.Match(Path.GetFileName(catalogPath), @"^catalog_(\d+(?:\.\d+){1,3})\.json$").Groups[1].Value;
        if (version.Length == 0) throw new InvalidDataException("请选择当前版本的 catalog_版本.json。");
        if (File.Exists(destination) || File.Exists(destination + ".install.json")) throw new IOException("导出文件已经存在，请选择新文件名。");
        byte[] originalCatalog = File.ReadAllBytes(catalogPath);
        var catalog = new AndroidTextureCatalog(originalCatalog);
        var sources = selected.GroupBy(source => source.Name, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);
        if (sources.Count == 0) throw new InvalidDataException("没有选择贴图。");
        string[] missing = sources.Keys.Except(catalog.Textures.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var groups = sources.Values.Where(source => catalog.Textures.ContainsKey(source.Name))
            .GroupBy(source => catalog.Textures[source.Name].BundleEntry).ToArray();
        if (groups.Length == 0) throw new InvalidDataException("所选图片均不在当前安卓资源表中。");
        if (groups.Select(group => catalog.Textures[group.First().Name].BundleFile).Distinct(StringComparer.OrdinalIgnoreCase).Count() != groups.Length)
            throw new InvalidDataException("不同资源包使用重复文件名，无法安全重定向。");
        using var apk = ZipFile.OpenRead(apkPath);
        var settingsEntry = apk.GetEntry("assets/aa/settings.json") ?? throw new InvalidDataException("APK 缺少 Addressables 设置。");
        using (var settings = JsonDocument.Parse(settingsEntry.Open()))
        {
            var root = settings.RootElement;
            if (root.GetProperty("m_buildTarget").GetString() != "Android"
                || !root.GetProperty("m_CatalogLocations").EnumerateArray().Any(location =>
                    location.GetProperty("m_InternalId").GetString()?.EndsWith("/catalog_" + version + ".hash", StringComparison.Ordinal) == true))
                throw new InvalidDataException("APK 与所选资源表版本不一致，已停止导出。");
        }
        string packageId = DateTime.UtcNow.ToString("yyyyMMddHHmmss") + "-" + Guid.NewGuid().ToString("N")[..8];
        string work = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(destination))!, ".jix-mobile-" + packageId);
        Directory.CreateDirectory(work);
        string temporary = destination + "." + packageId + ".tmp";
        string receipt = destination + ".install.json";
        var redirects = new Dictionary<int, (string Path, long Size)>();
        var paths = new List<string>();
        var textures = new List<string>();
        try
        {
            int done = 0;
            Parallel.ForEach(groups, new ParallelOptions { MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount / 2, 1, 4), CancellationToken = cancellation }, group =>
            {
                cancellation.ThrowIfCancellationRequested();
                var location = catalog.Textures[group.First().Name];
                string original = Path.Combine(work, "original-" + group.Key + ".bundle");
                lock (apk)
                {
                    if (location.InApk)
                    {
                        var entry = apk.GetEntry("assets/aa/Android/" + location.BundleFile)
                            ?? throw new InvalidDataException("当前 APK 缺少原始资源：" + location.BundleFile);
                        entry.ExtractToFile(original, true);
                    }
                    else
                    {
                        string source = resolveCache?.Invoke(location);
                        if (!File.Exists(source)) throw new FileNotFoundException("缺少当前版本的安卓原始包，不能导出旧哈希目录：" + location.Name, source);
                        File.Copy(source, original, true);
                    }
                }
                string relative = "JixModResources/" + packageId + "/" + location.BundleFile;
                string output = Path.Combine(work, relative);
                MobileTextureBundle.Replace(original, output, group.ToDictionary(source => source.Name, source => source.Read(), StringComparer.Ordinal));
                lock (redirects)
                {
                    if (paths.Contains(relative, StringComparer.OrdinalIgnoreCase)) throw new InvalidDataException("不同资源使用重复文件名，无法安全重定向。");
                    redirects.Add(group.Key, (relative, new FileInfo(output).Length));
                    paths.Add(relative);
                    textures.AddRange(group.Select(source => source.Name));
                }
                File.Delete(original);
                int completed = Interlocked.Increment(ref done);
                if (completed % 10 == 0 || completed == groups.Length) progress?.Invoke(UiText.F($"生成安卓贴图 {completed}/{groups.Length}：{group.First().Name}"));
            });
            var requiredFiles = new List<FileIntegrity.Entry>();
            foreach (var inherited in catalog.Textures.Values.Where(location => location.ModPath != null && !redirects.ContainsKey(location.BundleEntry))
                .DistinctBy(location => location.ModPath, StringComparer.Ordinal))
            {
                cancellation.ThrowIfCancellationRequested();
                string file = resolveCache?.Invoke(inherited);
                if (!File.Exists(file)) throw new FileNotFoundException("当前资源表还引用了其它已装贴图，缺少这些文件时不能生成增量包。请连接原手机后重试：" + inherited.Name, file);
                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
                FileIntegrity.CheckBundleLength(stream, inherited.ModPath);
                requiredFiles.Add(new(inherited.ModPath, stream.Length, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream))));
            }
            string catalogName = Path.GetFileName(catalogPath);
            byte[] updatedCatalog = catalog.Redirect(redirects);
            AndroidTextureCatalog.VerifyUpdate(originalCatalog, updatedCatalog);
            File.WriteAllBytes(Path.Combine(work, catalogName), updatedCatalog);
            paths.Add(catalogName);
            cancellation.ThrowIfCancellationRequested();
            progress?.Invoke(UiText.T("压缩并回读校验安卓 ZIP…"));
            var files = FileIntegrity.WriteZip(work, paths, temporary, (done, total) =>
            {
                cancellation.ThrowIfCancellationRequested();
                if (done % 25 == 0 || done == total) progress?.Invoke(UiText.F($"压缩 / 校验 {done}/{total}"));
            }).ToList();
            var manifest = new Manifest(1, packageId, version, catalogName, FileIntegrity.Hash(originalCatalog),
                FileIntegrity.Hash(updatedCatalog), FileIntegrity.Hash(apkPath), files, textures.Order(StringComparer.Ordinal).ToList(), missing.ToList(), requiredFiles);
            File.WriteAllText(receipt + ".tmp", JsonSerializer.Serialize(manifest, Json));
            File.Move(temporary, destination);
            try { File.Move(receipt + ".tmp", receipt); }
            catch { File.Delete(destination); throw; }
            return new(destination, receipt, manifest);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            if (File.Exists(receipt + ".tmp")) File.Delete(receipt + ".tmp");
            Directory.Delete(work, true);
        }
    }

    public static Manifest Read(string zip)
    {
        var manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(zip + ".install.json"), Json)
            ?? throw new InvalidDataException("缺少安卓安装校验清单。");
        bool Hash(string value) => value != null && Regex.IsMatch(value, "^[0-9A-F]{64}$");
        bool Dependency(FileIntegrity.Entry file) => file != null && file.Size > 0 && Hash(file.Sha256)
            && Regex.IsMatch(file.Path ?? "", @"^JixModResources/[0-9]{14}-[0-9a-f]{8}/[0-9a-f]{32}\.bundle$");
        if (manifest.Format != 1 || !Regex.IsMatch(manifest.PackageId ?? "", @"^[0-9]{14}-[0-9a-f]{8}$")
            || !Hash(manifest.OriginalCatalogSha256) || !Hash(manifest.InstalledCatalogSha256) || !Hash(manifest.ApkSha256)
            || !Regex.IsMatch(manifest.GameVersion ?? "", @"^\d+(?:\.\d+){1,3}$") || manifest.CatalogName != "catalog_" + manifest.GameVersion + ".json"
            || manifest.Files == null || manifest.Files.Count < 2
            || manifest.Files.Any(file => file == null || file.Path == null || (file.Path != manifest.CatalogName &&
                !Regex.IsMatch(file.Path, "^JixModResources/" + manifest.PackageId + @"/[0-9a-f]{32}\.bundle$")))
            || manifest.Files.Any(file => file.Size <= 0 || !Hash(file.Sha256))
            || manifest.Files.Count(file => file.Path == manifest.CatalogName && file.Sha256 == manifest.InstalledCatalogSha256) != 1
            || manifest.Files.Select(file => file.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != manifest.Files.Count
            || manifest.RequiredFiles?.Any(file => !Dependency(file) || manifest.Files.Any(own => own.Path == file.Path)) == true
            || (manifest.RequiredFiles?.Select(file => file.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() ?? 0) != (manifest.RequiredFiles?.Count ?? 0))
            throw new InvalidDataException("安卓安装清单无效。");
        FileIntegrity.VerifyZip(zip, manifest.Files);
        using (var archive = ZipFile.OpenRead(zip))
        {
            using var stream = archive.GetEntry(manifest.CatalogName)!.Open();
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            var catalog = new AndroidTextureCatalog(buffer.ToArray());
            var declared = manifest.Files.Where(file => file.Path != manifest.CatalogName)
                .Concat(manifest.RequiredFiles ?? new()).Select(file => file.Path).ToHashSet(StringComparer.Ordinal);
            var referenced = catalog.Textures.Values.Where(location => location.ModPath != null).Select(location => location.ModPath).ToHashSet(StringComparer.Ordinal);
            if (!declared.SetEquals(referenced)) throw new InvalidDataException("资源表引用的独立贴图与安装清单不一致，已拒绝缺少依赖的安装包。");
        }
        return manifest;
    }
}
