using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace JixModMaker;

public static class PortraitReplacement
{
    public const string SafetyNotice = "使用游戏原生视频槽位；替换前请关闭游戏。";
    public sealed record PortraitEntry(string VideoKey, string SourceName, bool RemoveGreen, DateTimeOffset InstalledAt, string Preview);
    public sealed record Receipt(string Texture, string VideoKey, string SourceName, bool RemoveGreen, DateTimeOffset InstalledAt,
        string Preview, string ReplacementZip, string RestoreZip, string BaselineRestoreZip, Dictionary<string, string> Hashes,
        Dictionary<string, PortraitEntry> Portraits = null);
    private static string HistoryRoot(string root) => Path.Combine(Path.GetFullPath(root), "_原始备份", "动态立绘");
    private static string StatePath(string root) => Path.Combine(HistoryRoot(root), "current.json");
    private static bool IsCacheInfo(string path) => Path.GetFileName(path) == "__info";

    public static Receipt ReadReceipt(string root, string texture = null)
    {
        try
        {
            var receipt = JsonSerializer.Deserialize<Receipt>(File.ReadAllText(StatePath(root)));
            if (receipt?.Hashes == null) return null;
            if (texture == null || receipt.Texture == texture) return receipt;
            return receipt.Portraits != null && receipt.Portraits.TryGetValue(texture, out var entry)
                ? receipt with { Texture = texture, VideoKey = entry.VideoKey, SourceName = entry.SourceName, RemoveGreen = entry.RemoveGreen,
                    InstalledAt = entry.InstalledAt, Preview = entry.Preview } : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or ArgumentException) { return null; }
    }

    public static bool IsInstalled(string root, Receipt receipt) => receipt != null && receipt.Hashes.Count > 0 &&
        receipt.Hashes.All(pair =>
        {
            try
            {
                string path = TargetPath(root, pair.Key);
                using var stream = File.OpenRead(path);
                return Convert.ToHexString(SHA256.HashData(stream)) == pair.Value;
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) { return false; }
        });

    public static Receipt Apply(AnimatedPortraitPatch.Request request, string preview = null, string sourceName = "", bool removeGreen = false)
    {
        EnsureGameClosed();
        var previous = ReadReceipt(request.Root);
        if (previous != null && !IsInstalled(request.Root, previous))
        {
            if (ArchiveMatches(request.Root, previous.BaselineRestoreZip)) previous = null;
            else throw new IOException("资源与上次记录不同，可能已被游戏更新或其他 Mod 改动。请使用新的干净资源副本制作，不能沿用旧备份。");
        }
        if (previous != null && previous.VideoKey.StartsWith("JixPortrait_", StringComparison.Ordinal))
            throw new IOException("当前资源含独立动态资源补丁。请先点击“恢复原资源”，再使用原生视频槽位模式。");
        string backup = Path.Combine(HistoryRoot(request.Root), DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(backup);
        string savedPreview = null;
        if (preview != null)
        {
            savedPreview = Path.Combine(backup, "preview.gif");
            File.Copy(preview, savedPreview);
        }
        var result = AnimatedPortraitPatch.Export(request with { Destination = Path.Combine(backup, "replacement.zip") });
        var hashes = new Dictionary<string, string>();
        using (var zip = ZipFile.OpenRead(result.ReplacementZip))
        foreach (var entry in zip.Entries)
        {
            if (IsCacheInfo(entry.FullName)) continue;
            using var stream = entry.Open();
            hashes.Add(entry.FullName, Convert.ToHexString(SHA256.HashData(stream)));
        }
        var now = DateTimeOffset.Now;
        var receipt = new Receipt(request.TextureName, result.VideoKey, sourceName, removeGreen, now,
            savedPreview, result.ReplacementZip, result.RestoreZip, previous?.BaselineRestoreZip ?? result.RestoreZip, hashes);
        string stagedState = Path.Combine(backup, "receipt.json");
        File.WriteAllText(stagedState, JsonSerializer.Serialize(receipt));
        EnsureGameClosed();
        Install(request.Root, result.ReplacementZip, result.RestoreZip);
        try
        {
            if (!IsInstalled(request.Root, receipt)) throw new IOException("写入后的资源哈希不匹配");
            File.Copy(stagedState, StatePath(request.Root), true);
        }
        catch (Exception failure)
        {
            try { Install(request.Root, result.RestoreZip, result.ReplacementZip); }
            catch (Exception rollback) { throw new AggregateException("写入校验与回滚失败，请保留备份：" + result.RestoreZip, failure, rollback); }
            throw;
        }
        return receipt;
    }

    public static void Restore(string root)
    {
        EnsureGameClosed();
        var receipt = ReadReceipt(root) ?? throw new IOException("没有动态替换记录");
        if (!IsInstalled(root, receipt)) throw new IOException("游戏资源已改变，停止恢复，避免覆盖游戏更新或其他 Mod。");
        Install(root, receipt.BaselineRestoreZip, receipt.ReplacementZip);
        if (!ArchiveMatches(root, receipt.BaselineRestoreZip)) throw new IOException("恢复后的资源校验失败，请保留备份。");
        File.Delete(StatePath(root));
    }

    private static bool ArchiveMatches(string root, string archive)
    {
        try
        {
            using var zip = ZipFile.OpenRead(archive);
            return zip.Entries.Any(e => !IsCacheInfo(e.FullName)) && zip.Entries.Where(e => !IsCacheInfo(e.FullName)).All(entry =>
            {
                using var expected = entry.Open();
                using var actual = File.OpenRead(TargetPath(root, entry.FullName));
                return SHA256.HashData(expected).AsSpan().SequenceEqual(SHA256.HashData(actual));
            });
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) { return false; }
    }

    public static void EnsureGameClosed()
    {
        var games = System.Diagnostics.Process.GetProcessesByName("AstralParty_CN");
        bool running = games.Length > 0;
        foreach (var game in games) game.Dispose();
        if (running) throw new IOException("请先关闭游戏，再执行替换或恢复。");
    }

    private static string TargetPath(string root, string relative)
    {
        root = Path.GetFullPath(root);
        string target = Path.GetFullPath(Path.Combine(root, relative));
        string check = Path.GetRelativePath(root, target);
        if (Path.IsPathRooted(check) || check == ".." || check.StartsWith(".." + Path.DirectorySeparatorChar))
            throw new InvalidDataException("替换包路径不合法");
        return target;
    }

    // Stage every file on the target volume before replacing anything; roll back committed files on failure.
    public static void Install(string root, string replacement, string restore)
    {
        root = Path.GetFullPath(root);
        using var zip = ZipFile.OpenRead(replacement);
        using var original = ZipFile.OpenRead(restore);
        var staged = new List<(string Target, string Temp, ZipArchiveEntry Original)>();
        var committed = new List<(string Target, ZipArchiveEntry Original)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var entry in zip.Entries)
            {
                // Unity updates __info access timestamps independently of the actual bundle.
                if (IsCacheInfo(entry.FullName)) continue;
                string target = TargetPath(root, entry.FullName);
                if (!seen.Add(target))
                    throw new InvalidDataException("替换包路径不合法");
                var source = original.GetEntry(entry.FullName) ?? throw new InvalidDataException("缺少恢复文件");
                if (!File.Exists(target)) throw new FileNotFoundException("目标资源已移动", target);
                using (var current = File.OpenRead(target))
                using (var saved = source.Open())
                    if (!System.Security.Cryptography.SHA256.HashData(current).AsSpan().SequenceEqual(System.Security.Cryptography.SHA256.HashData(saved)))
                        throw new IOException("目标文件已被其他程序修改，请重新替换");
                string temp = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
                staged.Add((target, temp, source));
                entry.ExtractToFile(temp);
            }
            foreach (var item in staged)
            {
                File.Replace(item.Temp, item.Target, null);
                committed.Add((item.Target, item.Original));
            }
        }
        catch (Exception failure)
        {
            var failures = new List<Exception> { failure };
            foreach (var item in committed.AsEnumerable().Reverse())
            {
                try { item.Original.ExtractToFile(item.Target, true); }
                catch (Exception rollback) { failures.Add(rollback); }
            }
            if (failures.Count > 1) throw new AggregateException("部分文件恢复失败，请使用恢复包：" + restore, failures);
            throw;
        }
        finally { foreach (var item in staged) if (File.Exists(item.Temp)) File.Delete(item.Temp); }
    }
}
