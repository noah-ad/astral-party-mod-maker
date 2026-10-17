using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace JixModMaker;

public enum ModificationState { Unverified, Recorded, Modified, Original }

public sealed class ModificationCatalog
{
    public sealed class BundleState
    {
        public string Stamp { get; set; }
        public string BackupPath { get; set; }
        public Dictionary<string, ModificationState> Textures { get; set; } = new(StringComparer.Ordinal);
        public List<ModEngine.TextureSignature> Signatures { get; set; } = new();
        public bool SameFile { get; set; }
    }

    public sealed record ScanResult(List<ModEntry> Recovered, int VerifiedBundles, int ReusedBundles, int FailedBundles, string CacheNotice = null)
    {
        public Dictionary<string, string> Errors { get; init; } = new();
    }
    private readonly List<TexIndexEntry> _assets;
    private readonly Dictionary<string, List<TexIndexEntry>> _byBundle;
    private readonly string[] _backupDirs;
    private readonly string _cachePath;
    private readonly ConcurrentDictionary<string, BundleState> _states = new(StringComparer.OrdinalIgnoreCase);

    public ModificationCatalog(GameIndex index, string cachePath = null)
    {
        _assets = (index.Lightweight ? index.EnumerateLightweightTextures().Concat(index.Items) : index.Items).ToList();
        _byBundle = _assets.Where(asset => !string.IsNullOrWhiteSpace(asset.Bundle))
            .GroupBy(asset => PackService.BundleKey(asset.Bundle), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);
        _backupDirs = new[] { index.GameDir, index.HotDir }.Where(Directory.Exists)
            .Select(root => Path.Combine(Path.GetFullPath(root), "_原始备份")).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        _cachePath = cachePath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JixModMaker",
            "mod-state-v1-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(index.GameDir).ToLowerInvariant())))[..12] + ".json");
        try
        {
            var saved = JsonSerializer.Deserialize<Dictionary<string, BundleState>>(File.ReadAllText(_cachePath));
            foreach (var pair in saved ?? new())
                if (pair.Value?.Textures != null && pair.Value.Signatures != null) _states[pair.Key] = pair.Value;
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        catch (JsonException) { }
    }

    public TexIndexEntry Find(ModEntry entry)
    {
        if (entry == null || !_byBundle.TryGetValue(PackService.BundleKey(entry.Bundle), out var assets)) return null;
        var matches = assets.Where(asset => asset.Kind == entry.Kind && (string.IsNullOrWhiteSpace(entry.TextureName)
            ? entry.PathId != 0 && asset.PathId == entry.PathId
            : string.Equals(asset.Name, entry.TextureName, StringComparison.Ordinal))).ToList();
        return matches.FirstOrDefault(asset => entry.PathId != 0 && asset.PathId == entry.PathId)
            ?? (matches.Count == 1 ? matches[0] : null);
    }

    public List<ModEntry> CurrentEntries(ModManifest workspace)
    {
        var current = new List<ModEntry>();
        foreach (var entry in workspace.Entries)
        {
            var asset = Find(entry);
            if (asset == null || string.IsNullOrWhiteSpace(asset.BundlePath) || !File.Exists(asset.BundlePath)
                || GetState(asset.BundlePath, asset.Name, true) == ModificationState.Original) continue;
            current.Add(new ModEntry
            {
                Bundle = asset.Bundle, TextureName = asset.Name, PathId = asset.PathId != 0 ? asset.PathId : entry.PathId,
                Width = asset.Width > 0 ? asset.Width : entry.Width, Height = asset.Height > 0 ? asset.Height : entry.Height,
                Kind = asset.Kind, Label = entry.Label, ModifiedAt = entry.ModifiedAt
            });
        }
        return current.GroupBy(entry => (Bundle: PackService.BundleKey(entry.Bundle).ToUpperInvariant(), entry.Kind, entry.TextureName))
            .Select(group => group.OrderByDescending(entry => entry.ModifiedAt).First()).ToList();
    }

    public ModificationState GetState(string path, string name, bool recorded)
    {
        if (!string.IsNullOrWhiteSpace(path) && _states.TryGetValue(Path.GetFullPath(path), out var state))
        {
            try
            {
                if (string.IsNullOrWhiteSpace(state.BackupPath) || Stamp(path, state.BackupPath) != state.Stamp)
                {
                    Invalidate(path);
                    return recorded ? ModificationState.Recorded : ModificationState.Unverified;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { Invalidate(path); return recorded ? ModificationState.Recorded : ModificationState.Unverified; }
            if (state.SameFile) return ModificationState.Original;
            if (name != null && state.Textures.TryGetValue(name, out var status)) return status;
        }
        return recorded ? ModificationState.Recorded : ModificationState.Unverified;
    }

    public void Invalidate(string path)
    {
        if (!string.IsNullOrWhiteSpace(path)) _states.TryRemove(Path.GetFullPath(path), out _);
    }

    public string FindBackup(string target, string bundle)
        => FindBackup(target, bundle, null);

    private string FindBackup(string target, string bundle, HashSet<string> knownBackups)
    {
        bool Exists(string candidate) => knownBackups == null ? File.Exists(candidate) : knownBackups.Contains(candidate);
        string fullTarget = Path.GetFullPath(target);
        foreach (string directory in _backupDirs)
        {
            if (Path.GetFileName(target).Equals("__data", StringComparison.OrdinalIgnoreCase))
            {
                string relative = Path.GetRelativePath(Path.GetDirectoryName(directory)!, fullTarget);
                if (!Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar))
                {
                    string nested = Path.Combine(directory, "__wrapped__", relative);
                    if (Exists(nested)) return nested;
                }
            }
            if (string.IsNullOrWhiteSpace(bundle)) continue;
            string name = ModEngine.SafeBackupName(bundle);
            if (name.Equals("__data", StringComparison.OrdinalIgnoreCase)) continue;
            string flat = Path.Combine(directory, name);
            if (Exists(flat) && !string.Equals(Path.GetFullPath(flat), fullTarget, StringComparison.OrdinalIgnoreCase)) return flat;
        }
        return null;
    }

    private static string Stamp(string target, string backup)
    {
        var current = new FileInfo(target);
        var original = new FileInfo(backup);
        return $"{current.Length}|{current.LastWriteTimeUtc.Ticks}|{original.FullName.ToLowerInvariant()}|{original.Length}|{original.LastWriteTimeUtc.Ticks}";
    }

    public ScanResult Scan(Action<int, int> progress = null, CancellationToken token = default)
    {
        // List backups once instead of probing the disk for every indexed game bundle.
        var backups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string directory in _backupDirs)
        {
            token.ThrowIfCancellationRequested();
            if (!Directory.Exists(directory)) continue;
            foreach (string path in Directory.EnumerateFiles(directory, "*", new EnumerationOptions
                { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint }))
            {
                token.ThrowIfCancellationRequested();
                backups.Add(Path.GetFullPath(path));
            }
        }
        var groups = _assets.Where(asset => !string.IsNullOrWhiteSpace(asset.BundlePath))
            .GroupBy(asset => Path.GetFullPath(asset.BundlePath), StringComparer.OrdinalIgnoreCase)
            .Select(group => (Path: group.Key, Assets: group.ToList(), Backup: FindBackup(group.Key, group.First().Bundle, backups)))
            .Where(group => group.Backup != null && File.Exists(group.Path)).ToList();
        var recovered = new List<ModEntry>();
        var errors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int verified = 0, reused = 0, failed = 0, done = 0;
        var engine = new ModEngine();
        foreach (var group in groups)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                string stamp = Stamp(group.Path, group.Backup);
                if (_states.TryGetValue(group.Path, out var cached) && cached.Stamp == stamp) reused++;
                else
                {
                    _states.TryRemove(group.Path, out _);
                    var current = new FileInfo(group.Path);
                    var original = new FileInfo(group.Backup);
                    bool same = current.Length == original.Length && SkillMovieEngine.Hash(group.Path) == SkillMovieEngine.Hash(group.Backup);
                    cached = new BundleState { Stamp = stamp, BackupPath = group.Backup, SameFile = same };
                    if (!same && group.Assets.Any(asset => asset.Kind == ResourceKinds.Texture))
                    {
                        cached.Signatures = engine.ReadTextureSignatures(group.Path);
                        var baseline = engine.ReadTextureSignatures(group.Backup).GroupBy(texture => texture.Name, StringComparer.Ordinal)
                            .ToDictionary(items => items.Key, items => items.ToList(), StringComparer.Ordinal);
                        foreach (var textures in cached.Signatures.GroupBy(texture => texture.Name, StringComparer.Ordinal))
                        {
                            if (textures.Count() != 1 || !baseline.TryGetValue(textures.Key, out var originals)) continue;
                            var texture = textures.First();
                            var match = originals.FirstOrDefault(item => item.PathId == texture.PathId) ?? (originals.Count == 1 ? originals[0] : null);
                            if (match != null) cached.Textures[texture.Name] = match.Hash == texture.Hash ? ModificationState.Original : ModificationState.Modified;
                        }
                    }
                    foreach (var movie in group.Assets.Where(asset => asset.Kind == ResourceKinds.SkillMovie).DistinctBy(asset => asset.Name))
                    {
                        if (same) break;
                        var active = SkillMovieEngine.Read(group.Path, movie.Name);
                        var baseline = SkillMovieEngine.Read(group.Backup, movie.Name);
                        cached.Textures[movie.Name] = active.Loop == baseline.Loop && active.Bytes.AsSpan().SequenceEqual(baseline.Bytes)
                            ? ModificationState.Original : ModificationState.Modified;
                        if (cached.Textures[movie.Name] == ModificationState.Modified)
                            cached.Signatures.Add(new(movie.Name, active.PathId, movie.Width, movie.Height, "USM", ""));
                    }
                    if (Stamp(group.Path, group.Backup) != stamp) throw new IOException("资源在核验期间变化。");
                    _states[group.Path] = cached;
                    verified++;
                }
                if (!cached.SameFile)
                {
                    foreach (var texture in cached.Signatures.Where(texture => cached.Textures.GetValueOrDefault(texture.Name) == ModificationState.Modified))
                    {
                        var asset = group.Assets.FirstOrDefault(asset => asset.Name == texture.Name);
                        if (asset == null) continue;
                        recovered.Add(new ModEntry { Bundle = group.Assets.First().Bundle, PathId = texture.PathId, TextureName = texture.Name,
                            Width = texture.Width, Height = texture.Height, Kind = asset.Kind, Label = asset.CategoryLabel ?? ResourceCategories.Label(asset.Kind,
                                ResourceCategories.Categorize(asset.Kind, texture.Name, texture.Width, texture.Height)) });
                    }
                }
            }
            catch (Exception ex) when (!token.IsCancellationRequested && ex is not OperationCanceledException)
            { failed++; errors[group.Path] = ex.Message; _states.TryRemove(group.Path, out _); }
            progress?.Invoke(++done, groups.Count);
        }
        token.ThrowIfCancellationRequested();
        string cacheNotice = null;
        string temporary = _cachePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_cachePath)!);
            File.WriteAllText(temporary, JsonSerializer.Serialize(_states));
            File.Move(temporary, _cachePath, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { cacheNotice = "核验缓存未保存：" + ex.Message; }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return new(recovered, verified, reused, failed, cacheNotice) { Errors = errors };
    }

    public static string Label(ModificationState state) => state switch
    {
        ModificationState.Modified => "已修改（与原始备份不同）",
        ModificationState.Original => "与原始备份一致",
        ModificationState.Recorded => "有修改记录，尚未核验",
        _ => "未核验（尚无可靠的比对结果）"
    };

    public static string ShortLabel(ModificationState state) => state switch
    {
        ModificationState.Modified => "已修改", ModificationState.Original => "备份一致",
        ModificationState.Recorded => "有记录", _ => "未核验"
    };
}
