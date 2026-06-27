using AssetsTools.NET;
using AssetsTools.NET.Extra;
using AssetsTools.NET.Texture;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Image = SixLabors.ImageSharp.Image;
using Size = SixLabors.ImageSharp.Size;

namespace JixModMaker;

/// <summary>一张可替换贴图的引用信息。</summary>
public class TexRef
{
    public string BundlePath;
    public long PathId;
    public string Name;
    public int Width;
    public int Height;
    public string Format;
    public bool Modded;      // 是否已被本工具改成 RGBA32
    public string Display;   // 显示名 (角色模式用 "半身 01" 等友好标签; null 则用 Name)
    public override string ToString() => $"{Name} ({Width}x{Height})";
}

/// <summary>
/// mod 引擎: 封装"解包 bundle 贴图 / 导出预览 / 写回替换"。
/// 逻辑已通过 round-trip 验证 (AssetsTools.NET 2.x + RGBA32 写回)。
/// </summary>
public class ModEngine
{
    private readonly string _tpkPath;

    public ModEngine()
    {
        _tpkPath = Path.Combine(AppContext.BaseDirectory, "classdata.tpk");
    }

    private AssetsManager NewManager()
    {
        var am = new AssetsManager();
        if (File.Exists(_tpkPath)) am.LoadClassPackage(_tpkPath);
        return am;
    }

    /// <summary>列出一个 bundle 里所有 Texture2D。</summary>
    public List<TexRef> ListTextures(string bundlePath)
    {
        var result = new List<TexRef>();
        var am = NewManager();
        try
        {
            var bun = am.LoadBundleFile(bundlePath);
            var afile = am.LoadAssetsFileFromBundle(bun, 0, false);
            if (am.ClassDatabase == null && am.ClassPackage != null)
                am.LoadClassDatabaseFromPackage(afile.file.Metadata.UnityVersion);

            foreach (var info in afile.file.GetAssetsOfType(AssetClassID.Texture2D))
            {
                var bf = am.GetBaseField(afile, info);
                int fmt = bf["m_TextureFormat"].AsInt;
                result.Add(new TexRef
                {
                    BundlePath = bundlePath,
                    PathId = info.PathId,
                    Name = bf["m_Name"].AsString,
                    Width = bf["m_Width"].AsInt,
                    Height = bf["m_Height"].AsInt,
                    Format = ((TextureFormat)fmt).ToString(),
                    Modded = fmt == (int)TextureFormat.RGBA32
                });
            }
        }
        finally { am.UnloadAll(); }
        return result;
    }

    /// <summary>把指定贴图解码成 PNG 字节 (可选缩放到 maxSize 边长, 0=原尺寸)。</summary>
    public byte[] DecodePng(string bundlePath, long pathId, int maxSize = 0)
    {
        var am = NewManager();
        try
        {
            var bun = am.LoadBundleFile(bundlePath);
            var afile = am.LoadAssetsFileFromBundle(bun, 0, false);
            if (am.ClassDatabase == null && am.ClassPackage != null)
                am.LoadClassDatabaseFromPackage(afile.file.Metadata.UnityVersion);

            var info = afile.file.GetAssetsOfType(AssetClassID.Texture2D)
                .First(i => i.PathId == pathId);
            var bf = am.GetBaseField(afile, info);
            int w = bf["m_Width"].AsInt, h = bf["m_Height"].AsInt;

            var tf = TextureFile.ReadTextureFile(bf);
            byte[] bgra = tf.GetTextureData(afile);

            using var img = Image.LoadPixelData<Bgra32>(bgra.AsSpan(0, w * h * 4), w, h);
            img.Mutate(c => c.Flip(FlipMode.Vertical));   // Unity 原点左下
            if (maxSize > 0 && (w > maxSize || h > maxSize))
                img.Mutate(c => c.Resize(new ResizeOptions
                {
                    Size = new Size(maxSize, maxSize),
                    Mode = ResizeMode.Max
                }));

            using var ms = new MemoryStream();
            img.SaveAsPng(ms);
            return ms.ToArray();
        }
        finally { am.UnloadAll(); }
    }

    /// <summary>用图片文件替换指定贴图, 原地写回 bundle (先备份)。</summary>
    public void ReplaceInPlace(string bundlePath, long pathId, string newImagePath, string backupDir)
    {
        using var img = Image.Load<Rgba32>(newImagePath);
        ReplaceCore(bundlePath, pathId, img, backupDir);
    }

    /// <summary>用图片字节 (如图包内的 PNG) 替换指定贴图, 原地写回 bundle (先备份)。</summary>
    public void ReplaceInPlaceFromBytes(string bundlePath, long pathId, byte[] pngBytes, string backupDir)
    {
        using var img = Image.Load<Rgba32>(pngBytes);
        ReplaceCore(bundlePath, pathId, img, backupDir);
    }

    /// <summary>
    /// 核心: 把图写回 Texture2D (RGBA32 未压缩, 库无 BC7 编码器)。允许尺寸不同。
    /// </summary>
    private void ReplaceCore(string bundlePath, long pathId, Image<Rgba32> img, string backupDir)
    {
        // 1. 首次修改该 bundle 时自动备份原始文件
        if (!string.IsNullOrEmpty(backupDir))
        {
            Directory.CreateDirectory(backupDir);
            string backup = Path.Combine(backupDir, Path.GetFileName(bundlePath));
            if (!File.Exists(backup)) File.Copy(bundlePath, backup);
        }

        // 2. 图 -> RGBA32 bottom-up
        img.Mutate(c => c.Flip(FlipMode.Vertical));
        int nw = img.Width, nh = img.Height;
        byte[] rgba = new byte[nw * nh * 4];
        img.CopyPixelDataTo(rgba);

        // 3. 打开 bundle, 改字段
        string tmp = bundlePath + ".tmp";
        var am = NewManager();
        try
        {
            var bun = am.LoadBundleFile(bundlePath);
            var afile = am.LoadAssetsFileFromBundle(bun, 0, false);
            if (am.ClassDatabase == null && am.ClassPackage != null)
                am.LoadClassDatabaseFromPackage(afile.file.Metadata.UnityVersion);

            var info = afile.file.GetAssetsOfType(AssetClassID.Texture2D)
                .First(i => i.PathId == pathId);
            var bf = am.GetBaseField(afile, info);

            bf["m_Width"].AsInt = nw;
            bf["m_Height"].AsInt = nh;
            bf["m_TextureFormat"].AsInt = (int)TextureFormat.RGBA32;
            bf["m_MipCount"].AsInt = 1;
            if (bf["m_CompleteImageSize"] != null && !bf["m_CompleteImageSize"].IsDummy)
                bf["m_CompleteImageSize"].AsInt = rgba.Length;
            bf["image data"].AsByteArray = rgba;
            var sd = bf["m_StreamData"];
            sd["offset"].AsLong = 0; sd["size"].AsLong = 0; sd["path"].AsString = "";

            // 4. 重打包 (2.x replacer 模式)
            var replacers = new List<AssetsReplacer> { new AssetsReplacerFromMemory(afile.file, info, bf) };
            byte[] afileBytes;
            using (var msm = new MemoryStream())
            {
                using var w2 = new AssetsFileWriter(msm);
                afile.file.Write(w2, 0, replacers, null);
                afileBytes = msm.ToArray();
            }
            var bundleRepl = new BundleReplacerFromMemory(afile.name, afile.name, true, afileBytes, -1);
            using (var bw = new AssetsFileWriter(tmp))
                bun.file.Write(bw, new List<BundleReplacer> { bundleRepl });
        }
        finally { am.UnloadAll(); }

        // 5. 用临时文件替换原 bundle
        File.Delete(bundlePath);
        File.Move(tmp, bundlePath);
    }

    /// <summary>从备份还原一个 bundle。</summary>
    public bool RestoreFromBackup(string bundlePath, string backupDir)
    {
        string backup = Path.Combine(backupDir, Path.GetFileName(bundlePath));
        if (!File.Exists(backup)) return false;
        File.Copy(backup, bundlePath, true);
        return true;
    }
}
