using AssetsTools.NET;
using AssetsTools.NET.Extra;
using System.Security.Cryptography;
using System.Text.Json;

namespace JixModMaker;

public sealed record NativeMovieTiming(int Width, int Height, int FramerateN, int FramerateD,
    int TotalFrames, bool HasAlpha, bool HasAudio)
{
    public double Duration => TotalFrames * (double)FramerateD / FramerateN;
    public void Validate()
    {
        if (Width is < 8 or > 4096 || Height is < 8 or > 4096 || FramerateN <= 0 || FramerateD <= 0 ||
            FramerateN / (double)FramerateD > 60 || TotalFrames <= 0 || Duration > 60)
            throw new InvalidDataException("原生技能特写尺寸或时长超出支持范围。");
        if (HasAudio) throw new InvalidDataException("此技能视频内含音轨，当前不能在保留原音轨的前提下替换。");
    }
}

public sealed record SkillMovieInfo(string Name, long PathId, byte Loop, NativeMovieTiming Timing, string BundleHash);

public static class SkillMovieEngine
{
    private sealed record Movie(AssetFileInfo Info, AssetTypeValueField Field, byte[] Bytes);

    private static AssetsManager Manager()
    {
        var manager = new AssetsManager();
        manager.LoadClassPackage(Path.Combine(AppContext.BaseDirectory, "classdata.tpk"));
        return manager;
    }

    private static Movie Find(AssetsManager manager, AssetsFileInstance file, string name)
    {
        if (!NameParser.IsSkillMovie(name)) throw new InvalidDataException("不是原生技能特写资源。");
        var matches = new List<Movie>();
        foreach (var info in file.file.GetAssetsOfType(AssetClassID.MonoBehaviour))
        {
            var field = manager.GetBaseField(file, info);
            if (field["m_Name"].AsString != name) continue;
            var implementation = field["references"].Value?.AsManagedReferencesRegistry?.references
                .SingleOrDefault(reference => reference.type.ClassName == "CriSerializedBytesAssetImpl");
            if (implementation == null) continue;
            byte[] bytes = implementation.data["data"]["Array"].AsByteArray;
            if (bytes.AsSpan().StartsWith("CRID"u8)) matches.Add(new(info, field, bytes));
        }
        if (matches.Count != 1) throw new InvalidDataException("未找到唯一的原生技能视频：" + name);
        return matches[0];
    }

    public static (long PathId, byte Loop, byte[] Bytes) Read(string bundlePath, string name)
    {
        var manager = Manager();
        try
        {
            var file = manager.LoadAssetsFileFromBundle(manager.LoadBundleFile(bundlePath), 0, false);
            manager.LoadClassDatabaseFromPackage(file.file.Metadata.UnityVersion);
            var movie = Find(manager, file, name);
            return (movie.Info.PathId, movie.Field["assetInfo"]["loop"].AsByte, movie.Bytes);
        }
        finally { manager.UnloadAll(); }
    }

    public static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    public static async Task<SkillMovieInfo> InspectAsync(string bundlePath, string name, CancellationToken token)
    {
        await PortraitVideoConverter.EnsureConversionAvailableAsync(PortraitVideoSettings.Load(), token);
        string work = Path.Combine(Path.GetTempPath(), "JixNativeMovie-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            string hash = Hash(bundlePath);
            var movie = await Task.Run(() => Read(bundlePath, name), token);
            string source = Path.Combine(work, "native.usm");
            await File.WriteAllBytesAsync(source, movie.Bytes, token);
            string metadata = Path.Combine(work, "native.json");
            string tools = Path.Combine(AppContext.BaseDirectory, "Tools", "video");
            await PortraitVideoConverter.RunAsync(Path.Combine(tools, "python", "python.exe"),
                new[] { Path.Combine(tools, "inspect_movie.py"), source, metadata }, token);
            var timing = JsonSerializer.Deserialize<NativeMovieTiming>(await File.ReadAllTextAsync(metadata, token))
                ?? throw new InvalidDataException("无法读取原生技能视频元数据。");
            timing.Validate();
            if (hash != Hash(bundlePath)) throw new IOException("资源包在读取期间已更新，请重新选择。");
            return new(name, movie.PathId, movie.Loop, timing, hash);
        }
        finally { Directory.Delete(work, true); }
    }

    public static async Task<byte[]> PreviewAsync(string bundlePath, string name, bool animated, CancellationToken token)
    {
        string cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JixModMaker", "skill-movies");
        Directory.CreateDirectory(cache);
        string key = Hash(bundlePath) + "-" + Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(name)));
        string output = Path.Combine(cache, key + (animated ? ".gif" : ".png"));
        if (File.Exists(output)) return await File.ReadAllBytesAsync(output, token);
        string work = Path.Combine(Path.GetTempPath(), "JixMoviePreview-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            var movie = await Task.Run(() => Read(bundlePath, name), token);
            string source = Path.Combine(work, "native.usm");
            await File.WriteAllBytesAsync(source, movie.Bytes, token);
            string rendered = Path.Combine(work, animated ? "preview.gif" : "preview.png");
            string filter = "[0:v:1]extractplanes=y[alpha];[0:v:0][alpha]alphamerge," +
                (animated ? "fps=15," : "thumbnail=30,") +
                "scale=640:640:force_original_aspect_ratio=decrease:flags=lanczos,setsar=1";
            var arguments = new List<string> { "-nostdin", "-v", "error", "-y", "-i", source, "-an" };
            if (animated)
                filter += ",split[p][q];[p]palettegen=reserve_transparent=1[pal];[q][pal]paletteuse=alpha_threshold=128";
            arguments.AddRange(new[] { "-filter_complex", filter });
            arguments.AddRange(animated ? new[] { "-t", "6", "-loop", "0" } : new[] { "-frames:v", "1" });
            arguments.Add(rendered);
            await PortraitVideoConverter.RunAsync(PortraitVideoSettings.Load().Ffmpeg, arguments, token);
            var bytes = await File.ReadAllBytesAsync(rendered, token);
            File.Move(rendered, output, true);
            return bytes;
        }
        finally { Directory.Delete(work, true); }
    }

    public static void Rebuild(string source, string destination, string name, byte[] usm)
    {
        if (!usm.AsSpan().StartsWith("CRID"u8)) throw new InvalidDataException("缺少 CRI 视频头。");
        var manager = Manager();
        try
        {
            var bundle = manager.LoadBundleFile(source);
            var file = manager.LoadAssetsFileFromBundle(bundle, 0, false);
            manager.LoadClassDatabaseFromPackage(file.file.Metadata.UnityVersion);
            var movie = Find(manager, file, name);
            byte[] original = ObjectBytes(file.file, movie.Info);
            int offset = original.AsSpan().IndexOf(movie.Bytes);
            if (offset < 4 || original.AsSpan(offset + 1).IndexOf(movie.Bytes) >= 0)
                throw new InvalidDataException("无法唯一定位技能视频。");
            using (var reader = new AssetsFileReader(new MemoryStream(original, false)) { BigEndian = file.file.Reader.BigEndian })
            {
                reader.Position = offset - 4;
                if (reader.ReadInt32() != movie.Bytes.Length) throw new InvalidDataException("视频长度字段不匹配。");
            }
            int end = checked((offset + movie.Bytes.Length + 3) & ~3);
            if (end > original.Length) throw new InvalidDataException("视频超出对象边界。");
            using var payload = new MemoryStream();
            using (var writer = new AssetsFileWriter(payload) { BigEndian = file.file.Reader.BigEndian })
            {
                // Keep native names, references, movieInfo and playback flags byte-identical.
                writer.Write(original.AsSpan(0, offset - 4).ToArray());
                writer.Write(usm.Length);
                writer.Write(usm);
                writer.Align();
                writer.Write(original.AsSpan(end).ToArray());
            }
            using var rebuilt = new MemoryStream();
            using (var writer = new AssetsFileWriter(rebuilt))
                file.file.Write(writer, 0, new List<AssetsReplacer>
                {
                    new AssetsReplacerFromMemory(movie.Info.PathId, movie.Info.TypeId, file.file.GetScriptIndex(movie.Info), payload.ToArray())
                }, null);
            using (var writer = new AssetsFileWriter(destination))
                bundle.file.Write(writer, new List<BundleReplacer>
                {
                    new BundleReplacerFromMemory(file.name, file.name, true, rebuilt.ToArray(), -1)
                });
        }
        finally { manager.UnloadAll(); }
        Verify(source, destination, name, usm);
    }

    public static void Verify(string before, string after, string name, byte[] usm)
    {
        AnimatedPortraitPatch.VerifyTypeLayout(before, after);
        var oldMovie = Read(before, name);
        var newMovie = Read(after, name);
        if (oldMovie.PathId != newMovie.PathId || oldMovie.Loop != newMovie.Loop || !newMovie.Bytes.AsSpan().SequenceEqual(usm))
            throw new InvalidDataException("技能视频回读校验失败。");
        var manager = Manager();
        try
        {
            var left = manager.LoadAssetsFileFromBundle(manager.LoadBundleFile(before), 0, false).file;
            var right = manager.LoadAssetsFileFromBundle(manager.LoadBundleFile(after), 0, false).file;
            foreach (var item in left.AssetInfos.Where(item => item.PathId != oldMovie.PathId))
                if (!ObjectBytes(left, item).AsSpan().SequenceEqual(ObjectBytes(right, right.GetAssetInfo(item.PathId))))
                    throw new InvalidDataException("替换意外改变了其他资源，已阻止写入。");
            byte[] original = ObjectBytes(left, left.GetAssetInfo(oldMovie.PathId));
            byte[] updated = ObjectBytes(right, right.GetAssetInfo(newMovie.PathId));
            int oldOffset = original.AsSpan().IndexOf(oldMovie.Bytes);
            int newOffset = updated.AsSpan().IndexOf(usm);
            int oldEnd = (oldOffset + oldMovie.Bytes.Length + 3) & ~3;
            int newEnd = (newOffset + usm.Length + 3) & ~3;
            if (oldOffset < 4 || newOffset != oldOffset ||
                !original.AsSpan(0, oldOffset - 4).SequenceEqual(updated.AsSpan(0, newOffset - 4)) ||
                !original.AsSpan(oldEnd).SequenceEqual(updated.AsSpan(newEnd)))
                throw new InvalidDataException("原生技能的序列化元数据发生变化，已阻止写入。");
        }
        finally { manager.UnloadAll(); }
    }

    public static bool Restore(TexRef asset, SkillMovieInfo info, string backupDir)
    {
        string backup = ResourceLocator.BackupPath(asset.BundlePath, backupDir, asset.BundleName);
        if (!File.Exists(backup)) return false;
        var plan = BundleRestore.Prepare(asset.BundlePath, backupDir, asset.BundleName);
        if (plan.CurrentHash != info.BundleHash) throw new IOException("当前资源已更新，未覆盖。");
        Read(plan.Backup, asset.Name);
        AnimatedPortraitPatch.VerifyTypeLayout(plan.Target, plan.Backup);
        BundleRestore.Apply(plan);
        return true;
    }

    private static byte[] ObjectBytes(AssetsFile file, AssetFileInfo info)
    {
        file.Reader.Position = info.GetAbsoluteByteStart(file);
        return file.Reader.ReadBytes(checked((int)info.ByteSize));
    }

    public static async Task ReplaceAsync(TexRef asset, SkillMovieInfo info, string input, string backupDir,
        string work, PortraitVideoConverter.Options options, CancellationToken token, IProgress<string> progress = null)
    {
        PortraitReplacement.EnsureGameClosed();
        Directory.CreateDirectory(work);
        if (Hash(asset.BundlePath) != info.BundleHash) throw new IOException("资源包已更新，请重新打开替换窗口。");
        string before = Path.Combine(work, "before.bundle");
        File.Copy(asset.BundlePath, before, false);
        if (Hash(before) != info.BundleHash) throw new IOException("备份期间资源已变化，未写入。");
        string usmPath = await PortraitVideoConverter.ConvertAsync(input, work, PortraitVideoSettings.Load(), options,
            token, progress, info.Timing);
        byte[] usm = await File.ReadAllBytesAsync(usmPath, token);
        var metadata = PortraitMovieMetadata.Read(usmPath);
        metadata.Validate(usm);
        if (metadata.Width != info.Timing.Width || metadata.Height != info.Timing.Height ||
            metadata.TotalFrames != info.Timing.TotalFrames ||
            metadata.FramerateN * (long)info.Timing.FramerateD != info.Timing.FramerateN * (long)metadata.FramerateD)
            throw new InvalidDataException("转换后的视频与原生技能的尺寸、时长不一致，未写入。");
        string stage = asset.BundlePath + ".jix-" + Guid.NewGuid().ToString("N");
        string rollback = stage + ".rollback";
        bool installed = false;
        string installedHash = null;
        try
        {
            progress?.Report("正在封装技能特写并校验未修改的资源...");
            await Task.Run(() => Rebuild(before, stage, asset.Name, usm), token);
            token.ThrowIfCancellationRequested();
            PortraitReplacement.EnsureGameClosed();
            if (Hash(asset.BundlePath) != info.BundleHash) throw new IOException("资源包已被更新或修改，未覆盖。");
            string backup = ResourceLocator.BackupPath(asset.BundlePath, backupDir, asset.BundleName);
            Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
            if (!File.Exists(backup)) File.Copy(before, backup, false);
            installedHash = Hash(stage);
            File.Replace(stage, asset.BundlePath, rollback);
            installed = true;
            Verify(before, asset.BundlePath, asset.Name, usm);
        }
        catch
        {
            if (installed && Hash(asset.BundlePath) == installedHash) File.Replace(rollback, asset.BundlePath, null);
            throw;
        }
        finally
        {
            if (File.Exists(stage)) File.Delete(stage);
            // Retain the immediate rollback copy if the final file changed concurrently.
            if (File.Exists(rollback) && Hash(asset.BundlePath) == installedHash) File.Delete(rollback);
        }
    }
}
