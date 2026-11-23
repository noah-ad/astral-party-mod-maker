using AssetsTools.NET;
using AssetsTools.NET.Extra;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Image = SixLabors.ImageSharp.Image;

namespace JixModMaker;

public static class MobileTextureBundle
{
    public static bool IsAndroid(string path)
    {
        var manager = new AssetsManager();
        try { return manager.LoadAssetsFileFromBundle(manager.LoadBundleFile(path), 0, false).file.Metadata.TargetPlatform == 13; }
        catch (Exception error) when (error is IOException or InvalidDataException or ArgumentException) { return false; }
        finally { manager.UnloadAll(); }
    }

    public static void Replace(string original, string output, IReadOnlyDictionary<string, byte[]> images)
    {
        if (images.Count == 0) throw new InvalidOperationException("没有选择贴图。");
        var engine = new ModEngine();
        var manager = new AssetsManager();
        manager.LoadClassPackage(Path.Combine(AppContext.BaseDirectory, "classdata.tpk"));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        string temporary = output + "." + Guid.NewGuid().ToString("N") + ".unpacked";
        try
        {
            var bundle = manager.LoadBundleFile(original);
            var assets = manager.LoadAssetsFileFromBundle(bundle, 0, false);
            if (assets.file.Metadata.TargetPlatform != 13) throw new InvalidDataException("目标不是安卓原始资源，已拒绝混用 PC Bundle。");
            manager.LoadClassDatabaseFromPackage(assets.file.Metadata.UnityVersion);
            var replacements = new List<AssetsReplacer>();
            var expected = new Dictionary<string, (long PathId, int Width, int Height, byte[] Pixels)>(StringComparer.Ordinal);
            foreach (var info in assets.file.GetAssetsOfType(AssetClassID.Texture2D))
            {
                var field = manager.GetBaseField(assets, info);
                string name = field["m_Name"].AsString;
                if (!images.TryGetValue(name, out var png)) continue;
                if (expected.ContainsKey(name)) throw new InvalidDataException("目标包内有重名贴图：" + name);
                int width = field["m_Width"].AsInt, height = field["m_Height"].AsInt;
                using var image = Image.Load<Rgba32>(png);
                if (image.Width != width || image.Height != height)
                    image.Mutate(context => context.Resize(new ResizeOptions
                    {
                        Size = new(width, height), Mode = ResizeMode.Pad, Sampler = KnownResamplers.Lanczos3
                    }));
                byte[] topDown = new byte[checked(width * height * 4)];
                image.CopyPixelDataTo(topDown);
                expected.Add(name, (info.PathId, width, height, topDown));
                image.Mutate(context => context.Flip(FlipMode.Vertical));
                byte[] pixels = new byte[topDown.Length];
                image.CopyPixelDataTo(pixels);
                field["m_TextureFormat"].AsInt = 4;
                field["m_MipCount"].AsInt = 1;
                field["m_CompleteImageSize"].AsInt = pixels.Length;
                field["image data"].AsByteArray = pixels;
                field["m_StreamData"]["offset"].AsLong = 0;
                field["m_StreamData"]["size"].AsLong = 0;
                field["m_StreamData"]["path"].AsString = "";
                replacements.Add(new AssetsReplacerFromMemory(assets.file, info, field));
            }
            if (expected.Count != images.Count) throw new InvalidDataException("当前安卓包缺少所选贴图，不能使用旧版本资源代替：" +
                string.Join("、", images.Keys.Except(expected.Keys)));
            byte[] data;
            using (var buffer = new MemoryStream())
            {
                using var writer = new AssetsFileWriter(buffer);
                assets.file.Write(writer, 0, replacements, null);
                data = buffer.ToArray();
            }
            using (var writer = new AssetsFileWriter(temporary))
                bundle.file.Write(writer, new List<BundleReplacer> { new BundleReplacerFromMemory(assets.name, assets.name, true, data, -1) });
            manager.UnloadAll();
            Compress(temporary, output);
            AnimatedPortraitPatch.VerifyTypeLayout(original, output);
            VerifyObjects(original, output, expected.Values.Select(value => value.PathId).ToHashSet());
            foreach (var pair in expected)
            {
                using var decoded = Image.Load<Rgba32>(engine.DecodePng(output, pair.Value.PathId));
                byte[] pixels = new byte[pair.Value.Pixels.Length];
                decoded.CopyPixelDataTo(pixels);
                if (decoded.Width != pair.Value.Width || decoded.Height != pair.Value.Height || !pixels.AsSpan().SequenceEqual(pair.Value.Pixels))
                    throw new InvalidDataException("安卓贴图回读像素校验失败：" + pair.Key);
            }
        }
        catch { if (File.Exists(output)) File.Delete(output); throw; }
        finally
        {
            manager.UnloadAll();
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static void Compress(string input, string output)
    {
        var manager = new AssetsManager();
        try
        {
            var bundle = manager.LoadBundleFile(input);
            using var writer = new AssetsFileWriter(output);
            bundle.file.Pack(bundle.file.Reader, writer, AssetBundleCompressionType.LZ4, false, null);
        }
        finally { manager.UnloadAll(); }
        using var check = File.OpenRead(output);
        FileIntegrity.CheckBundleLength(check, output);
    }

    private static void VerifyObjects(string original, string output, HashSet<long> changed)
    {
        var manager = new AssetsManager();
        try
        {
            var beforeBundle = manager.LoadBundleFile(original);
            var afterBundle = manager.LoadBundleFile(output);
            var before = manager.LoadAssetsFileFromBundle(beforeBundle, 0, false).file;
            var after = manager.LoadAssetsFileFromBundle(afterBundle, 0, false).file;
            var old = before.AssetInfos.ToDictionary(info => info.PathId);
            foreach (var info in after.AssetInfos.Where(info => !changed.Contains(info.PathId)))
            {
                var previous = old[info.PathId];
                before.Reader.Position = previous.GetAbsoluteByteStart(before);
                after.Reader.Position = info.GetAbsoluteByteStart(after);
                if (previous.ByteSize != info.ByteSize || !before.Reader.ReadBytes(checked((int)previous.ByteSize)).AsSpan()
                    .SequenceEqual(after.Reader.ReadBytes(checked((int)info.ByteSize))))
                    throw new InvalidDataException("非目标对象发生变化，已拒绝导出。");
            }
            // External resource streams and other serialized files must stay byte-identical.
            for (int i = 1; i < beforeBundle.file.BlockAndDirInfo.DirectoryInfos.Length; i++)
            {
                beforeBundle.file.GetFileRange(i, out long oldOffset, out long oldSize);
                afterBundle.file.GetFileRange(i, out long newOffset, out long newSize);
                beforeBundle.file.DataReader.Position = oldOffset;
                afterBundle.file.DataReader.Position = newOffset;
                if (oldSize != newSize || !beforeBundle.file.DataReader.ReadBytes(checked((int)oldSize)).AsSpan()
                    .SequenceEqual(afterBundle.file.DataReader.ReadBytes(checked((int)newSize))))
                    throw new InvalidDataException("贴图包的其它流式资源发生变化。");
            }
        }
        finally { manager.UnloadAll(); }
    }
}
