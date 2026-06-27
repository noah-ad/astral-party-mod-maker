namespace JixModMaker;

/// <summary>
/// 把旧 mod (按旧 bundle hash 命名、版本更新后失效) 迁移为最新 v2 图包。
/// 原理: 旧文件里贴图的 m_Name 稳定不变, 解出当前图 + 按平台/类型分类打包。
/// 平台: .bundle=PC, __data=安卓。类型: UT_Hero_*=角色, 其余=卡图。
/// </summary>
public class MigrationService
{
    private readonly ModEngine _engine = new();

    public class Report
    {
        public int Files, Textures, Unreadable;
        public List<string> Packs = new();
        public Dictionary<string, int> GroupCounts = new();
    }

    public Report Migrate(string oldRoot, string outDir, Action<int, int, string> progress = null)
    {
        var files = new List<string>();
        files.AddRange(Directory.GetFiles(oldRoot, "*.bundle", SearchOption.AllDirectories));
        files.AddRange(Directory.GetFiles(oldRoot, "__data", SearchOption.AllDirectories));

        // (平台, 类型) -> 贴图名 -> NamedImage  (同组内按名字去重)
        var groups = new Dictionary<(string plat, string cat), Dictionary<string, NamedImage>>();
        var rep = new Report { Files = files.Count };

        int done = 0;
        foreach (var f in files)
        {
            string plat = f.EndsWith(".bundle", StringComparison.OrdinalIgnoreCase) ? "PC" : "安卓";
            List<TexRef> texs;
            try { texs = _engine.ListTextures(f); }
            catch { rep.Unreadable++; progress?.Invoke(++done, files.Count, ""); continue; }

            foreach (var t in texs)
            {
                byte[] png;
                try { png = _engine.DecodePng(f, t.PathId, 0); }
                catch { continue; }

                string cat = NameParser.Parse(t.Name).IsHero ? "角色立绘" : "卡图";
                var key = (plat, cat);
                if (!groups.TryGetValue(key, out var dict)) groups[key] = dict = new();
                dict[t.Name] = new NamedImage { TextureName = t.Name, Width = t.Width, Height = t.Height, Png = png };
                rep.Textures++;
            }
            progress?.Invoke(++done, files.Count, Path.GetFileName(f));
        }

        // 导出每组为一个 v2 图包: outDir/{平台}端/{类型}.jxpack
        Directory.CreateDirectory(outDir);
        foreach (var ((plat, cat), dict) in groups)
        {
            string dir = Path.Combine(outDir, plat + "端");
            Directory.CreateDirectory(dir);
            string outPath = Path.Combine(dir, cat + PackService.PackExt);
            PackService.ExportV2(dict.Values, outPath, $"{plat}端{cat}", "迁移", plat, cat);
            rep.Packs.Add(outPath);
            rep.GroupCounts[$"{plat}/{cat}"] = dict.Count;
        }
        return rep;
    }
}
