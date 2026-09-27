using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace JixModMaker;

// The replacement ZIP contains only game files. The restore ZIP also records files that did not exist.
public static class IndependentVideoTransaction
{
    private const string AbsenceManifest = "_jix_independent_restore.json";

    public static string TargetPath(string root, string relative)
    {
        root = Path.GetFullPath(root);
        if (string.IsNullOrWhiteSpace(relative) || relative.Contains(':') || relative.Contains('\\') ||
            relative.Split('/').Any(s => s is "" or "." or "..")) throw new InvalidDataException("Invalid video package path");
        string target = Path.GetFullPath(Path.Combine(root, relative));
        if (!target.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Video package escapes its root");
        for (string current = target; current != null; current = Path.GetDirectoryName(current))
        {
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Video package paths cannot traverse a link: " + current);
            if (string.Equals(current, root, StringComparison.OrdinalIgnoreCase)) break;
        }
        return target;
    }

    public static void Write(string destination, IReadOnlyDictionary<string, string> files, IEnumerable<string> absent)
    {
        using var stream = new FileStream(destination, FileMode.CreateNew, FileAccess.Write);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);
        foreach (var pair in files.OrderBy(p => p.Key, StringComparer.Ordinal))
            zip.CreateEntryFromFile(pair.Value, pair.Key, CompressionLevel.Optimal);
        var missing = absent.Order(StringComparer.Ordinal).ToArray();
        if (missing.Length == 0) return;
        foreach (string path in missing) ValidateAbsent(path);
        using var writer = zip.CreateEntry(AbsenceManifest).Open();
        JsonSerializer.Serialize(writer, missing);
    }

    public static bool Matches(string root, string archive)
    {
        try
        {
            using var zip = ZipFile.OpenRead(archive);
            return ReadState(zip).All(pair => MatchesFile(TargetPath(root, pair.Key), pair.Value));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or JsonException) { return false; }
    }

    public static void Apply(string root, string replacement, string expected, bool restoreMissingVideos = false)
    {
        using var afterZip = ZipFile.OpenRead(replacement);
        using var beforeZip = ZipFile.OpenRead(expected);
        var after = ReadState(afterZip);
        var before = ReadState(beforeZip);
        if (!after.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(before.Keys))
            throw new InvalidDataException("Video replacement and recovery states differ");
        if (restoreMissingVideos)
        {
            foreach (string relative in after.Where(p => p.Value == null).Select(p => p.Key))
            {
                ValidateAbsent(relative);
                string path = TargetPath(root, relative);
                if (!File.Exists(path) && !Directory.Exists(path)) before[relative] = null;
            }
        }
        var staged = new List<(string Path, string Temp, string OriginalTemp, ZipArchiveEntry Before)>();
        var committed = new List<(string Path, string OriginalTemp, ZipArchiveEntry Before)>();
        try
        {
            foreach (var pair in after)
            {
                string path = TargetPath(root, pair.Key);
                var original = before[pair.Key];
                if (!MatchesFile(path, original)) throw new IOException("资源已改变，已停止写入：" + pair.Key);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                string originalTemp = temp + ".original";
                staged.Add((path, temp, originalTemp, original));
                pair.Value?.ExtractToFile(temp);
                original?.ExtractToFile(originalTemp);
            }
            foreach (var item in staged)
                if (!MatchesFile(item.Path, item.Before)) throw new IOException("资源在准备期间发生变化，已停止写入。");
            foreach (var item in staged)
            {
                if (!MatchesFile(item.Path, item.Before)) throw new IOException("资源在写入期间发生变化，已停止写入。");
                if (File.Exists(item.Temp))
                {
                    if (item.Before == null) File.Move(item.Temp, item.Path);
                    else File.Replace(item.Temp, item.Path, null);
                }
                else File.Delete(item.Path);
                committed.Add((item.Path, item.OriginalTemp, item.Before));
            }
        }
        catch (Exception failure)
        {
            var failures = new List<Exception> { failure };
            foreach (var item in committed.AsEnumerable().Reverse())
            {
                try
                {
                    if (item.Before == null) File.Delete(item.Path);
                    else if (File.Exists(item.Path)) File.Replace(item.OriginalTemp, item.Path, null);
                    else File.Move(item.OriginalTemp, item.Path);
                }
                catch (Exception rollback) { failures.Add(rollback); }
            }
            if (failures.Count > 1) throw new AggregateException("独立视频回滚未完成，请保留恢复包：" + expected, failures);
            throw;
        }
        finally
        {
            foreach (var item in staged)
            {
                if (File.Exists(item.Temp)) File.Delete(item.Temp);
                if (File.Exists(item.OriginalTemp)) File.Delete(item.OriginalTemp);
            }
        }
    }

    private static Dictionary<string, ZipArchiveEntry> ReadState(ZipArchive zip)
    {
        var files = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in zip.Entries)
        {
            if (Path.GetFileName(entry.FullName) == "__info") continue;
            if (entry.FullName == AbsenceManifest)
            {
                using var stream = entry.Open();
                var absent = JsonSerializer.Deserialize<string[]>(stream) ?? throw new InvalidDataException("Invalid recovery manifest");
                foreach (string path in absent)
                {
                    ValidateAbsent(path);
                    if (!files.TryAdd(path, null)) throw new InvalidDataException("Duplicate video recovery path");
                }
            }
            else if (!files.TryAdd(entry.FullName, entry)) throw new InvalidDataException("Duplicate video package path");
        }
        if (files.Count == 0) throw new InvalidDataException("Empty video package");
        return files;
    }

    private static void ValidateAbsent(string path)
    {
        string prefix = IndependentVideoPatch.Folder + "/" + IndependentVideoPatch.KeyPrefix;
        if (path == null || !path.StartsWith(prefix, StringComparison.Ordinal) || !path.EndsWith(".usm", StringComparison.Ordinal) ||
            !string.Equals(IndependentVideoPatch.RelativeFile(path[prefix.Length..^4]), path, StringComparison.Ordinal))
            throw new InvalidDataException("Recovery may only delete generated independent USM files");
    }

    private static bool MatchesFile(string path, ZipArchiveEntry entry)
    {
        if (entry == null) return !File.Exists(path) && !Directory.Exists(path);
        if (!File.Exists(path) || new FileInfo(path).Length != entry.Length) return false;
        using var actual = File.OpenRead(path);
        using var saved = entry.Open();
        return SHA256.HashData(actual).AsSpan().SequenceEqual(SHA256.HashData(saved));
    }
}
