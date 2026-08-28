namespace JixModMaker;

/// <summary>统一定位普通 PC bundle 与 B服 __data/__info 套壳资源。</summary>
public static class ResourceLocator
{
    public static string NormalizeGameDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return path;
        try
        {
            for (var current = new DirectoryInfo(Path.GetFullPath(path)); current != null; current = current.Parent)
            {
                if (File.Exists(Path.Combine(current.FullName, "__info"))
                    && string.Equals(current.Parent?.Name, "AssetBundles", StringComparison.OrdinalIgnoreCase))
                    return current.Parent.FullName;

                if (string.Equals(current.Name, "AssetBundles", StringComparison.OrdinalIgnoreCase)
                    && (File.Exists(Path.Combine(current.FullName, "__info"))
                        || Directory.EnumerateDirectories(current.FullName)
                            .Take(32)
                            .Any(child => File.Exists(Path.Combine(child, "__info")))))
                    return current.FullName;
            }
        }
        catch { }
        return path;
    }

    public static bool IsWrappedData(string path)
        => string.Equals(Path.GetFileName(path), "__data", StringComparison.OrdinalIgnoreCase)
           && File.Exists(Path.Combine(Path.GetDirectoryName(path)!, "__info"));

    public static bool HasWrappedResources(string root)
    {
        if (!Directory.Exists(root)) return false;
        try
        {
            return Directory.EnumerateFiles(root, "__data", SearchOption.AllDirectories)
                .Any(IsWrappedData);
        }
        catch { return false; }
    }

    public static List<string> EnumerateResourceFiles(string root)
    {
        root = NormalizeGameDirectory(root);
        if (!Directory.Exists(root)) return new();

        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in Directory.EnumerateFiles(root, "*.bundle", SearchOption.AllDirectories))
            if (!IsBackupPath(root, path)) result.Add(path);
        foreach (var path in Directory.EnumerateFiles(root, "__data", SearchOption.AllDirectories))
            if (IsWrappedData(path) && !IsBackupPath(root, path)) result.Add(path);
        return result.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static string RelativePath(string root, string resourcePath)
        => Path.GetRelativePath(NormalizeGameDirectory(root), resourcePath);

    public static string BackupPath(string resourcePath, string backupDir, string backupName = null)
    {
        if (!IsWrappedData(resourcePath))
            return Path.Combine(backupDir, ModEngine.SafeBackupName(backupName ?? Path.GetFileName(resourcePath)));

        var gameRoot = Directory.GetParent(backupDir)?.FullName;
        if (string.IsNullOrWhiteSpace(gameRoot))
            return Path.Combine(backupDir, Path.GetFileName(resourcePath));

        var relative = Path.GetRelativePath(gameRoot, resourcePath);
        if (Path.IsPathRooted(relative)
            || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            return Path.Combine(backupDir, Path.GetFileName(resourcePath));
        return Path.Combine(backupDir, "__wrapped__", relative);
    }

    private static bool IsBackupPath(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        return relative.StartsWith("_原始备份" + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);
    }
}
