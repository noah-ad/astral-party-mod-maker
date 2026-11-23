using System.IO.Compression;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Globalization;

namespace JixModMaker;

/// <summary>图包/工作区里的一条替换记录。</summary>
public class ModEntry
{
    public string Bundle { get; set; }       // bundle 文件名 (v1 定位用; v2 可空)
    public long PathId { get; set; }          // Texture2D 的 PathID (v1)
    public string TextureName { get; set; }   // 贴图名 (v2 主键, 跨版本稳定)
    public string Kind { get; set; } = ResourceKinds.Texture;
    public int Width { get; set; }
    public int Height { get; set; }
    public string Label { get; set; }          // 备注/角色名 (可选)
    public string Image { get; set; }          // 包内图片路径 (仅图包用)
    public DateTimeOffset? ModifiedAt { get; set; }
}

/// <summary>迁移/导出时一张待打包的图 (内存中)。</summary>
public class NamedImage
{
    public string TextureName;
    public int Width, Height;
    public byte[] Png;
    public string Label;
}

/// <summary>图包 / 工作区清单。</summary>
public class ModManifest
{
    public int Format { get; set; } = 1;
    public string Name { get; set; } = "";
    public string Author { get; set; } = "";
    public string Game { get; set; } = "AstralParty";
    public string Platform { get; set; } = "";    // PC / Android (v2, 仅备注)
    public string Category { get; set; } = "";    // 角色 / 卡图 (v2, 仅备注)
    public string CreatedAt { get; set; } = "";
    public List<ModEntry> Entries { get; set; } = new();
    [JsonIgnore] public string LoadNotice { get; set; }
}

/// <summary>
/// 图包 (.jxpack, 本质 zip) 的导入导出 + 工作区清单管理。
/// 图包内只含改过的图: manifest.json + images/*.png。
/// </summary>
public static class PackService
{
    public const string PackExt = ".jxpack";
    private const string WorkspaceFile = ".jixmod.json";

    private static readonly JsonSerializerOptions J = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,  // 中文不转义
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        Converters = { new OptionalTimestampConverter() }
    };

    // ---------- 工作区清单 (记录某文件夹下被改过的贴图) ----------

    public static string WorkspacePath(string folder) => Path.Combine(folder, WorkspaceFile);

    public static ModManifest LoadWorkspace(string folder)
    {
        var p = WorkspacePath(folder);
        string error = null;
        foreach (var candidate in new[] { p, p + ".bak" })
        {
            if (!File.Exists(candidate)) continue;
            try
            {
                var manifest = ReadWorkspace(candidate);
                if (candidate != p) manifest.LoadNotice = "已读取上次保存的修改记录；当前记录文件缺失或无法读取。";
                return manifest;
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { error = ex.Message; }
        }
        return new ModManifest { LoadNotice = error == null ? null : "修改记录无法读取，原文件已保留；将尝试从原始备份识别。" };
    }

    public static void SaveWorkspace(string folder, ModManifest m)
    {
        if (m?.Entries == null || m.Entries.Any(entry => entry == null)) throw new JsonException("修改记录无效，未覆盖原文件。");
        string path = WorkspacePath(folder);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(m, J));
            if (!File.Exists(path)) File.Move(temporary, path);
            else
            {
                bool valid;
                try { ReadWorkspace(path); valid = true; }
                catch (JsonException) { valid = false; }
                if (valid) File.Replace(temporary, path, path + ".bak", true);
                else
                {
                    File.Copy(path, path + ".unreadable-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff") + "-" + Guid.NewGuid().ToString("N") + ".json");
                    File.Move(temporary, path, true);
                }
            }
            m.LoadNotice = null;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static ModManifest ReadWorkspace(string path)
    {
        string content = File.ReadAllText(path);
        using var document = JsonDocument.Parse(content);
        if (document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.EnumerateObject().Any(property => property.Name.Equals("Entries", StringComparison.OrdinalIgnoreCase)
                && property.Value.ValueKind == JsonValueKind.Array)) throw new JsonException("记录文件缺少有效的条目列表。");
        var manifest = JsonSerializer.Deserialize<ModManifest>(content, J) ?? throw new JsonException("记录文件为空。");
        if (manifest.Entries == null || manifest.Entries.Any(entry => entry == null)) throw new JsonException("记录中存在无效条目。");
        foreach (var entry in manifest.Entries) entry.Kind ??= ResourceKinds.Texture;
        return manifest;
    }

    private sealed class OptionalTimestampConverter : JsonConverter<DateTimeOffset?>
    {
        public override DateTimeOffset? Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null) return null;
            if (reader.TokenType == JsonTokenType.String && string.IsNullOrWhiteSpace(reader.GetString())) return null;
            if (reader.TokenType == JsonTokenType.String && DateTimeOffset.TryParse(reader.GetString(), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal, out var value)) return value;
            throw new JsonException("无效的修改时间。");
        }
        public override void Write(Utf8JsonWriter writer, DateTimeOffset? value, JsonSerializerOptions options)
        {
            if (value == null) writer.WriteNullValue(); else writer.WriteStringValue(value.Value);
        }
    }

    public static string BundleKey(string bundle) => (bundle ?? "").Replace('\\', '/');

    private static bool SameEntry(ModEntry entry, string bundle, long pathId, string name)
        => string.Equals(BundleKey(entry.Bundle), BundleKey(bundle), StringComparison.OrdinalIgnoreCase)
            && (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(entry.TextureName)
                ? string.Equals(entry.TextureName, name, StringComparison.Ordinal)
                : pathId != 0 && entry.PathId == pathId);

    public static int MergeRecovered(ModManifest workspace, IEnumerable<ModEntry> recovered)
    {
        int changes = 0;
        foreach (var entry in recovered)
        {
            var previous = workspace.Entries.Where(old => SameEntry(old, entry.Bundle, entry.PathId, entry.TextureName) && old.Kind == entry.Kind)
                .OrderByDescending(old => old.ModifiedAt).FirstOrDefault();
            if (previous == null) { workspace.Entries.Add(entry); changes++; continue; }
            if (previous.PathId == entry.PathId && previous.Width == entry.Width && previous.Height == entry.Height && previous.TextureName == entry.TextureName) continue;
            previous.PathId = entry.PathId;
            previous.TextureName = entry.TextureName;
            previous.Width = entry.Width;
            previous.Height = entry.Height;
            changes++;
        }
        return changes;
    }

    public static void Upsert(ModManifest ws, ModEntry e)
    {
        e.ModifiedAt = DateTimeOffset.UtcNow;
        ws.Entries.RemoveAll(x => x.Kind == e.Kind && SameEntry(x, e.Bundle, e.PathId, e.TextureName));
        ws.Entries.Add(e);
    }

    public static bool Contains(ModManifest ws, string bundleName, long pathId)
        => Contains(ws, bundleName, pathId, null);

    public static bool Contains(ModManifest ws, string bundleName, long pathId, string textureName)
    {
        if (ws?.Entries == null) return false;
        return ws.Entries.Any(entry => SameEntry(entry, bundleName, pathId, textureName));
    }

    // ---------- 导出图包 ----------

    /// <summary>把 folder 里所有改过的贴图 (来自 workspace) 打包成 .jxpack。返回打包条目数。</summary>
    public static int Export(string folder, ModManifest workspace, string outPath,
                             string name, string author, ModEngine engine)
    {
        var outManifest = new ModManifest
        {
            Name = string.IsNullOrWhiteSpace(name) ? Path.GetFileNameWithoutExtension(outPath) : name,
            Author = author ?? "",
            Game = "AstralParty",
            CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
        };

        if (File.Exists(outPath)) File.Delete(outPath);
        using var zip = ZipFile.Open(outPath, ZipArchiveMode.Create);

        int i = 0;
        foreach (var e in workspace.Entries)
        {
            string bundlePath = Path.Combine(folder, e.Bundle);
            if (!File.Exists(bundlePath)) continue;

            byte[] png;
            try { png = engine.DecodePng(bundlePath, e.PathId, 0); }  // 全尺寸当前图
            catch { continue; }

            string imgName = $"images/{i}.png";
            var imgEntry = zip.CreateEntry(imgName, CompressionLevel.Optimal);
            using (var s = imgEntry.Open()) s.Write(png, 0, png.Length);

            outManifest.Entries.Add(new ModEntry
            {
                Bundle = e.Bundle, PathId = e.PathId, TextureName = e.TextureName,
                Width = e.Width, Height = e.Height, Label = e.Label, Image = imgName
            });
            i++;
        }

        var manEntry = zip.CreateEntry("manifest.json", CompressionLevel.Optimal);
        using (var s = manEntry.Open())
        using (var w = new StreamWriter(s))
            w.Write(JsonSerializer.Serialize(outManifest, J));

        return i;
    }

    // ---------- 导入图包 ----------

    public class ImportResult
    {
        public int Applied;
        public int Missing;
        public int Targets;
        public int VariantTargets;
        public int Failed;
        public string PackName;
        public string Author;
        public List<(string Name, string Error)> Issues { get; } = new();
        public HashSet<string> WrittenBundles { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>读图包格式版本 (1 或 2); 读不到返回 1。</summary>
    public static int PeekFormat(string packPath)
    {
        try
        {
            using var zip = ZipFile.OpenRead(packPath);
            var me = zip.GetEntry("manifest.json");
            if (me == null) return 1;
            using var r = new StreamReader(me.Open());
            var m = JsonSerializer.Deserialize<ModManifest>(r.ReadToEnd());
            if (m == null) return 1;
            if (m.Format >= 2) return m.Format;
            return m.Entries.Any(entry => !string.IsNullOrWhiteSpace(entry.TextureName)) ? 2 : 1;
        }
        catch { return 1; }
    }

    // ========== v2: 按贴图名定位 (跨版本/跨平台通用) ==========

    /// <summary>把内存中的图按贴图名打包成 v2 图包。</summary>
    public static void ExportV2(IEnumerable<NamedImage> images, string outPath,
                                string name, string author, string platform, string category)
    {
        var man = new ModManifest
        {
            Format = 2, Name = name, Author = author ?? "", Game = "AstralParty",
            Platform = platform, Category = category,
            CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
        };

        Directory.CreateDirectory(Path.GetDirectoryName(outPath));
        if (File.Exists(outPath)) File.Delete(outPath);
        using var zip = ZipFile.Open(outPath, ZipArchiveMode.Create);

        int i = 0;
        foreach (var img in images)
        {
            string imgName = $"images/{i}.png";
            var e = zip.CreateEntry(imgName, CompressionLevel.Optimal);
            using (var s = e.Open()) s.Write(img.Png, 0, img.Png.Length);
            man.Entries.Add(new ModEntry
            {
                TextureName = img.TextureName, Width = img.Width, Height = img.Height,
                Label = img.Label, Image = imgName
            });
            i++;
        }

        var me = zip.CreateEntry("manifest.json", CompressionLevel.Optimal);
        using (var s = me.Open())
        using (var w = new StreamWriter(s))
            w.Write(JsonSerializer.Serialize(man, J));
    }

    public static ImportResult ImportV2(string packPath, GameIndex gameIndex, string gameDir,
        ModEngine engine, string backupDir, ModManifest workspace, Action<int, int> progress = null)
    {
        ArgumentNullException.ThrowIfNull(gameIndex);
        if (!RuntimeBundleLocator.SamePath(gameDir, gameIndex.GameDir))
            throw new InvalidOperationException("图包导入目标与当前索引不一致。");

        using var zip = ZipFile.OpenRead(packPath);
        var manifest = zip.GetEntry("manifest.json") ?? throw new InvalidDataException("图包损坏: 缺少 manifest.json");
        using var input = manifest.Open();
        var pack = JsonSerializer.Deserialize<ModManifest>(input, J);
        if (pack?.Entries == null || pack.Entries.Any(entry => entry == null))
            throw new InvalidDataException("图包的条目列表无效。");

        var res = new ImportResult { PackName = pack.Name, Author = pack.Author };
        var byName = BuildTextureTargetMap(gameIndex);
        var byBundle = gameIndex.Items.Where(row => row.Kind == ResourceKinds.Texture)
            .ToLookup(row => BundleKey(row.Bundle), StringComparer.OrdinalIgnoreCase);
        var roots = new[] { gameDir, gameIndex.IncludeHotCache ? IndexService.UserCacheFor(gameDir) : null,
            gameIndex.IncludeHotCache ? RuntimeBundleLocator.GameDataCache(gameDir) : null }
            .Where(Directory.Exists).Select(Path.GetFullPath).ToArray();
        var explicitNames = pack.Entries.Select(entry => entry.TextureName).Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool matchVariants = GameLocator.EditionOf(gameDir) == GameEdition.International;
        var written = new HashSet<(string Bundle, long Id)>();
        progress?.Invoke(0, pack.Entries.Count);
        for (int i = 0; i < pack.Entries.Count; i++)
        {
            var entry = pack.Entries[i];
            string name = entry.TextureName?.Trim(), label = string.IsNullOrEmpty(name) ? entry.Bundle : name;
            try
            {
                var targets = new List<(TexIndexEntry Asset, bool Variant)>();
                if (!string.IsNullOrEmpty(name))
                {
                    if (byName.TryGetValue(name, out var exact)) targets.AddRange(exact.Select(asset => (asset, false)));
                    if (matchVariants && Counterpart(name) is { } alias && !explicitNames.Contains(alias)
                        && byName.TryGetValue(alias, out var other))
                        targets.AddRange(other.Select(asset => (asset, true)));
                }
                else if (!string.IsNullOrWhiteSpace(entry.Bundle) && entry.PathId != 0)
                    targets.AddRange(byBundle[BundleKey(entry.Bundle)].Where(asset => asset.PathId == entry.PathId)
                        .Select(asset => (asset, false)));

                if (targets.Count == 0) { res.Missing++; res.Issues.Add((label, null)); continue; }
                var image = FindImageEntry(zip, entry.Image) ?? throw new InvalidDataException("图包缺少图片：" + entry.Image);
                byte[] png;
                using (var source = image.Open())
                using (var buffer = new MemoryStream()) { source.CopyTo(buffer); png = buffer.ToArray(); }
                bool applied = false;
                foreach (var (asset, variant) in targets)
                {
                    try
                    {
                        string target = Path.GetFullPath(string.IsNullOrWhiteSpace(asset.BundlePath)
                            ? Path.Combine(gameDir, asset.Bundle) : asset.BundlePath);
                        if (!roots.Any(root => IsInside(root, target)) || !File.Exists(target))
                            throw new FileNotFoundException("当前索引中的资源已不存在，请刷新索引。", target);
                        long id = asset.PathId;
                        if (id == 0)
                            id = engine.FindTextureByName(target, asset.Name)?.PathId
                                ?? throw new InvalidDataException("资源包里找不到贴图：" + asset.Name);
                        var key = (target.ToUpperInvariant(), id);
                        if (written.Contains(key)) continue;

                        engine.ReplaceInPlaceFromBytes(target, id, png, backupDir, asset.Bundle, asset.Name);
                        written.Add(key);
                        Upsert(workspace, new ModEntry
                        {
                            Bundle = asset.Bundle, PathId = id, TextureName = asset.Name,
                            Width = entry.Width > 0 ? entry.Width : asset.Width,
                            Height = entry.Height > 0 ? entry.Height : asset.Height,
                            Label = entry.Label
                        });
                        res.WrittenBundles.Add(target);
                        res.Targets++;
                        if (variant) res.VariantTargets++;
                        applied = true;
                    }
                    catch (Exception ex) when (ex is not OutOfMemoryException)
                    { res.Failed++; res.Issues.Add((asset.Name, ex.Message)); }
                }
                if (applied) res.Applied++;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            { res.Failed++; res.Issues.Add((label, ex.Message)); }
            finally { progress?.Invoke(i + 1, pack.Entries.Count); }
        }
        return res;
    }

    private static bool IsInside(string root, string file)
    {
        string relative = Path.GetRelativePath(root, file);
        return !Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar);
    }

    private static string Counterpart(string name)
    {
        if (!name.StartsWith("UT_", StringComparison.OrdinalIgnoreCase)) return null;
        return name.EndsWith("_sfw", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name + "_sfw";
    }

    private static ZipArchiveEntry FindImageEntry(ZipArchive zip, string imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath)) return null;
        string normalized = imagePath.Replace('\\', '/');
        return zip.GetEntry(imagePath)
            ?? zip.GetEntry(normalized)
            ?? zip.Entries.FirstOrDefault(entry =>
                string.Equals(entry.FullName, imagePath, StringComparison.OrdinalIgnoreCase)
                || string.Equals(entry.FullName, normalized, StringComparison.OrdinalIgnoreCase));
    }

    private static Dictionary<string, List<TexIndexEntry>> BuildTextureTargetMap(GameIndex gameIndex)
    {
        var byName = new Dictionary<string, List<TexIndexEntry>>(StringComparer.OrdinalIgnoreCase);
        if (gameIndex == null) return byName;

        if (gameIndex.Lightweight)
        {
            foreach (var pair in gameIndex.TextureNamesByBundle)
            {
                string bundlePath = "";
                string source = "";
                gameIndex.BundlePathsByName?.TryGetValue(pair.Key, out bundlePath);
                gameIndex.BundleSourcesByName?.TryGetValue(pair.Key, out source);
                foreach (var name in pair.Value ?? new List<string>())
                    AddTarget(byName, new TexIndexEntry
                    {
                        Bundle = pair.Key,
                        BundlePath = bundlePath,
                        Source = source,
                        PathId = 0,
                        Name = name,
                        Kind = ResourceKinds.Texture
                    });
            }
            return byName;
        }

        foreach (var target in gameIndex.Items.Where(i => i.Kind == ResourceKinds.Texture))
            AddTarget(byName, target);
        return byName;
    }

    private static void AddTarget(Dictionary<string, List<TexIndexEntry>> byName, TexIndexEntry target)
    {
        if (string.IsNullOrWhiteSpace(target.Name)) return;
        if (!byName.TryGetValue(target.Name, out var list))
        {
            list = new List<TexIndexEntry>();
            byName[target.Name] = list;
        }
        list.Add(target);
    }
}
