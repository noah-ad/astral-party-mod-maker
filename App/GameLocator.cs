using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace JixModMaker;

public enum GameEdition { China, International }

public sealed class GameInstallInfo
{
    public string InstallDir { get; init; }
    public string ExePath { get; init; }
    public string AaDir { get; init; }
    public string HotCacheDir { get; init; }
    public GameEdition? Edition => GameLocator.EditionOf(AaDir);
}

public static class GameLocator
{
    public const string AppId = "2622000";
    private const string Platform = "StandaloneWindows64";
    private static readonly string[] Products = { "AstralParty_CN", "AstralParty_INT", "AstralParty" };
    private static readonly string[] Launchers = { "8vJXn6CN", "8vJXnINT" };

    public static GameInstallInfo Find() => Find(CancellationToken.None);

    public static GameInstallInfo Find(CancellationToken token, GameEdition? edition = null)
    {
        foreach (var steamapps in SteamAppsDirs())
        {
            token.ThrowIfCancellationRequested();
            var manifest = Path.Combine(steamapps, $"appmanifest_{AppId}.acf");
            if (!File.Exists(manifest)) continue;

            var text = SafeRead(manifest);
            var installDirName = AcfValue(text, "installdir");
            if (string.IsNullOrWhiteSpace(installDirName)) continue;

            var installDir = Path.Combine(steamapps, "common", installDirName);
            var language = AcfValue(text, "language");
            var preferred = language.Length == 0 ? (GameEdition?)null
                : language.Equals("schinese", StringComparison.OrdinalIgnoreCase) ? GameEdition.China : GameEdition.International;
            var info = BuildFromInstallDir(installDir, edition, preferred);
            if (info != null) return info;
        }

        foreach (var candidate in CommonInstallDirs())
        {
            token.ThrowIfCancellationRequested();
            var info = BuildFromInstallDir(candidate, edition);
            if (info != null) return info;
        }

        return null;
    }

    private static GameInstallInfo BuildFromInstallDir(string installDir, GameEdition? edition, GameEdition? preferred = null)
    {
        if (!Directory.Exists(installDir)) return null;
        var aa = ResolvePcResources(installDir, edition ?? preferred)
            ?? (edition == null && preferred != null ? ResolvePcResources(installDir) : null);
        if (aa == null) return null;
        var data = Directory.GetParent(aa)?.Parent?.Parent;
        string exe = data?.Name.EndsWith("_Data", StringComparison.OrdinalIgnoreCase) == true
            ? Path.Combine(data.Parent.FullName, data.Name[..^5] + ".exe") : null;

        return new GameInstallInfo
        {
            InstallDir = installDir,
            ExePath = File.Exists(exe) ? exe : "",
            AaDir = aa,
            HotCacheDir = IndexService.UserCacheFor(aa)
        };
    }

    internal static string ProductOf(string dataDir)
        => dataDir == null ? null : Products.FirstOrDefault(p =>
            string.Equals(Path.GetFileName(dataDir), p + "_Data", StringComparison.OrdinalIgnoreCase));

    internal static GameEdition? EditionOf(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        for (var dir = new DirectoryInfo(path); dir != null; dir = dir.Parent)
        {
            if (ProductOf(dir.FullName) is "AstralParty_INT") return GameEdition.International;
            if (ProductOf(dir.FullName) is "AstralParty_CN") return GameEdition.China;
        }
        return null;
    }

    public static string ResolvePcResources(string selected, GameEdition? edition = null)
    {
        if (!Directory.Exists(selected)) return null;
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(selected));
        string Candidate(string path) => Directory.Exists(path) && (edition == null || EditionOf(path) == edition) ? path : null;
        var dataDir = RuntimeBundleLocator.GameDataDirectory(root);
        // Selecting any level of the player's Addressables cache opens that player's full library.
        if (ProductOf(dataDir) != null)
        {
            string relative = Path.GetRelativePath(dataDir, root).Replace('\\', '/');
            if (relative == "." || relative.Equals("StreamingAssets", StringComparison.OrdinalIgnoreCase)
                || relative.Equals("StreamingAssets/aa", StringComparison.OrdinalIgnoreCase)
                || relative.Equals("com.unity.addressables", StringComparison.OrdinalIgnoreCase)
                || relative.Equals("com.unity.addressables/AssetBundles", StringComparison.OrdinalIgnoreCase)
                || relative.StartsWith("com.unity.addressables/AssetBundles/", StringComparison.OrdinalIgnoreCase))
                return Candidate(Path.Combine(dataDir, "StreamingAssets", "aa", Platform));
        }
        if (Path.GetFileName(root).Equals(Platform, StringComparison.OrdinalIgnoreCase)) return Candidate(root);
        if (Path.GetFileName(root).Equals("AssetBundles", StringComparison.OrdinalIgnoreCase)
            || File.Exists(Path.Combine(root, "__info"))) return null;
        foreach (string suffix in new[] { Platform, @"aa\StandaloneWindows64", @"StreamingAssets\aa\StandaloneWindows64" })
            if (Candidate(Path.Combine(root, suffix)) is { } direct) return direct;
        string ForPlayer(string folder)
        {
            foreach (string product in Products)
                if (Candidate(Path.Combine(folder, product + "_Data", "StreamingAssets", "aa", Platform)) is { } match)
                    return match;
            return null;
        }
        if (ForPlayer(root) is { } player) return player;
        foreach (string launcher in Launchers)
            if (ForPlayer(Path.Combine(root, launcher)) is { } match) return match;
        // A distribution may rename its launcher subdirectory. Never traverse the bundle/cache trees.
        var opts = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        foreach (string child in Directory.EnumerateDirectories(root, "*", opts))
            if (ForPlayer(child) is { } match) return match;
        return null;
    }

    private static IEnumerable<string> SteamAppsDirs()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in SteamRootCandidates())
        {
            var steamapps = Path.Combine(root, "steamapps");
            if (Directory.Exists(steamapps)) roots.Add(steamapps);
            var library = Path.Combine(steamapps, "libraryfolders.vdf");
            if (!File.Exists(library)) continue;

            var text = SafeRead(library);
            foreach (Match m in Regex.Matches(text, "\"path\"\\s+\"(?<p>[^\"]+)\""))
            {
                var p = m.Groups["p"].Value.Replace(@"\\", @"\");
                var libSteamApps = Path.Combine(p, "steamapps");
                if (Directory.Exists(libSteamApps)) roots.Add(libSteamApps);
            }
        }
        return roots;
    }

    private static IEnumerable<string> SteamRootCandidates()
    {
        foreach (var keyPath in new[]
        {
            @"HKEY_CURRENT_USER\Software\Valve\Steam",
            @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam",
            @"HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam"
        })
        {
            var value = Registry.GetValue(keyPath, "SteamPath", null) ?? Registry.GetValue(keyPath, "InstallPath", null);
            if (value is string s && Directory.Exists(s)) yield return s;
        }

        foreach (var p in new[] { @"C:\Program Files (x86)\Steam", @"C:\Program Files\Steam", @"D:\steam", @"D:\Steam", @"E:\Steam", @"F:\Steam" })
            if (Directory.Exists(p)) yield return p;
    }

    private static IEnumerable<string> CommonInstallDirs()
    {
        foreach (var root in new[]
        {
            @"C:\Program Files (x86)\Steam",
            @"C:\Program Files\Steam",
            @"D:\steam",
            @"D:\Steam",
            @"E:\Steam",
            @"F:\Steam"
        })
        {
            yield return Path.Combine(root, @"steamapps\common\Astral Party");
        }

        for (char d = 'C'; d <= 'Z'; d++)
        {
            var drive = d + @":\";
            yield return Path.Combine(drive, @"SteamLibrary\steamapps\common\Astral Party");
            yield return Path.Combine(drive, @"steamapps\common\Astral Party");
        }
    }

    private static string AcfValue(string text, string key)
    {
        var m = Regex.Match(text, "\"" + Regex.Escape(key) + "\"\\s+\"(?<v>[^\"]+)\"");
        return m.Success ? m.Groups["v"].Value : "";
    }

    private static string SafeRead(string path)
    {
        try { return File.ReadAllText(path); }
        catch { return ""; }
    }
}
