using AssetsTools.NET;
using AssetsTools.NET.Extra;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SharpImage = SixLabors.ImageSharp.Image;
using SharpPoint = SixLabors.ImageSharp.Point;
using SharpRectangle = SixLabors.ImageSharp.Rectangle;

namespace JixModMaker;

public sealed record SkillAnimationInfo(string TextureName, long TexturePathId, int AtlasWidth, int AtlasHeight,
    int FrameWidth, int FrameHeight, IReadOnlyList<string> FrameNames);

public static class SkillAnimationEngine
{
    private sealed record Tile(float MinX, float MinY, float MaxX, float MaxY,
        float MinU, float MinV, float MaxU, float MaxV);

    private sealed record FrameLayout(string Name, float MinX, float MinY, float MaxX, float MaxY,
        IReadOnlyList<Tile> Tiles);

    private sealed record BundleLayout(SkillAnimationInfo Info, IReadOnlyList<FrameLayout> Frames);

    public static bool IsCandidate(TexRef asset) => asset is { IsTexture: true, IsSkillAnimation: true }
        && ResourceCategories.IsSkillAnimationAtlas(asset.Name);

    public static SkillAnimationInfo Inspect(string bundlePath, long texturePathId, string textureName = null)
        => ReadLayout(bundlePath, texturePathId, textureName).Info;

    public static byte[] DecodeFramePreview(string bundlePath, long texturePathId, int frameIndex)
    {
        var layout = ReadLayout(bundlePath, texturePathId, null);
        if (frameIndex < 0 || frameIndex >= layout.Frames.Count) throw new ArgumentOutOfRangeException(nameof(frameIndex));
        byte[] source = new ModEngine().DecodePng(bundlePath, layout.Info.TexturePathId, 0);
        using var atlas = SharpImage.Load<Rgba32>(source);
        using var frame = new SixLabors.ImageSharp.Image<Rgba32>(layout.Info.FrameWidth, layout.Info.FrameHeight);
        foreach (var (frameRect, atlasRect) in TileRectangles(layout.Frames[frameIndex], frame.Width, frame.Height, atlas.Width, atlas.Height))
        {
            using var piece = atlas.Clone(context => context.Crop(atlasRect).Resize(frameRect.Width, frameRect.Height));
            frame.Mutate(context => context.DrawImage(piece, new SharpPoint(frameRect.X, frameRect.Y), 1f));
        }
        using var output = new MemoryStream();
        frame.SaveAsPng(output);
        return output.ToArray();
    }

    public static async Task<SkillAnimationInfo> ReplaceAsync(string bundlePath, long texturePathId, string textureName,
        string input, string backupDir, string backupName, string workDirectory, PortraitVideoConverter.Options options,
        CancellationToken token, IProgress<string> progress = null)
    {
        if (!File.Exists(bundlePath)) throw new FileNotFoundException("找不到技能动画资源包。", bundlePath);
        if (!File.Exists(input)) throw new FileNotFoundException("找不到视频或 GIF 文件。", input);
        Directory.CreateDirectory(workDirectory);
        var layout = await Task.Run(() => ReadLayout(bundlePath, texturePathId, textureName), token);
        byte[] atlas = await BuildAtlasAsync(bundlePath, layout, input, workDirectory, options, token, progress);
        token.ThrowIfCancellationRequested();

        string rollback = Path.Combine(workDirectory, "before-skill-animation.bundle");
        File.Copy(bundlePath, rollback, true);
        try
        {
            progress?.Report("正在写入重建后的技能动画图集...");
            await Task.Run(() => new ModEngine().ReplaceInPlaceFromBytes(bundlePath, layout.Info.TexturePathId,
                atlas, backupDir, backupName), token);
            token.ThrowIfCancellationRequested();
            var verified = await Task.Run(() => ReadLayout(bundlePath, layout.Info.TexturePathId, layout.Info.TextureName), token);
            if (verified.Info.FrameNames.Count != layout.Info.FrameNames.Count ||
                verified.Info.AtlasWidth != layout.Info.AtlasWidth || verified.Info.AtlasHeight != layout.Info.AtlasHeight)
                throw new InvalidDataException("重建后的技能动画图集未通过结构校验。");
            byte[] decoded = await Task.Run(() => new ModEngine().DecodePng(bundlePath, layout.Info.TexturePathId, 64), token);
            if (decoded.Length < 64) throw new InvalidDataException("重建后的技能动画图集无法回读解码。");
            return verified.Info;
        }
        catch
        {
            File.Copy(rollback, bundlePath, true);
            throw;
        }
    }

    private static async Task<byte[]> BuildAtlasAsync(string bundlePath, BundleLayout layout, string input, string work,
        PortraitVideoConverter.Options options, CancellationToken token, IProgress<string> progress)
    {
        string frameDirectory = Path.Combine(work, "skill-frames");
        if (Directory.Exists(frameDirectory)) Directory.Delete(frameDirectory, true);
        Directory.CreateDirectory(frameDirectory);
        progress?.Report($"正在提取并采样 {layout.Frames.Count} 帧技能动画...");
        string pattern = Path.Combine(frameDirectory, "frame-%05d.png");
        int decodeLimit = Math.Max(360, layout.Frames.Count * 3);
        int filterSize = Math.Max(2048, Math.Max(layout.Info.FrameWidth, layout.Info.FrameHeight));
        string filter = PortraitVideoConverter.Filter(options, filterSize) +
            $",scale={layout.Info.FrameWidth}:{layout.Info.FrameHeight}:flags=lanczos,format=rgba,fps=30";
        await PortraitVideoConverter.RunAsync(PortraitVideoSettings.Load().Ffmpeg, new[]
        {
            "-nostdin", "-v", "error", "-y", "-i", input, "-t", "12", "-an", "-vf", filter,
            "-frames:v", decodeLimit.ToString(), "-fps_mode", "passthrough", pattern
        }, token);
        var decodedFrames = Directory.EnumerateFiles(frameDirectory, "frame-*.png")
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
        if (decodedFrames.Length == 0) throw new InvalidDataException("没有从视频中解码出可用画面。");

        progress?.Report("正在按原生 Sprite 网格重建图集...");
        byte[] original = await Task.Run(() => new ModEngine().DecodePng(bundlePath, layout.Info.TexturePathId, 0), token);
        using var atlas = SharpImage.Load<Rgba32>(original);
        if (atlas.Width != layout.Info.AtlasWidth || atlas.Height != layout.Info.AtlasHeight)
            throw new InvalidDataException("图集实际尺寸与资源元数据不一致。");

        for (int i = 0; i < layout.Frames.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            int sourceIndex = layout.Frames.Count == 1 ? 0
                : (int)Math.Round(i * (decodedFrames.Length - 1d) / (layout.Frames.Count - 1d));
            using var frame = SharpImage.Load<Rgba32>(decodedFrames[sourceIndex]);
            WriteFrame(atlas, frame, layout.Frames[i]);
        }

        using var output = new MemoryStream();
        await atlas.SaveAsPngAsync(output, token);
        return output.ToArray();
    }

    private static void WriteFrame(SixLabors.ImageSharp.Image<Rgba32> atlas,
        SixLabors.ImageSharp.Image<Rgba32> frame, FrameLayout layout)
    {
        foreach (var (source, destination) in TileRectangles(layout, frame.Width, frame.Height, atlas.Width, atlas.Height))
        {
            Clear(atlas, destination);
            using var piece = frame.Clone(context => context.Crop(source).Resize(destination.Width, destination.Height));
            atlas.Mutate(context => context.DrawImage(piece, new SharpPoint(destination.X, destination.Y), 1f));
        }
    }

    private static IEnumerable<(SharpRectangle Frame, SharpRectangle Atlas)> TileRectangles(FrameLayout layout,
        int frameWidth, int frameHeight, int atlasWidth, int atlasHeight)
    {
        float width = layout.MaxX - layout.MinX;
        float height = layout.MaxY - layout.MinY;
        if (width <= 0 || height <= 0) throw new InvalidDataException("技能 Sprite 的几何范围无效。");
        foreach (var tile in layout.Tiles)
        {
            int sx0 = Edge((tile.MinX - layout.MinX) / width, frameWidth);
            int sx1 = Edge((tile.MaxX - layout.MinX) / width, frameWidth);
            int sy0 = Edge((layout.MaxY - tile.MaxY) / height, frameHeight);
            int sy1 = Edge((layout.MaxY - tile.MinY) / height, frameHeight);
            int dx0 = Edge(tile.MinU, atlasWidth);
            int dx1 = Edge(tile.MaxU, atlasWidth);
            int dy0 = Edge(1 - tile.MaxV, atlasHeight);
            int dy1 = Edge(1 - tile.MinV, atlasHeight);
            yield return (Rect(sx0, sy0, sx1, sy1, frameWidth, frameHeight),
                Rect(dx0, dy0, dx1, dy1, atlasWidth, atlasHeight));
        }
    }

    private static int Edge(float normalized, int size)
        => Math.Clamp((int)Math.Round(normalized * size), 0, size);

    private static SharpRectangle Rect(int x0, int y0, int x1, int y1, int maxWidth, int maxHeight)
    {
        x0 = Math.Clamp(x0, 0, maxWidth - 1);
        y0 = Math.Clamp(y0, 0, maxHeight - 1);
        x1 = Math.Clamp(x1, x0 + 1, maxWidth);
        y1 = Math.Clamp(y1, y0 + 1, maxHeight);
        return new SharpRectangle(x0, y0, x1 - x0, y1 - y0);
    }

    private static void Clear(SixLabors.ImageSharp.Image<Rgba32> image, SharpRectangle rectangle)
    {
        image.ProcessPixelRows(accessor =>
        {
            for (int y = rectangle.Top; y < rectangle.Bottom; y++)
                accessor.GetRowSpan(y).Slice(rectangle.Left, rectangle.Width).Clear();
        });
    }

    private static BundleLayout ReadLayout(string bundlePath, long texturePathId, string textureName)
    {
        var manager = NewManager();
        try
        {
            var bundle = manager.LoadBundleFile(bundlePath);
            var file = manager.LoadAssetsFileFromBundle(bundle, 0, false);
            if (manager.ClassDatabase == null && manager.ClassPackage != null)
                manager.LoadClassDatabaseFromPackage(file.file.Metadata.UnityVersion);
            var textures = file.file.GetAssetsOfType(AssetClassID.Texture2D);
            var textureInfo = texturePathId != 0
                ? textures.SingleOrDefault(info => info.PathId == texturePathId)
                : textures.SingleOrDefault(info => manager.GetBaseField(file, info)["m_Name"].AsString == textureName);
            if (textureInfo == null) throw new InvalidDataException("资源包中找不到选中的技能动画 Texture2D。");
            var texture = manager.GetBaseField(file, textureInfo);
            string resolvedName = texture["m_Name"].AsString;
            if (!ResourceCategories.IsSkillAnimationAtlas(resolvedName))
                throw new InvalidDataException("选中的贴图不是 Talent 技能动画图集。");
            int atlasWidth = texture["m_Width"].AsInt;
            int atlasHeight = texture["m_Height"].AsInt;

            var frames = new List<(FrameLayout Layout, float Width, float Height)>();
            foreach (var spriteInfo in file.file.GetAssetsOfType(AssetClassID.Sprite))
            {
                var sprite = manager.GetBaseField(file, spriteInfo);
                var renderData = sprite["m_RD"];
                if (renderData["texture"]["m_PathID"].AsLong != textureInfo.PathId) continue;
                string name = sprite["m_Name"].AsString;
                float frameWidth = sprite["m_Rect"]["width"].AsFloat;
                float frameHeight = sprite["m_Rect"]["height"].AsFloat;
                frames.Add((ReadFrameLayout(name, renderData), frameWidth, frameHeight));
            }
            frames = frames.OrderBy(frame => TrailingNumber(frame.Layout.Name))
                .ThenBy(frame => frame.Layout.Name, StringComparer.OrdinalIgnoreCase).ToList();
            if (frames.Count < 2) throw new InvalidDataException("选中的 Talent 图集不包含可播放的 Sprite 序列。");
            int targetWidth = Math.Max(2, (int)Math.Round(frames[0].Width));
            int targetHeight = Math.Max(2, (int)Math.Round(frames[0].Height));
            if (frames.Any(frame => frame.Width <= 0 || frame.Height <= 0 ||
                Math.Abs(frame.Width / frame.Height - targetWidth / (double)targetHeight) > .01))
                throw new InvalidDataException("Talent Sprite 序列中存在比例不一致的帧。");
            var info = new SkillAnimationInfo(resolvedName, textureInfo.PathId, atlasWidth, atlasHeight,
                targetWidth, targetHeight, frames.Select(frame => frame.Layout.Name).ToArray());
            return new BundleLayout(info, frames.Select(frame => frame.Layout).ToArray());
        }
        finally { manager.UnloadAll(); }
    }

    private static FrameLayout ReadFrameLayout(string name, AssetTypeValueField renderData)
    {
        var vertexData = renderData["m_VertexData"];
        int vertexCount = checked((int)vertexData["m_VertexCount"].AsUInt);
        var channels = vertexData["m_Channels"]["Array"].Children;
        if (vertexCount <= 0 || channels.Count <= 4) throw new InvalidDataException(name + " 没有可读取的 Sprite 顶点。");
        var position = ReadChannel(channels[0]);
        var uv = ReadChannel(channels[4]);
        if (position.Format != 0 || position.Dimension < 2 || uv.Format != 0 || uv.Dimension < 2)
            throw new InvalidDataException(name + " 使用了暂不支持的 Sprite 顶点格式。");
        var active = channels.Select(ReadChannel).Where(channel => channel.Dimension > 0).ToArray();
        var strides = active.GroupBy(channel => channel.Stream)
            .ToDictionary(group => group.Key, group => group.Max(channel => channel.Offset + channel.Dimension * 4));
        var streamStarts = new Dictionary<int, int>();
        int cursor = 0;
        foreach (int stream in strides.Keys.OrderBy(value => value))
        {
            cursor = Align(cursor, 16);
            streamStarts[stream] = cursor;
            cursor += checked(strides[stream] * vertexCount);
        }
        byte[] data = vertexData["m_DataSize"].AsByteArray;
        if (cursor > data.Length) throw new InvalidDataException(name + " 的 Sprite 顶点数据不完整。");
        var positions = new (float X, float Y)[vertexCount];
        var uvs = new (float X, float Y)[vertexCount];
        for (int i = 0; i < vertexCount; i++)
        {
            positions[i] = ReadVector2(data, streamStarts[position.Stream] + i * strides[position.Stream] + position.Offset);
            uvs[i] = ReadVector2(data, streamStarts[uv.Stream] + i * strides[uv.Stream] + uv.Offset);
        }

        byte[] indices = renderData["m_IndexBuffer"]["Array"].AsByteArray;
        var parent = Enumerable.Range(0, vertexCount).ToArray();
        int Find(int value) => parent[value] == value ? value : parent[value] = Find(parent[value]);
        void Union(int a, int b)
        {
            a = Find(a); b = Find(b);
            if (a != b) parent[b] = a;
        }
        if (indices.Length % 2 != 0) throw new InvalidDataException(name + " 的 Sprite 索引缓冲格式不受支持。");
        for (int offset = 0; offset < indices.Length; offset += 6)
        {
            if (offset + 6 > indices.Length) throw new InvalidDataException(name + " 的 Sprite 三角形数据不完整。");
            int a = BitConverter.ToUInt16(indices, offset);
            int b = BitConverter.ToUInt16(indices, offset + 2);
            int c = BitConverter.ToUInt16(indices, offset + 4);
            if (a >= vertexCount || b >= vertexCount || c >= vertexCount)
                throw new InvalidDataException(name + " 含有越界的 Sprite 索引。");
            Union(a, b); Union(b, c);
        }
        var tiles = new List<Tile>();
        foreach (var component in Enumerable.Range(0, vertexCount).GroupBy(Find))
        {
            var ids = component.ToArray();
            if (ids.Length != 4) throw new InvalidDataException(name + " 不是可重建的矩形分块 Sprite。");
            float minX = ids.Min(id => positions[id].X), maxX = ids.Max(id => positions[id].X);
            float minY = ids.Min(id => positions[id].Y), maxY = ids.Max(id => positions[id].Y);
            float minU = ids.Min(id => uvs[id].X), maxU = ids.Max(id => uvs[id].X);
            float minV = ids.Min(id => uvs[id].Y), maxV = ids.Max(id => uvs[id].Y);
            if (maxX <= minX || maxY <= minY || maxU <= minU || maxV <= minV ||
                minU < -.001 || minV < -.001 || maxU > 1.001 || maxV > 1.001)
                throw new InvalidDataException(name + " 的 Sprite 分块坐标无效。");
            tiles.Add(new Tile(minX, minY, maxX, maxY, minU, minV, maxU, maxV));
        }
        float allMinX = positions.Min(point => point.X), allMaxX = positions.Max(point => point.X);
        float allMinY = positions.Min(point => point.Y), allMaxY = positions.Max(point => point.Y);
        return new FrameLayout(name, allMinX, allMinY, allMaxX, allMaxY, tiles);
    }

    private sealed record Channel(int Stream, int Offset, int Format, int Dimension);

    private static Channel ReadChannel(AssetTypeValueField field) => new(
        field["stream"].AsByte, field["offset"].AsByte, field["format"].AsByte, field["dimension"].AsByte);

    private static (float X, float Y) ReadVector2(byte[] data, int offset)
    {
        if (offset < 0 || offset + 8 > data.Length) throw new InvalidDataException("Sprite 顶点数据不完整。");
        return (BitConverter.ToSingle(data, offset), BitConverter.ToSingle(data, offset + 4));
    }

    private static int TrailingNumber(string name)
    {
        int start = (name ?? "").Length;
        while (start > 0 && char.IsDigit(name[start - 1])) start--;
        return start < (name ?? "").Length && int.TryParse(name[start..], out int value) ? value : int.MaxValue;
    }

    private static int Align(int value, int alignment) => (value + alignment - 1) & ~(alignment - 1);

    private static AssetsManager NewManager()
    {
        var manager = new AssetsManager();
        string tpk = Path.Combine(AppContext.BaseDirectory, "classdata.tpk");
        if (File.Exists(tpk)) manager.LoadClassPackage(tpk);
        return manager;
    }
}
