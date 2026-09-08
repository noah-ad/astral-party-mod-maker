using System.IO.Compression;

namespace JixModMaker;

public static class ReplacementZip
{
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
