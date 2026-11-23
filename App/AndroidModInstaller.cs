using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace JixModMaker;

public sealed class AndroidModInstaller
{
    public const string Package = "com.feimo.astralparty";
    public const string Addressables = "/storage/emulated/0/Android/data/" + Package + "/files/com.unity.addressables";
    public sealed record Snapshot(string Serial, string Version, string Apk, string Catalog, string Directory);
    public sealed record Installation(string Serial, string Version, string PackageId, string CatalogName,
        string BeforeSha256, string AfterSha256, string BackupDirectory, List<FileIntegrity.Entry> Files, string State);
    public string Adb { get; }
    public string Serial { get; }
    public Action<string> Progress { get; set; }

    public AndroidModInstaller(string adb = null, string serial = null)
    {
        Adb = adb ?? Path.Combine(AppContext.BaseDirectory, "Tools", "adb", "adb.exe");
        if (!File.Exists(Adb)) throw new FileNotFoundException("缺少 ADB 组件，请使用完整视频版，或选择 Android SDK 的 adb.exe。", Adb);
        string[] devices = RunRaw("devices").Split('\n').Select(line => Regex.Match(line.Trim(), @"^(\S+)\s+device$"))
            .Where(match => match.Success).Select(match => match.Groups[1].Value).ToArray();
        if (serial == null && devices.Length != 1) throw new InvalidOperationException(devices.Length == 0
            ? "未检测到已授权的手机。请连接 USB、启用调试，并在手机上允许这台电脑。" : "连接了多台设备，请只保留要安装的手机。");
        Serial = serial ?? devices.Single();
        if (!devices.Contains(Serial) || !Regex.IsMatch(Serial, @"^[A-Za-z0-9._:-]+$")) throw new InvalidDataException("设备未连接或未授权。");
    }

    public Snapshot ReadCurrent(string destination)
    {
        string version = Version();
        string[] packages = Run("shell", "pm path " + Package).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        string apk = packages.Select(line => line.Trim()).SingleOrDefault(line => line.StartsWith("package:") && line.EndsWith("/base.apk"))?[8..]
            ?? throw new InvalidDataException("找不到游戏 base.apk。");
        string apkHash = RemoteHash(apk);
        string root = Path.Combine(destination, Serial.Replace(':', '_'), version, apkHash[..12]);
        Directory.CreateDirectory(root);
        string localApk = Path.Combine(root, "base.apk");
        if (!File.Exists(localApk) || FileIntegrity.Hash(localApk) != apkHash)
        {
            Progress?.Invoke(UiText.T("读取手机当前 APK…"));
            PullVerified(apk, localApk, apkHash);
        }
        string catalogName = "catalog_" + version + ".json";
        string catalog = Path.Combine(root, catalogName);
        Progress?.Invoke(UiText.T("读取当前资源表 ") + catalogName);
        PullVerified(Addressables + "/" + catalogName, catalog);
        _ = new AndroidTextureCatalog(File.ReadAllBytes(catalog));
        return new(Serial, version, localApk, catalog, root);
    }

    public string ReadCache(Snapshot snapshot, AndroidTextureCatalog.Location location)
    {
        if (snapshot.Serial != Serial) throw new InvalidDataException("手机已变化，请重新读取资源。");
        string relative = location.ModPath ?? "AssetBundles/" + location.CachePath;
        string path = Path.Combine(snapshot.Directory, "cache", FileIntegrity.Relative(relative));
        if (location.ModPath != null || !File.Exists(path)) PullVerified(Addressables + "/" + relative, path);
        return path;
    }

    public Installation Install(string zip, string backupRoot)
    {
        Progress?.Invoke(UiText.T("核验安装包…"));
        var manifest = MobileTexturePackage.Read(zip);
        EnsureClosed();
        if (Version() != manifest.GameVersion) throw new InvalidDataException("压缩包与手机游戏版本不一致，未写入。");
        string apkPath = Run("shell", "pm path " + Package).Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim()).SingleOrDefault(line => line.StartsWith("package:") && line.EndsWith("/base.apk"))?[8..]
            ?? throw new InvalidDataException("找不到游戏 APK。");
        if (RemoteHash(apkPath) != manifest.ApkSha256) throw new InvalidDataException("手机 APK 与导出使用的原始 APK 不同，未写入。请重新读取手机生成安装包。");
        string targetCatalog = Addressables + "/" + manifest.CatalogName;
        if (RemoteHash(targetCatalog) != manifest.OriginalCatalogSha256) throw new InvalidDataException("手机资源表已变化或已有其它重定向，未写入。请重新读取手机后生成安装包。");
        VerifyInstalledFiles(manifest.RequiredFiles ?? new());
        string backup = Path.Combine(Path.GetFullPath(backupRoot), Serial.Replace(':', '_'), DateTime.UtcNow.ToString("yyyyMMddHHmmss") + "-" + manifest.PackageId);
        Directory.CreateDirectory(backup);
        PullVerified(targetCatalog, Path.Combine(backup, manifest.CatalogName), manifest.OriginalCatalogSha256);
        using (var archive = ZipFile.OpenRead(zip))
        {
            using var stream = archive.GetEntry(manifest.CatalogName)!.Open();
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            AndroidTextureCatalog.VerifyUpdate(File.ReadAllBytes(Path.Combine(backup, manifest.CatalogName)), buffer.ToArray());
        }
        var installation = new Installation(Serial, manifest.GameVersion, manifest.PackageId, manifest.CatalogName,
            manifest.OriginalCatalogSha256, manifest.InstalledCatalogSha256, backup, manifest.Files, "prepared");
        Save(installation);
        string staging = Path.Combine(backup, "staging");
        string remote = "/data/local/tmp/jixmod-" + manifest.PackageId;
        string modDirectory = Addressables + "/JixModResources/" + manifest.PackageId;
        bool catalogStarted = false;
        try
        {
            Run("shell", "test ! -e " + Quote(modDirectory));
            ZipFile.ExtractToDirectory(zip, staging);
            string checksums = string.Join("\n", manifest.Files.Select(file => file.Sha256.ToLowerInvariant() + "  " + file.Path)) + "\n";
            File.WriteAllText(Path.Combine(staging, "checksums.txt"), checksums, new UTF8Encoding(false));
            Progress?.Invoke(UiText.T("发送资源到手机临时目录…"));
            Run("push", staging, remote);
            Run("shell", "cd " + Quote(remote) + " && sha256sum -c checksums.txt");
            EnsureClosed();
            if (RemoteHash(targetCatalog) != installation.BeforeSha256) throw new IOException("安装准备期间资源表发生变化，已停止。");
            Progress?.Invoke(UiText.T("写入独立贴图文件…"));
            Run("shell", "mkdir -p " + Quote(Addressables + "/JixModResources") + " && cp -R " + Quote(remote + "/JixModResources/" + manifest.PackageId) + " " + Quote(modDirectory));
            VerifyInstalledFiles(manifest.Files.Where(file => file.Path != manifest.CatalogName).ToList());
            EnsureClosed();
            VerifyInstalledFiles(manifest.RequiredFiles ?? new());
            if (RemoteHash(targetCatalog) != installation.BeforeSha256) throw new IOException("提交前资源表发生变化，已停止。");
            Progress?.Invoke(UiText.T("提交资源表并读回校验…"));
            catalogStarted = true;
            Run("shell", "dd if=" + Quote(remote + "/" + manifest.CatalogName) + " of=" + Quote(targetCatalog) + " bs=65536 conv=fsync status=none");
            if (RemoteHash(targetCatalog) != installation.AfterSha256) throw new IOException("手机资源表写入不完整。");
            VerifyInstalledFiles(manifest.Files);
            installation = installation with { State = "installed" };
            Save(installation);
            return installation;
        }
        catch (Exception error)
        {
            try
            {
                if (catalogStarted) RestoreCatalog(installation);
                installation = installation with { State = "failed-rolled-back" };
                Save(installation);
            }
            catch (Exception rollback)
            {
                Save(installation with { State = "rollback-required" });
                throw new AggregateException("安装失败且自动恢复未完成。备份保留在：" + backup, error, rollback);
            }
            throw new IOException("安装未完成；未提交资源表或已恢复原资源表。备份：" + backup, error);
        }
        finally
        {
            try { Run("shell", "rm -rf " + Quote(remote)); } catch { }
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
        }
    }

    public void Restore(string receipt)
    {
        var installation = JsonSerializer.Deserialize<Installation>(File.ReadAllText(receipt), MobileTexturePackage.Json)
            ?? throw new InvalidDataException("安装记录为空。");
        if (installation.Serial != Serial || !Regex.IsMatch(installation.PackageId, @"^[0-9]{14}-[0-9a-f]{8}$")
            || !Regex.IsMatch(installation.Version, @"^\d+(?:\.\d+){1,3}$") || installation.CatalogName != "catalog_" + installation.Version + ".json"
            || !RuntimeBundleLocator.SamePath(Path.GetDirectoryName(receipt), installation.BackupDirectory)) throw new InvalidDataException("安装记录与设备或备份目录不符。");
        EnsureClosed();
        if (Version() != installation.Version) throw new InvalidDataException("游戏版本已更新，不能把旧资源表还原到新版。");
        string current = RemoteHash(Addressables + "/" + installation.CatalogName);
        if (current != installation.AfterSha256 && current != installation.BeforeSha256) throw new InvalidDataException("手机资源表已被更新或其它工具修改，已停止还原，避免覆盖其它 Mod。");
        RestoreCatalog(installation);
        Save(installation with { State = "restored" });
        // Keep orphaned, content-addressed files: deleting them could break a later catalog that references them.
    }

    public void VerifyInstalledFiles(IReadOnlyList<FileIntegrity.Entry> files)
    {
        const int batch = 25;
        for (int start = 0; start < files.Count; start += batch)
        {
            var chunk = files.Skip(start).Take(batch).ToArray();
            string command = "cd " + Quote(Addressables) + " && sha256sum " + string.Join(" ", chunk.Select(file => Quote(FileIntegrity.Relative(file.Path))));
            string[] rows = Run("shell", command).Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (rows.Length != chunk.Length) throw new IOException("设备文件校验结果不完整。");
            foreach (var item in chunk)
                if (!rows.Any(row => row.TrimEnd().Equals(item.Sha256.ToLowerInvariant() + "  " + item.Path, StringComparison.OrdinalIgnoreCase)))
                    throw new IOException("手机文件哈希不匹配，可能被截断：" + item.Path);
            Progress?.Invoke(UiText.F($"读回校验手机文件 {Math.Min(start + batch, files.Count)}/{files.Count}"));
        }
    }

    private void RestoreCatalog(Installation installation)
    {
        string original = Path.Combine(installation.BackupDirectory, installation.CatalogName);
        if (FileIntegrity.Hash(original) != installation.BeforeSha256) throw new IOException("本地恢复备份校验失败。");
        string staged = "/data/local/tmp/jixmod-restore-" + installation.PackageId + ".json";
        try
        {
            Run("push", original, staged);
            if (RemoteHash(staged) != installation.BeforeSha256) throw new IOException("恢复暂存文件不完整。");
            Run("shell", "dd if=" + Quote(staged) + " of=" + Quote(Addressables + "/" + installation.CatalogName) + " bs=65536 conv=fsync status=none");
            if (RemoteHash(Addressables + "/" + installation.CatalogName) != installation.BeforeSha256) throw new IOException("恢复后的资源表校验失败。");
        }
        finally { try { Run("shell", "rm -f " + Quote(staged)); } catch { } }
    }

    private static void Save(Installation installation)
    {
        string path = Path.Combine(installation.BackupDirectory, "installation.json");
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(installation, MobileTexturePackage.Json));
        File.Move(temporary, path, true);
    }

    public string Version()
    {
        string version = Regex.Match(Run("shell", "dumpsys package " + Package), @"versionName=(\d+(?:\.\d+){1,3})(?:\s|$)").Groups[1].Value;
        return version.Length > 0 ? version : throw new InvalidDataException("未安装可识别版本的吉星派对。");
    }

    private void EnsureClosed()
    {
        if (Run("shell", "if pidof " + Package + " >/dev/null; then echo RUNNING; fi").Contains("RUNNING", StringComparison.Ordinal))
            throw new InvalidOperationException("游戏仍在运行，请先在手机上退出游戏。未提交本次安装。");
    }

    private string RemoteHash(string path)
    {
        var match = Regex.Match(Run("shell", "sha256sum " + Quote(path)), @"^[0-9a-fA-F]{64}(?=\s)");
        return match.Success ? match.Value.ToUpperInvariant() : throw new IOException("无法读取设备文件哈希：" + path);
    }

    private void PullVerified(string remote, string local, string expected = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(local)!);
        expected ??= RemoteHash(remote);
        string temporary = local + ".pulling";
        try
        {
            Run("pull", remote, temporary);
            if (FileIntegrity.Hash(temporary) != expected || RemoteHash(remote) != expected) throw new IOException("手机读取结果不完整或源文件发生变化：" + remote);
            File.Move(temporary, local, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private string Run(params string[] arguments) => RunRaw(new[] { "-s", Serial }.Concat(arguments).ToArray());
    private string RunRaw(params string[] arguments)
    {
        var info = new ProcessStartInfo(Adb) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in arguments) info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new IOException("无法启动 ADB。");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(300000)) { process.Kill(true); throw new TimeoutException("ADB 超时，请检查 USB 连接。"); }
        string stdout = output.GetAwaiter().GetResult(), stderr = error.GetAwaiter().GetResult();
        if (process.ExitCode != 0) throw new IOException("ADB 操作失败：" + (stderr + "\n" + stdout).Trim());
        return stdout;
    }
    private static string Quote(string value) => "'" + value.Replace("'", "'\\''") + "'";
}
