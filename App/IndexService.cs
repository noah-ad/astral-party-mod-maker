using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AssetsTools.NET;
using AssetsTools.NET.Extra;

namespace JixModMaker;

/// <summary>索引里的一张贴图。</summary>
public class TexIndexEntry
{
    public string Bundle { get; set; }
    public long PathId { get; set; }
    public string Name { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}

/// <summary>一个游戏目录的立绘索引 (可缓存)。</summary>
public class GameIndex
{
    public string GameDir { get; set; }
    public string BuiltAt { get; set; }
    public bool HeroOnly { get; set; } = true;
    public List<TexIndexEntry> Items { get; set; } = new();
}

/// <summary>
/// 扫描游戏目录, 提取角色立绘/手牌贴图, 建立可缓存的索引。
/// 角色立绘分散在各自独立 bundle 里, 必须全量扫一遍才能按角色聚合。
/// </summary>
public class IndexService
{
    private readonly string _tpk;
    public IndexService() => _tpk = Path.Combine(AppContext.BaseDirectory, "classdata.tpk");

    /// <summary>扫描 gameDir, 返回索引。heroOnly=true 只收角色立绘+手牌。</summary>
    public GameIndex Build(string gameDir, Action<int, int> progress = null, bool heroOnly = true)
    {
        var idx = new GameIndex { GameDir = gameDir, BuiltAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), HeroOnly = heroOnly };
        var bundles = Directory.GetFiles(gameDir, "*.bundle");

        var am = new AssetsManager();
        if (File.Exists(_tpk)) am.LoadClassPackage(_tpk);
        bool dbLoaded = false;
        int done = 0;

        foreach (var b in bundles)
        {
            try
            {
                var bun = am.LoadBundleFile(b);
                var afile = am.LoadAssetsFileFromBundle(bun, 0, false);
                if (!dbLoaded && am.ClassPackage != null)
                { am.LoadClassDatabaseFromPackage(afile.file.Metadata.UnityVersion); dbLoaded = true; }

                string bundleName = Path.GetFileName(b);
                foreach (var info in afile.file.GetAssetsOfType(AssetClassID.Texture2D))
                {
                    var bf = am.GetBaseField(afile, info);
                    string nm = bf["m_Name"].AsString;
                    if (heroOnly)
                    {
                        var c = NameParser.Parse(nm);
                        if (!c.IsHero && !c.IsHandCard) continue;
                    }
                    idx.Items.Add(new TexIndexEntry
                    {
                        Bundle = bundleName, PathId = info.PathId, Name = nm,
                        Width = bf["m_Width"].AsInt, Height = bf["m_Height"].AsInt
                    });
                }
            }
            catch { }
            finally { try { am.UnloadAll(false); } catch { } }

            progress?.Invoke(++done, bundles.Length);
        }
        try { am.UnloadAll(); } catch { }
        return idx;
    }

    // ---------- 缓存 ----------

    public static string CachePath(string gameDir, bool heroOnly = true)
    {
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JixModMaker");
        Directory.CreateDirectory(dir);
        string h = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(gameDir.ToLowerInvariant())))[..8];
        return Path.Combine(dir, $"index_{(heroOnly ? "hero" : "full")}_{h}.json");
    }

    public void Save(GameIndex idx) => File.WriteAllText(CachePath(idx.GameDir, idx.HeroOnly), JsonSerializer.Serialize(idx));

    public GameIndex Load(string gameDir, bool heroOnly = true)
    {
        var p = CachePath(gameDir, heroOnly);
        if (!File.Exists(p)) return null;
        try { return JsonSerializer.Deserialize<GameIndex>(File.ReadAllText(p)); }
        catch { return null; }
    }
}
