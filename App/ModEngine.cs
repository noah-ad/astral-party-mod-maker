using AssetsTools.NET;
using AssetsTools.NET.Extra;
using AssetsTools.NET.Texture;
using System.Security.Cryptography;
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
    public string BundleName;
    public long PathId;
    public string Name;
    public string Kind = ResourceKinds.Texture;
    public string CategoryId;
    public string CategoryLabel;
    public string Source;
    public int Width;
    public int Height;
    public string Format;
    public NativeMovieTiming MovieTiming;
    public bool IsSkillAnimation;
    public string OwnerHeroId;
    public string OwnerVariant;
    public bool OwnerIsMonster;
    public bool Modded;
    public ModificationState Modification;
    public string Display;   // 显示名 (角色模式用 "半身 01" 等友好标签; null 则用 Name)
    public override string ToString() => $"{Name} ({Width}x{Height})";
    public bool IsTexture => Kind == ResourceKinds.Texture;
    public bool IsSkillMovie => Kind == ResourceKinds.SkillMovie && NameParser.IsSkillMovie(Name);
    public bool IsVisual => IsTexture || IsSkillMovie;
}

/// <summary>
/// mod 引擎: 封装"解包 bundle 贴图 / 导出预览 / 写回替换"。
/// 逻辑已通过 round-trip 验证 (AssetsTools.NET 2.x + RGBA32 写回)。
/// </summary>
public class ModEngine
{
    public sealed record TextureSignature(string Name, long PathId, int Width, int Height, string Format, string Hash);
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

    public List<TextureSignature> ReadTextureSignatures(string bundlePath)
    {
        using var source = new FileStream(bundlePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var manager = NewManager();
        try
        {
            var bundle = manager.LoadBundleFile(source, true);
            var assets = manager.LoadAssetsFileFromBundle(bundle, 0, false);
            if (manager.ClassDatabase == null && manager.ClassPackage != null)
                manager.LoadClassDatabaseFromPackage(assets.file.Metadata.UnityVersion);
            var result = new List<TextureSignature>();
            foreach (var info in assets.file.GetAssetsOfType(AssetClassID.Texture2D))
            {
                var field = manager.GetBaseField(assets, info);
                var texture = TextureFile.ReadTextureFile(field);
                if ((texture.pictureData == null || texture.pictureData.Length == 0) && texture.m_StreamData.size > 0
                    && !texture.SetPictureDataFromBundle(bundle))
                    throw new IOException("无法读取贴图的流式数据：" + texture.m_Name);
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                foreach (int value in new[] { texture.m_Width, texture.m_Height, texture.m_TextureFormat, texture.m_MipCount,
                    texture.m_ImageCount, texture.m_TextureDimension, texture.m_ColorSpace })
                    hash.AppendData(BitConverter.GetBytes(value));
                hash.AppendData(texture.pictureData ?? Array.Empty<byte>());
                result.Add(new(texture.m_Name, info.PathId, texture.m_Width, texture.m_Height,
                    ((TextureFormat)texture.m_TextureFormat).ToString(), Convert.ToHexString(hash.GetHashAndReset())));
            }
            return result;
        }
        finally { manager.UnloadAll(); }
    }

    /// <summary>列出一个 bundle 里所有 Texture2D。</summary>
    public List<TexRef> ListTextures(string bundlePath)
        => ListTextureHeaders(bundlePath);

    /// <summary>只读 Texture2D 元数据；不扫其它资产，不导出像素，用于快速索引。</summary>
    public List<TexRef> ListTextureHeaders(string bundlePath) => new BundleHeaderReader(this).Read(bundlePath, false);

    public TexRef FindTextureByName(string bundlePath, string textureName)
    {
        if (string.IsNullOrWhiteSpace(textureName)) return null;
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
                var name = bf["m_Name"].AsString;
                if (!string.Equals(name, textureName, StringComparison.Ordinal))
                    continue;
                return ReadTextureHeader(am, afile, info, bundlePath, bf);
            }
        }
        finally { am.UnloadAll(); }
        return null;
    }

    private static TexRef ReadTextureHeader(AssetsManager am, AssetsFileInstance afile, AssetFileInfo info,
                                            string bundlePath, AssetTypeValueField bf = null)
    {
        try
        {
            bf ??= am.GetBaseField(afile, info);
            int fmt = bf["m_TextureFormat"].AsInt;
            return new TexRef
            {
                BundlePath = bundlePath,
                BundleName = Path.GetFileName(bundlePath),
                PathId = info.PathId,
                Kind = ResourceKinds.Texture,
                Name = bf["m_Name"].AsString,
                Width = bf["m_Width"].AsInt,
                Height = bf["m_Height"].AsInt,
                Format = ((TextureFormat)fmt).ToString()
            };
        }
        catch { return null; }
    }

    /// <summary>列出一个 bundle / __data 里的常用 Unity 资源。</summary>
    public List<TexRef> ListAssets(string bundlePath) => new BundleHeaderReader(this).Read(bundlePath, true);

    internal sealed class BundleHeaderReader
    {
        private readonly AssetsManager _manager;
        private string _unityVersion;

        public BundleHeaderReader(ModEngine engine) => _manager = engine.NewManager();

        public List<TexRef> Read(string bundlePath, bool advanced)
        {
            var result = new List<TexRef>();
            using var source = new FileStream(bundlePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            try
            {
                var bun = _manager.LoadBundleFile(source);
                var file = _manager.LoadAssetsFileFromBundle(bun, 0, false);
                if (file == null) return result;
                string version = file.file.Metadata.UnityVersion;
                if (_manager.ClassPackage != null && _unityVersion != version)
                {
                    _manager.LoadClassDatabaseFromPackage(version);
                    _unityVersion = version;
                }

                var prefixes = new Dictionary<int, AssetTypeTemplateField>();
                foreach (var info in file.file.GetAssetsOfType(AssetClassID.Texture2D))
                {
                    AssetTypeValueField fields = null;
                    try
                    {
                        if (!prefixes.TryGetValue(info.TypeIdOrIndex, out var prefix))
                            prefixes[info.TypeIdOrIndex] = prefix = HeaderTemplate(_manager.GetTemplateBaseField(file, info));
                        fields = prefix?.MakeValue(file.file.Reader, info.GetAbsoluteByteStart(file.file));
                    }
                    catch (Exception ex) when (ex is not OutOfMemoryException) { }
                    if (ReadTextureHeader(_manager, file, info, bundlePath, fields) is { } texture) result.Add(texture);
                }

                if (advanced)
                {
                    AddNamedAssets(_manager, file, bundlePath, AssetClassID.AudioClip, ResourceKinds.Audio, result);
                    AddNamedAssets(_manager, file, bundlePath, AssetClassID.TextAsset, ResourceKinds.Text, result);
                    AddNamedAssets(_manager, file, bundlePath, AssetClassID.Mesh, ResourceKinds.Mesh, result);
                    AddNamedAssets(_manager, file, bundlePath, AssetClassID.AnimationClip, ResourceKinds.Animation, result);
                }
                return result;
            }
            finally { _manager.UnloadAll(false); }
        }

        // Use the bundle's own type tree and stop before inline image data.
        private static AssetTypeTemplateField HeaderTemplate(AssetTypeTemplateField template)
        {
            if (template?.Children == null) return null;
            var fields = template.Children;
            int last = -1;
            foreach (string key in new[] { "m_Name", "m_Width", "m_Height", "m_TextureFormat" })
            {
                int index = fields.FindIndex(field => field.Name == key);
                if (index < 0) return null;
                last = Math.Max(last, index);
            }
            int image = fields.FindIndex(field => field.ValueType == AssetValueType.ByteArray);
            if (image >= 0 && image < last) return null;
            return new AssetTypeTemplateField { Name = template.Name, Type = template.Type,
                ValueType = template.ValueType, Children = fields.GetRange(0, last + 1) };
        }
    }

    private static void AddNamedAssets(AssetsManager am, AssetsFileInstance afile, string bundlePath,
                                       AssetClassID classId, string kind, List<TexRef> output)
    {
        foreach (var info in afile.file.GetAssetsOfType(classId))
        {
            try
            {
                var bf = am.GetBaseField(afile, info);
                string name = "";
                try { name = bf["m_Name"].AsString; } catch { }
                if (string.IsNullOrWhiteSpace(name)) name = "(未命名)";
                output.Add(new TexRef
                {
                    BundlePath = bundlePath,
                    BundleName = Path.GetFileName(bundlePath),
                    PathId = info.PathId,
                    Kind = kind,
                    Name = name,
                    Format = kind
                });
            }
            catch { }
        }
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
            return DecodeTexturePng(am, afile, info, maxSize);
        }
        finally { am.UnloadAll(); }
    }

    /// <summary>一次打开 bundle，批量解码同包缩略图，避免每张图重复解析资源包。</summary>
    public Dictionary<long, byte[]> DecodePngBatch(string bundlePath, IEnumerable<long> pathIds, int maxSize = 0)
    {
        var wanted = pathIds.Where(id => id != 0).ToHashSet();
        var output = new Dictionary<long, byte[]>();
        if (wanted.Count == 0) return output;

        var am = NewManager();
        try
        {
            var bun = am.LoadBundleFile(bundlePath);
            var afile = am.LoadAssetsFileFromBundle(bun, 0, false);
            if (am.ClassDatabase == null && am.ClassPackage != null)
                am.LoadClassDatabaseFromPackage(afile.file.Metadata.UnityVersion);

            foreach (var info in afile.file.GetAssetsOfType(AssetClassID.Texture2D))
            {
                if (!wanted.Contains(info.PathId)) continue;
                try { output[info.PathId] = DecodeTexturePng(am, afile, info, maxSize); }
                catch { }
                if (output.Count >= wanted.Count) break;
            }
            return output;
        }
        finally { am.UnloadAll(); }
    }

    private static byte[] DecodeTexturePng(AssetsManager am, AssetsFileInstance afile, AssetFileInfo info, int maxSize)
    {
        var bf = am.GetBaseField(afile, info);
        int w = bf["m_Width"].AsInt, h = bf["m_Height"].AsInt;
        var tf = TextureFile.ReadTextureFile(bf);
        byte[] bgra = tf.GetTextureData(afile);

        using var img = Image.LoadPixelData<Bgra32>(bgra.AsSpan(0, w * h * 4), w, h);
        img.Mutate(c => c.Flip(FlipMode.Vertical));
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

    /// <summary>用图片文件替换指定贴图, 原地写回 bundle (先备份)。</summary>
    public void ReplaceInPlace(string bundlePath, long pathId, string newImagePath, string backupDir, string backupName = null, string expectedName = null)
    {
        using var img = Image.Load<Rgba32>(newImagePath);
        ReplaceCore(bundlePath, pathId, img, backupDir, backupName, expectedName);
    }

    /// <summary>用图片字节 (如图包内的 PNG) 替换指定贴图, 原地写回 bundle (先备份)。</summary>
    public void ReplaceInPlaceFromBytes(string bundlePath, long pathId, byte[] pngBytes, string backupDir, string backupName = null, string expectedName = null)
    {
        using var img = Image.Load<Rgba32>(pngBytes);
        ReplaceCore(bundlePath, pathId, img, backupDir, backupName, expectedName);
    }

    /// <summary>
    /// 核心: 把图写回 Texture2D (RGBA32 未压缩, 库无 BC7 编码器)。允许尺寸不同。
    /// </summary>
    private void ReplaceCore(string bundlePath, long pathId, Image<Rgba32> img, string backupDir, string backupName, string expectedName)
    {
        // 2. 图 -> RGBA32 bottom-up
        img.Mutate(c => c.Flip(FlipMode.Vertical));
        int nw = img.Width, nh = img.Height;
        byte[] rgba = new byte[nw * nh * 4];
        img.CopyPixelDataTo(rgba);

        // 3. 打开 bundle, 改字段
        string tmp = bundlePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            WriteReplacement();
            if (!string.IsNullOrEmpty(backupDir))
            {
                string backup = ResourceLocator.BackupPath(bundlePath, backupDir, backupName);
                Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                if (!File.Exists(backup)) File.Copy(bundlePath, backup);
            }
            File.Move(tmp, bundlePath, true);
        }
        finally { if (File.Exists(tmp)) File.Delete(tmp); }

        void WriteReplacement()
        {
            var am = NewManager();
            try
            {
                var bun = am.LoadBundleFile(bundlePath);
                var afile = am.LoadAssetsFileFromBundle(bun, 0, false);
                if (am.ClassDatabase == null && am.ClassPackage != null)
                    am.LoadClassDatabaseFromPackage(afile.file.Metadata.UnityVersion);

                var info = afile.file.GetAssetsOfType(AssetClassID.Texture2D).First(i => i.PathId == pathId);
                var bf = am.GetBaseField(afile, info);
                if (expectedName != null && !string.Equals(bf["m_Name"].AsString, expectedName, StringComparison.Ordinal))
                    throw new InvalidDataException("贴图名称已变化，请刷新索引后重试：" + expectedName);

                bf["m_Width"].AsInt = nw;
                bf["m_Height"].AsInt = nh;
                bf["m_TextureFormat"].AsInt = (int)TextureFormat.RGBA32;
                bf["m_MipCount"].AsInt = 1;
                if (bf["m_CompleteImageSize"] != null && !bf["m_CompleteImageSize"].IsDummy)
                    bf["m_CompleteImageSize"].AsInt = rgba.Length;
                bf["image data"].AsByteArray = rgba;
                var sd = bf["m_StreamData"];
                sd["offset"].AsLong = 0; sd["size"].AsLong = 0; sd["path"].AsString = "";

                var replacers = new List<AssetsReplacer> { new AssetsReplacerFromMemory(afile.file, info, bf) };
                using var buffer = new MemoryStream();
                using var writer = new AssetsFileWriter(buffer);
                afile.file.Write(writer, 0, replacers, null);
                var bundleRepl = new BundleReplacerFromMemory(afile.name, afile.name, true, buffer.ToArray(), -1);
                using var bw = new AssetsFileWriter(tmp);
                bun.file.Write(bw, new List<BundleReplacer> { bundleRepl });
            }
            finally { am.UnloadAll(); }
        }
    }

    /// <summary>从备份还原一个 bundle。</summary>
    public bool RestoreFromBackup(string bundlePath, string backupDir, string backupName = null)
    {
        string backup = ResourceLocator.BackupPath(bundlePath, backupDir, backupName);
        if (!File.Exists(backup)) return false;
        BundleRestore.Apply(BundleRestore.Prepare(bundlePath, backupDir, backupName));
        return true;
    }

    public static string SafeBackupName(string name)
    {
        var raw = string.IsNullOrWhiteSpace(name) ? "resource.bundle" : name;
        foreach (var c in Path.GetInvalidFileNameChars())
            raw = raw.Replace(c, '_');
        return raw.Replace('\\', '_').Replace('/', '_');
    }
}
