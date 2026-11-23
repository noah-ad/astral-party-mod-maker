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
                if (string.Equals(current.Name, "AssetBundles", StringComparison.OrdinalIgnoreCase)
                    && (string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)), current.FullName, StringComparison.OrdinalIgnoreCase)
                        || File.Exists(Path.Combine(path, "__info"))))
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

    public static List<string> EnumerateResourceFiles(string root, CancellationToken token = default, bool nestedBundles = true)
    {
        root = NormalizeGameDirectory(root);
        if (!Directory.Exists(root)) return new();

        var result = new List<string>();
        var dirs = new Stack<DirectoryInfo>();
        dirs.Push(new DirectoryInfo(root));
        var options = new EnumerationOptions { IgnoreInaccessible = false, AttributesToSkip = FileAttributes.ReparsePoint };
        while (dirs.TryPop(out var dir))
        foreach (var item in dir.EnumerateFileSystemInfos("*", options))
        {
            token.ThrowIfCancellationRequested();
            if (item is DirectoryInfo child)
            {
                if (!child.Name.Equals("_原始备份", StringComparison.OrdinalIgnoreCase)
                    && (nestedBundles || !dir.Name.Equals("StandaloneWindows64", StringComparison.OrdinalIgnoreCase)))
                    dirs.Push(child);
            }
            else if ((nestedBundles || string.Equals(dir.FullName, root, StringComparison.OrdinalIgnoreCase))
                && item.Extension.Equals(".bundle", StringComparison.OrdinalIgnoreCase) || IsWrappedData(item.FullName))
                result.Add(item.FullName);
        }
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
        {
            string logicalName = backupName;
            if (string.IsNullOrWhiteSpace(logicalName))
            {
                string owner = Path.GetFileName(Path.GetDirectoryName(resourcePath));
                logicalName = string.IsNullOrWhiteSpace(owner) ? Path.GetFileName(resourcePath) : owner + ".bundle";
            }
            return Path.Combine(backupDir, ModEngine.SafeBackupName(logicalName));
        }
        return Path.Combine(backupDir, "__wrapped__", relative);
    }

}
