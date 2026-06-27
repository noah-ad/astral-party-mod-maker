using System.IO.Compression;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace JixModMaker;

/// <summary>图包/工作区里的一条替换记录。</summary>
public class ModEntry
{
    public string Bundle { get; set; }       // bundle 文件名 (v1 定位用; v2 可空)
    public long PathId { get; set; }          // Texture2D 的 PathID (v1)
    public string TextureName { get; set; }   // 贴图名 (v2 主键, 跨版本稳定)
    public int Width { get; set; }
    public int Height { get; set; }
    public string Label { get; set; }          // 备注/角色名 (可选)
    public string Image { get; set; }          // 包内图片路径 (仅图包用)
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
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping  // 中文不转义
    };

    // ---------- 工作区清单 (记录某文件夹下被改过的贴图) ----------

    public static string WorkspacePath(string folder) => Path.Combine(folder, WorkspaceFile);

    public static ModManifest LoadWorkspace(string folder)
    {
        var p = WorkspacePath(folder);
        if (File.Exists(p))
        {
            try { return JsonSerializer.Deserialize<ModManifest>(File.ReadAllText(p)) ?? new(); }
            catch { }
        }
        return new ModManifest();
    }

    public static void SaveWorkspace(string folder, ModManifest m)
        => File.WriteAllText(WorkspacePath(folder), JsonSerializer.Serialize(m, J));

    public static void Upsert(ModManifest ws, ModEntry e)
    {
        ws.Entries.RemoveAll(x => x.Bundle == e.Bundle && x.PathId == e.PathId);
        ws.Entries.Add(e);
    }

    public static bool Contains(ModManifest ws, string bundleName, long pathId)
        => ws.Entries.Any(e => e.Bundle == bundleName && e.PathId == pathId);

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

    public class ImportResult { public int Applied; public int Missing; public string PackName; public string Author; }

    /// <summary>把图包应用到 targetFolder。按 bundle+pathId 精确定位写回, 并更新工作区清单。</summary>
    public static ImportResult Import(string packPath, string targetFolder, ModEngine engine,
                                      string backupDir, ModManifest workspace)
    {
        using var zip = ZipFile.OpenRead(packPath);
        var manEntry = zip.GetEntry("manifest.json")
            ?? throw new Exception("图包损坏: 缺少 manifest.json");

        ModManifest m;
        using (var s = manEntry.Open())
        using (var r = new StreamReader(s))
            m = JsonSerializer.Deserialize<ModManifest>(r.ReadToEnd()) ?? new();

        var res = new ImportResult { PackName = m.Name, Author = m.Author };
        foreach (var e in m.Entries)
        {
            string bundlePath = Path.Combine(targetFolder, e.Bundle);
            var imgEntry = e.Image != null ? zip.GetEntry(e.Image) : null;
            if (!File.Exists(bundlePath) || imgEntry == null) { res.Missing++; continue; }

            byte[] png;
            using (var s = imgEntry.Open())
            using (var ms = new MemoryStream()) { s.CopyTo(ms); png = ms.ToArray(); }

            try
            {
                engine.ReplaceInPlaceFromBytes(bundlePath, e.PathId, png, backupDir);
                Upsert(workspace, new ModEntry
                {
                    Bundle = e.Bundle, PathId = e.PathId, TextureName = e.TextureName,
                    Width = e.Width, Height = e.Height, Label = e.Label
                });
                res.Applied++;
            }
            catch { res.Missing++; }
        }
        return res;
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
            return m?.Format ?? 1;
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

    /// <summary>
    /// 导入 v2 图包: 按贴图名在 gameIndex 里实时匹配当前游戏的 bundle+pathId 并写回。
    /// 同名贴图(多 bundle)全部写回。
    /// </summary>
    public static ImportResult ImportV2(string packPath, GameIndex gameIndex, string gameDir,
                                        ModEngine engine, string backupDir, ModManifest workspace)
    {
        var byName = gameIndex.Items
            .GroupBy(i => i.Name)
            .ToDictionary(g => g.Key, g => g.ToList());

        using var zip = ZipFile.OpenRead(packPath);
        var manEntry = zip.GetEntry("manifest.json") ?? throw new Exception("图包损坏: 缺少 manifest.json");
        ModManifest m;
        using (var s = manEntry.Open())
        using (var r = new StreamReader(s))
            m = JsonSerializer.Deserialize<ModManifest>(r.ReadToEnd()) ?? new();

        var res = new ImportResult { PackName = m.Name, Author = m.Author };
        foreach (var e in m.Entries)
        {
            var imgEntry = e.Image != null ? zip.GetEntry(e.Image) : null;
            if (imgEntry == null || !byName.TryGetValue(e.TextureName, out var targets))
            { res.Missing++; continue; }

            byte[] png;
            using (var s = imgEntry.Open())
            using (var ms = new MemoryStream()) { s.CopyTo(ms); png = ms.ToArray(); }

            bool any = false;
            foreach (var t in targets)
            {
                string bundlePath = Path.Combine(gameDir, t.Bundle);
                if (!File.Exists(bundlePath)) continue;
                try
                {
                    engine.ReplaceInPlaceFromBytes(bundlePath, t.PathId, png, backupDir);
                    Upsert(workspace, new ModEntry
                    {
                        Bundle = t.Bundle, PathId = t.PathId, TextureName = e.TextureName,
                        Width = e.Width, Height = e.Height, Label = e.Label
                    });
                    any = true;
                }
                catch { }
            }
            if (any) res.Applied++; else res.Missing++;
        }
        return res;
    }
}
