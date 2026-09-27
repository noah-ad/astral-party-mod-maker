using AssetsTools.NET;
using AssetsTools.NET.Extra;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace JixModMaker;

public static class IndependentVideoReplacement
{
    public static void VerifyBaseline(PortraitReplacement.Receipt receipt)
    {
        if (receipt.BaselineHashes == null || receipt.BaselineHashes.Count != 1)
            throw new InvalidDataException("缺少首次备份校验记录，已停止操作。");
        using var zip = ZipFile.OpenRead(receipt.BaselineRestoreZip);
        foreach (var pair in receipt.BaselineHashes)
        {
            var entry = zip.GetEntry(pair.Key) ?? throw new InvalidDataException("首次备份缺少程序集。");
            using var stream = entry.Open();
            if (Convert.ToHexString(SHA256.HashData(stream)) != pair.Value)
                throw new IOException("首次备份已被修改，已停止操作以免覆盖游戏资源。");
        }
    }

    public static PortraitReplacement.Receipt Apply(AnimatedPortraitPatch.Request request, string preview = null,
        string sourceName = "", bool removeGreen = false)
    {
        PortraitReplacement.EnsureGameClosed();
        var previous = PortraitReplacement.ReadReceipt(request.Root);
        if (previous != null && !PortraitReplacement.IsInstalled(request.Root, previous))
        {
            if (previous.Mode == IndependentVideoPatch.Mode && IndependentVideoTransaction.Matches(request.Root, previous.BaselineRestoreZip))
                previous = null;
            else throw new IOException("资源与上次记录不同，请先确认游戏更新或其他 Mod，不能继续沿用旧备份。");
        }
        if (previous != null && previous.Mode != IndependentVideoPatch.Mode)
            throw new IOException("当前仍有旧槽位动态替换，请先点“恢复原资源”，再安装独立视频。");
        string backup = Path.Combine(PortraitReplacement.HistoryRoot(request.Root), DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(backup);
        var receipt = Build(request, previous, backup, preview, sourceName, removeGreen);
        string stagedState = Path.Combine(backup, "receipt.json");
        File.WriteAllText(stagedState, JsonSerializer.Serialize(receipt));
        PortraitReplacement.EnsureGameClosed();
        IndependentVideoTransaction.Apply(request.Root, receipt.ReplacementZip, receipt.RestoreZip);
        try
        {
            if (!PortraitReplacement.IsInstalled(request.Root, receipt)) throw new IOException("独立视频写入校验失败。");
            string state = PortraitReplacement.StatePath(request.Root);
            string temporaryState = state + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.Copy(stagedState, temporaryState);
                File.Move(temporaryState, state, true);
            }
            finally { if (File.Exists(temporaryState)) File.Delete(temporaryState); }
        }
        catch (Exception failure)
        {
            try { IndependentVideoTransaction.Apply(request.Root, receipt.RestoreZip, receipt.ReplacementZip); }
            catch (Exception rollback) { throw new AggregateException("写入校验与回滚失败，请保留：" + backup, failure, rollback); }
            throw;
        }
        return receipt;
    }

    // Build against a clean saved DLL, never patch a patch or copy a previous game version over a new one.
    public static PortraitReplacement.Receipt Build(AnimatedPortraitPatch.Request request, PortraitReplacement.Receipt previous,
        string destinationDirectory, string preview = null, string sourceName = "", bool removeGreen = false)
    {
        string root = Path.GetFullPath(request.Root);
        string relativeRuntime = Path.GetRelativePath(root, Path.GetFullPath(request.RuntimeBundle)).Replace('\\', '/');
        IndependentVideoTransaction.TargetPath(root, relativeRuntime);
        string[] parts = relativeRuntime.Split('/');
        if (parts.Length != 3 || parts[2] != "__data" || parts.Take(2).Any(p => p.Length != 32 || !p.All(Uri.IsHexDigit)))
            throw new InvalidDataException("独立视频需要游戏的 AssetBundles 热更新缓存目录，不能安装到 StreamingAssets 或任意图片目录。");
        string key = IndependentVideoPatch.VideoKey(request.TextureName);
        if (new FileInfo(request.UsmFile).Length > 256L * 1024 * 1024) throw new InvalidDataException("单段视频不能超过 256 MB。");
        var usm = File.ReadAllBytes(request.UsmFile);
        PortraitMovieMetadata.Read(request.UsmFile).Validate(usm);
        if (previous != null && (previous.Mode != IndependentVideoPatch.Mode || !PortraitReplacement.IsInstalled(root, previous)))
            throw new IOException("旧替换状态不匹配，已停止生成独立视频。");
        Directory.CreateDirectory(destinationDirectory);
        string work = Path.Combine(destinationDirectory, "build");
        Directory.CreateDirectory(work);
        try
        {
            string currentRuntime = Path.Combine(work, "current-runtime");
            File.Copy(request.RuntimeBundle, currentRuntime);
            string originalRuntime = currentRuntime;
            if (previous != null && (!previous.Hashes.TryGetValue(relativeRuntime, out string runtimeHash) ||
                    Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(currentRuntime))) != runtimeHash))
                throw new IOException("程序集在准备期间发生变化，已停止生成。");
            if (previous != null)
            {
                VerifyBaseline(previous);
                using var zip = ZipFile.OpenRead(previous.BaselineRestoreZip);
                originalRuntime = Path.Combine(work, "original-runtime");
                (zip.GetEntry(relativeRuntime) ?? throw new InvalidDataException("恢复包缺少原程序集")).ExtractToFile(originalRuntime);
            }
            var entries = previous?.Portraits != null
                ? new Dictionary<string, PortraitReplacement.PortraitEntry>(previous.Portraits, StringComparer.Ordinal)
                : new Dictionary<string, PortraitReplacement.PortraitEntry>(StringComparer.Ordinal);
            string savedPreview = null;
            if (preview != null)
            {
                savedPreview = Path.Combine(destinationDirectory, "preview.gif");
                File.Copy(preview, savedPreview);
            }
            var now = DateTimeOffset.Now;
            entries[request.TextureName] = new(key, sourceName, removeGreen, now, savedPreview);
            string rebuiltRuntime = Path.Combine(work, "patched-runtime");
            string platform = RewriteRuntime(originalRuntime, rebuiltRuntime, entries.Keys.ToArray());
            if (platform is not "13" and not "19") throw new InvalidDataException("独立视频仅支持 Android / Windows 64 位资源。");

            string stagedUsm = Path.Combine(work, "input.usm");
            File.WriteAllBytes(stagedUsm, usm);
            var beforeFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [relativeRuntime] = currentRuntime };
            var afterFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [relativeRuntime] = rebuiltRuntime };
            var baselineFiles = new Dictionary<string, string> { [relativeRuntime] = originalRuntime };
            string info = Path.Combine(Path.GetDirectoryName(request.RuntimeBundle)!, "__info");
            if (File.Exists(info))
            {
                string infoCopy = Path.Combine(work, "__info");
                File.Copy(info, infoCopy);
                string infoRelative = relativeRuntime[..^"__data".Length] + "__info";
                beforeFiles.Add(infoRelative, infoCopy);
                afterFiles.Add(infoRelative, infoCopy);
                baselineFiles.Add(infoRelative, infoCopy);
            }
            var added = new List<string>();
            foreach (string texture in entries.Keys)
            {
                string relative = IndependentVideoPatch.RelativeFile(texture);
                string path = IndependentVideoTransaction.TargetPath(root, relative);
                bool existed = previous?.Portraits?.ContainsKey(texture) == true;
                string saved = null;
                if (existed)
                {
                    saved = Path.Combine(work, IndependentVideoPatch.VideoKey(texture) + ".usm");
                    File.Copy(path, saved);
                    if (!previous.Hashes.TryGetValue(relative, out string hash) || Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(saved))) != hash)
                        throw new IOException("独立视频在准备期间发生变化，已停止生成。");
                    beforeFiles.Add(relative, saved);
                }
                else
                {
                    if (File.Exists(path) || Directory.Exists(path)) throw new IOException("独立视频路径已被其他文件占用：" + relative);
                    added.Add(relative);
                }
                afterFiles.Add(relative, texture == request.TextureName ? stagedUsm : saved);
            }
            string replacement = Path.Combine(destinationDirectory, "replacement.zip");
            string restore = Path.Combine(destinationDirectory, "replacement.restore.zip");
            string baseline = Path.Combine(destinationDirectory, "baseline.restore.zip");
            IndependentVideoTransaction.Write(restore, beforeFiles, added);
            IndependentVideoTransaction.Write(baseline, baselineFiles, entries.Keys.Select(IndependentVideoPatch.RelativeFile));
            IndependentVideoTransaction.Write(replacement, afterFiles, Array.Empty<string>());
            using var installedZip = ZipFile.OpenRead(replacement);
            var hashes = installedZip.Entries.Where(entry => Path.GetFileName(entry.FullName) != "__info").ToDictionary(entry => entry.FullName, entry =>
            {
                using var stream = entry.Open();
                return Convert.ToHexString(SHA256.HashData(stream));
            }, StringComparer.OrdinalIgnoreCase);
            return new(request.TextureName, key, sourceName, removeGreen, now, savedPreview, replacement, restore, baseline,
                hashes, entries, IndependentVideoPatch.Mode,
                new Dictionary<string, string> { [relativeRuntime] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(originalRuntime))) });
        }
        finally { Directory.Delete(work, true); }
    }

    private static string RewriteRuntime(string source, string destination, string[] targets)
    {
        byte[] expected = null;
        string platform = AnimatedPortraitPatch.Rewrite(source, destination, (manager, file) =>
        {
            AnimatedPortraitPatch.RejectMalformedTypeTree(file.file);
            var dependencies = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            AssetFileInfo runtime = null;
            byte[] prefix = null;
            var reader = file.file.Reader;
            foreach (var info in file.file.GetAssetsOfType(AssetClassID.TextAsset))
            {
                reader.Position = info.GetAbsoluteByteStart(file.file);
                string name = reader.ReadCountStringInt32();
                reader.Align();
                long start = reader.Position;
                int size = reader.ReadInt32();
                if (name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) dependencies.Add(Path.GetFileNameWithoutExtension(name), reader.ReadBytes(size));
                if (name != "AstralParty.Runtime.dll") continue;
                if (runtime != null) throw new InvalidDataException("程序集资源不唯一。");
                runtime = info;
                reader.Position = info.GetAbsoluteByteStart(file.file);
                prefix = reader.ReadBytes(checked((int)(start - reader.Position)));
            }
            if (runtime == null) throw new InvalidDataException("未找到 AstralParty.Runtime.dll。");
            expected = IndependentVideoPatch.PatchAssembly(dependencies["AstralParty.Runtime"], targets, dependencies);
            using var stream = new MemoryStream();
            using (var writer = new AssetsFileWriter(stream) { BigEndian = reader.BigEndian })
            {
                writer.Write(prefix);
                writer.Write(expected.Length);
                writer.Write(expected);
                writer.Align();
            }
            return new AssetsReplacerFromMemory(runtime.PathId, runtime.TypeId, file.file.GetScriptIndex(runtime), stream.ToArray());
        });
        AnimatedPortraitPatch.VerifyTypeLayout(source, destination);
        var manager = AnimatedPortraitPatch.Manager();
        try
        {
            var file = manager.LoadAssetsFileFromBundle(manager.LoadBundleFile(destination), 0, false);
            manager.LoadClassDatabaseFromPackage(file.file.Metadata.UnityVersion);
            var info = file.file.GetAssetsOfType(AssetClassID.TextAsset).Single(i => manager.GetBaseField(file, i)["m_Name"].AsString == "AstralParty.Runtime.dll");
            var reader = file.file.Reader;
            reader.Position = info.GetAbsoluteByteStart(file.file);
            reader.ReadCountStringInt32();
            reader.Align();
            if (!reader.ReadBytes(reader.ReadInt32()).AsSpan().SequenceEqual(expected)) throw new InvalidDataException("独立视频程序集封包回读失败。");
        }
        finally { manager.UnloadAll(); }
        return platform;
    }
}
