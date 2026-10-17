using System.IO.Compression;

namespace JixModMaker;

public static class ReplacementZip
{
    public sealed record Source(string Root, string Label);
    public sealed record ExportPart(string Root, string Label, string Destination, IReadOnlyList<string> Paths);
    public sealed record ExportResult(string Label, string Destination, int Files);

    public static IReadOnlyList<ExportPart> Plan(IEnumerable<Source> sources, IEnumerable<string> paths, string destination)
    {
        var roots = sources.Where(source => !string.IsNullOrWhiteSpace(source.Root))
            .Select(source => source with { Root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(source.Root)) })
            .DistinctBy(source => source.Root, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(source => source.Root.Length).ToList();
        var groups = new Dictionary<Source, List<string>>();
        foreach (string path in paths.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var source = roots.FirstOrDefault(candidate => IsUnderRoot(candidate.Root, path));
            if (source == null) throw new InvalidOperationException("无法确定资源所属目录，请重新打开资源目录后导出：" + path);
            if (!File.Exists(path)) throw new FileNotFoundException("资源文件不存在", path);
            if (!groups.TryGetValue(source, out var files)) groups[source] = files = new();
            files.Add(path);
        }
        if (groups.Count == 0) throw new InvalidOperationException("没有选择资源");
        destination = Path.GetFullPath(destination);
        var parts = groups.Select(group => new ExportPart(group.Key.Root, group.Key.Label,
            groups.Count == 1 ? destination : Path.Combine(Path.GetDirectoryName(destination)!,
                Path.GetFileNameWithoutExtension(destination) + "-" + group.Key.Label + ".zip"), group.Value))
            .OrderBy(part => part.Label, StringComparer.Ordinal).ToList();
        if (parts.Select(part => part.Destination).Distinct(StringComparer.OrdinalIgnoreCase).Count() != parts.Count)
            throw new InvalidOperationException("导出文件名重复，请分别打开资源目录后导出。");
        if (parts.Any(part => parts.Any(other => other.Paths.Contains(part.Destination, StringComparer.OrdinalIgnoreCase))))
            throw new InvalidOperationException("导出位置不能覆盖所选资源文件。");
        return parts;
    }

    public static IReadOnlyList<ExportResult> Export(IReadOnlyList<ExportPart> parts)
    {
        if (parts.Count == 0) throw new InvalidOperationException("没有选择资源");
        var prepared = new List<(ExportPart Part, string Temporary, string Backup, int Files)>();
        var committed = new List<(ExportPart Part, string Backup)>();
        bool preserveBackups = false;
        try
        {
            foreach (var part in parts)
            {
                if (Directory.Exists(part.Destination)) throw new IOException("导出位置是文件夹：" + part.Destination);
                string temporary = part.Destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
                string backup = File.Exists(part.Destination) ? temporary + ".previous" : null;
                prepared.Add((part, temporary, backup, 0));
                int files = Export(part.Root, part.Paths, temporary);
                if (backup != null) File.Copy(part.Destination, backup);
                prepared[^1] = (part, temporary, backup, files);
            }
            foreach (var item in prepared)
            {
                File.Move(item.Temporary, item.Part.Destination, true);
                committed.Add((item.Part, item.Backup));
            }
            return prepared.Select(item => new ExportResult(item.Part.Label, item.Part.Destination, item.Files)).ToList();
        }
        catch (Exception error)
        {
            var failures = new List<Exception> { error };
            foreach (var item in committed.AsEnumerable().Reverse())
            {
                try
                {
                    if (item.Backup != null) File.Move(item.Backup, item.Part.Destination, true);
                    else File.Delete(item.Part.Destination);
                }
                catch (Exception rollbackError) { failures.Add(rollbackError); preserveBackups = true; }
            }
            if (failures.Count > 1) throw new AggregateException("导出未完成，旧压缩包备份保留在导出目录。", failures);
            throw;
        }
        finally
        {
            foreach (var item in prepared)
            {
                if (File.Exists(item.Temporary)) File.Delete(item.Temporary);
                if (!preserveBackups && item.Backup != null && File.Exists(item.Backup)) File.Delete(item.Backup);
            }
        }
    }

    private static bool IsUnderRoot(string root, string path)
    {
        string relative = Path.GetRelativePath(root, path);
        return !Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar);
    }

    public static int Export(string root, IEnumerable<string> paths, string destination)
    {
        root = Path.GetFullPath(root);
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var full = Path.GetFullPath(path);
            var relative = Path.GetRelativePath(root, full).Replace('\\', '/');
            if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith("../"))
                throw new InvalidOperationException("资源不在当前目录内，请打开它所在的资源根目录后导出：" + full);
            if (!File.Exists(full)) throw new FileNotFoundException("资源文件不存在", full);
            files[relative] = full;
            if (ResourceLocator.IsWrappedData(full))
            {
                var info = Path.Combine(Path.GetDirectoryName(full)!, "__info");
                files[Path.GetRelativePath(root, info).Replace('\\', '/')] = info;
            }
        }
        if (files.Count == 0) throw new InvalidOperationException("没有选择资源");
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var zip = ZipFile.Open(temporary, ZipArchiveMode.Create))
                foreach (var file in files.OrderBy(p => p.Key, StringComparer.Ordinal))
                    zip.CreateEntryFromFile(file.Value, file.Key, CompressionLevel.Optimal);
            File.Move(temporary, destination, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return files.Count;
    }
}
