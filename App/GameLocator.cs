using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace JixModMaker;

public sealed class GameInstallInfo
{
    public string InstallDir { get; init; }
    public string ExePath { get; init; }
    public string AaDir { get; init; }
    public string HotCacheDir { get; init; }
}

public static class GameLocator
{
    public const string AppId = "2622000";

    public static GameInstallInfo Find()
    {
        foreach (var steamapps in SteamAppsDirs())
        {
            var manifest = Path.Combine(steamapps, $"appmanifest_{AppId}.acf");
            if (!File.Exists(manifest)) continue;

            var text = SafeRead(manifest);
            var installDirName = AcfValue(text, "installdir");
            if (string.IsNullOrWhiteSpace(installDirName)) continue;

            var installDir = Path.Combine(steamapps, "common", installDirName);
            var info = BuildFromInstallDir(installDir);
            if (info != null) return info;
        }

        foreach (var candidate in CommonInstallDirs())
        {
            var info = BuildFromInstallDir(candidate);
            if (info != null) return info;
        }

        return null;
    }

    private static GameInstallInfo BuildFromInstallDir(string installDir)
    {
        if (!Directory.Exists(installDir)) return null;
        var exe = FindGameExe(installDir);
        var aa = exe != null ? AaDirFromExe(exe) : null;
        if (string.IsNullOrWhiteSpace(aa) || !Directory.Exists(aa))
            aa = FindAaDir(installDir);
        if (string.IsNullOrWhiteSpace(aa) || !Directory.Exists(aa)) return null;

        return new GameInstallInfo
        {
            InstallDir = installDir,
            ExePath = exe ?? "",
            AaDir = aa,
            HotCacheDir = IndexService.HotCacheDir()
        };
    }

    private static string FindGameExe(string installDir)
    {
        foreach (var rel in new[]
        {
            @"8vJXn6CN\AstralParty_CN.exe",
            @"AstralParty_CN.exe",
            @"AstralParty.exe"
        })
        {
            var p = Path.Combine(installDir, rel);
            if (File.Exists(p)) return p;
        }

        try
        {
            return Directory.GetFiles(installDir, "AstralParty*.exe", SearchOption.AllDirectories)
                .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}_", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(p => Path.GetFileName(p).Contains("_CN", StringComparison.OrdinalIgnoreCase))
                .ThenBy(p => p.Length)
                .FirstOrDefault();
        }
        catch { return null; }
    }

    private static string AaDirFromExe(string exe)
    {
        if (string.IsNullOrWhiteSpace(exe)) return null;
        var dataDir = Path.Combine(Path.GetDirectoryName(exe)!, Path.GetFileNameWithoutExtension(exe) + "_Data");
        var aa = Path.Combine(dataDir, "StreamingAssets", "aa", "StandaloneWindows64");
        return Directory.Exists(aa) ? aa : null;
    }

    private static string FindAaDir(string installDir)
    {
        foreach (var rel in new[]
        {
            @"8vJXn6CN\AstralParty_CN_Data\StreamingAssets\aa\StandaloneWindows64",
            @"AstralParty_CN_Data\StreamingAssets\aa\StandaloneWindows64",
            @"AstralParty_Data\StreamingAssets\aa\StandaloneWindows64"
        })
        {
            var p = Path.Combine(installDir, rel);
            if (Directory.Exists(p)) return p;
        }

        try
        {
            return Directory.GetDirectories(installDir, "StandaloneWindows64", SearchOption.AllDirectories)
                .FirstOrDefault(p =>
                    p.Contains($"{Path.DirectorySeparatorChar}StreamingAssets{Path.DirectorySeparatorChar}aa{Path.DirectorySeparatorChar}",
                        StringComparison.OrdinalIgnoreCase));
        }
        catch { return null; }
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
