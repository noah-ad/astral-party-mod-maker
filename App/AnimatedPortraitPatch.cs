using AssetsTools.NET;
using AssetsTools.NET.Extra;
using Mono.Cecil;
using System.Text;

namespace JixModMaker;

// Export-only: the existing alternate-art bundles and Addressables catalog are never changed.
public static class AnimatedPortraitPatch
{
    public static byte[] PatchAssembly(byte[] original, string texture, byte[] usm, PortraitMovieMetadata metadata,
        IReadOnlyDictionary<string, byte[]> dependencies = null)
    {
        if (string.IsNullOrWhiteSpace(texture)) throw new ArgumentException("立绘名称不能为空");
        metadata.Validate(usm);
        using var input = new MemoryStream(original, false);
        using var resolver = new BundleAssemblyResolver(dependencies);
        using var module = ModuleDefinition.ReadModule(input, new ReaderParameters { AssemblyResolver = resolver });
        if (module.Assembly.Name.HasPublicKey) throw new InvalidOperationException("不支持修改带签名的程序集");
        IndependentPortraitAssembly.Patch(module, texture, usm, metadata);
        using var output = new MemoryStream();
        module.Write(output);
        return output.ToArray();
    }

    public sealed record Request(string Root, string RuntimeBundle, string TextureName, string UsmFile, string Destination);
    public sealed record Result(string ReplacementZip, string RestoreZip, string VideoKey, string Platform);

    public static string FindRuntime(string root, CancellationToken cancellationToken, IProgress<int> progress = null)
    {
        var manager = Manager();
        int scanned = 0;
        try
        {
            var files = ResourceLocator.EnumerateResourceFiles(root)
                .Select(p => new FileInfo(p)).OrderByDescending(f => f.Length >= 4_000_000)
                .ThenByDescending(f => f.LastWriteTimeUtc);
            foreach (var path in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var bundle = manager.LoadBundleFile(path.FullName);
                    var file = manager.LoadAssetsFileFromBundle(bundle, 0, false);
                    if (!file.file.Metadata.UnityVersion.StartsWith("2021.", StringComparison.Ordinal)) continue;
                    foreach (var info in file.file.GetAssetsOfType(AssetClassID.TextAsset))
                    {
                        var reader = file.file.Reader;
                        reader.Position = info.GetAbsoluteByteStart(file.file);
                        int size = reader.ReadInt32();
                        if (size < 0 || size > 256 || size + 4 > info.ByteSize) continue;
                        if (Encoding.UTF8.GetString(reader.ReadBytes(size)) == "AstralParty.Runtime.dll") return path.FullName;
                    }
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException) { }
                finally { manager.UnloadAll(); }
                if (++scanned % 25 == 0) progress?.Report(scanned);
            }
            return null;
        }
        finally { manager.UnloadAll(); }
    }

    public static Result Export(Request request)
    {
        string root = Path.GetFullPath(request.Root);
        string relative = Path.GetRelativePath(root, Path.GetFullPath(request.RuntimeBundle));
        if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar))
            throw new InvalidOperationException("程序集资源包必须位于目标平台资源根目录内");
        string destination = Path.GetFullPath(request.Destination);
        string restore = Path.Combine(Path.GetDirectoryName(destination)!, Path.GetFileNameWithoutExtension(destination) + ".restore.zip");
        if (File.Exists(destination) || File.Exists(restore)) throw new IOException("输出或恢复 ZIP 已存在，请选择新文件名");
        if (new FileInfo(request.UsmFile).Length > 256L * 1024 * 1024) throw new InvalidDataException("暂不接受大于 256 MB 的单段视频");
        byte[] usm = File.ReadAllBytes(request.UsmFile);
        if (usm.Length < 32 || Encoding.ASCII.GetString(usm, 0, 4) != "CRID")
            throw new InvalidDataException("需要真正的 USM 视频，不能将 GIF / MP4 改名后导入");
        var metadata = PortraitMovieMetadata.Read(request.UsmFile);
        metadata.Validate(usm);
        string temporary = Path.Combine(Path.GetTempPath(), "JixAnimatedPortrait-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            string runtimeOut = Path.Combine(temporary, relative);
            byte[] expectedAssembly = null;
            var platform = Rewrite(request.RuntimeBundle, runtimeOut, (manager, file) =>
            {
                if (file.file.Metadata.TypeTreeEnabled && file.file.Metadata.TypeTreeTypes.Any(t => t.TypeId == (int)AssetClassID.TextAsset && t.Nodes.Count == 0))
                    throw new InvalidDataException("源程序集包含旧版损坏类型表，请使用游戏重置后的干净资源制作。");
                var matches = file.file.GetAssetsOfType(AssetClassID.TextAsset)
                    .Where(i => manager.GetBaseField(file, i)["m_Name"].AsString == "AstralParty.Runtime.dll").ToArray();
                if (matches.Length != 1) throw new InvalidDataException("所选资源包不包含唯一的 AstralParty.Runtime.dll");
                var info = matches[0];
                var reader = file.file.Reader;
                reader.Position = info.GetAbsoluteByteStart(file.file);
                reader.ReadCountStringInt32();
                reader.Align();
                long contentStart = reader.Position;
                byte[] original = reader.ReadBytes(reader.ReadInt32());
                var dependencies = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
                foreach (var dependency in file.file.GetAssetsOfType(AssetClassID.TextAsset))
                {
                    reader.Position = dependency.GetAbsoluteByteStart(file.file);
                    string name = reader.ReadCountStringInt32();
                    reader.Align();
                    int size = reader.ReadInt32();
                    if (name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) dependencies[Path.GetFileNameWithoutExtension(name)] = reader.ReadBytes(size);
                }
                byte[] patched = PatchAssembly(original, request.TextureName, usm, metadata, dependencies);
                expectedAssembly = patched;
                reader.Position = info.GetAbsoluteByteStart(file.file);
                byte[] prefix = reader.ReadBytes(checked((int)(contentStart - reader.Position)));
                using var stream = new MemoryStream();
                using (var writer = new AssetsFileWriter(stream))
                {
                    writer.BigEndian = reader.BigEndian;
                    writer.Write(prefix);
                    writer.Write(patched.Length);
                    writer.Write(patched);
                    writer.Align();
                }
                return new AssetsReplacerFromMemory(info.PathId, info.TypeId, file.file.GetScriptIndex(info), stream.ToArray());
            });
            if (platform != "13" && platform != "19") throw new InvalidOperationException("实验补丁仅接受 Android 或 Windows 64 位资源");
            VerifyRebuiltBundle(runtimeOut, expectedAssembly);
            VerifyTypeLayout(request.RuntimeBundle, runtimeOut);
            ReplacementZip.Export(root, new[] { request.RuntimeBundle }, restore);
            try { ReplacementZip.Export(temporary, new[] { runtimeOut }, destination); }
            catch { File.Delete(restore); throw; }
            return new Result(destination, restore, IndependentPortraitAssembly.Key(request.TextureName), platform == "13" ? "Android" : "Windows 64 位");
        }
        finally { Directory.Delete(temporary, true); }
    }

    private sealed class BundleAssemblyResolver : DefaultAssemblyResolver
    {
        private readonly IReadOnlyDictionary<string, byte[]> _dependencies;
        private readonly Dictionary<string, AssemblyDefinition> _loaded = new(StringComparer.OrdinalIgnoreCase);
        public BundleAssemblyResolver(IReadOnlyDictionary<string, byte[]> dependencies) => _dependencies = dependencies;
        public override AssemblyDefinition Resolve(AssemblyNameReference name)
        {
            if (_loaded.TryGetValue(name.Name, out var existing)) return existing;
            if (_dependencies != null && _dependencies.TryGetValue(name.Name, out var bytes))
            {
                var assembly = AssemblyDefinition.ReadAssembly(new MemoryStream(bytes, false), new ReaderParameters { AssemblyResolver = this });
                _loaded[name.Name] = assembly;
                return assembly;
            }
            return base.Resolve(name);
        }
        protected override void Dispose(bool disposing)
        {
            foreach (var assembly in _loaded.Values) assembly.Dispose();
            base.Dispose(disposing);
        }
    }

    private static void VerifyRebuiltBundle(string runtimePath, byte[] assembly)
    {
        var manager = Manager();
        try
        {
            var file = manager.LoadAssetsFileFromBundle(manager.LoadBundleFile(runtimePath), 0, false);
            manager.LoadClassDatabaseFromPackage(file.file.Metadata.UnityVersion);
            var info = file.file.GetAssetsOfType(AssetClassID.TextAsset).Single(i => manager.GetBaseField(file, i)["m_Name"].AsString == "AstralParty.Runtime.dll");
            var reader = file.file.Reader;
            reader.Position = info.GetAbsoluteByteStart(file.file);
            reader.ReadCountStringInt32();
            reader.Align();
            var length = reader.ReadInt32();
            if (length != assembly.Length || !reader.ReadBytes(length).AsSpan().SequenceEqual(assembly)) throw new InvalidDataException("程序集封包回读不一致");
        }
        finally { manager.UnloadAll(); }
    }

    private static AssetsManager Manager()
    {
        var manager = new AssetsManager();
        manager.LoadClassPackage(Path.Combine(AppContext.BaseDirectory, "classdata.tpk"));
        return manager;
    }

    public static void VerifyTypeLayout(string original, string rebuilt)
    {
        var manager = Manager();
        try
        {
            var before = manager.LoadAssetsFileFromBundle(manager.LoadBundleFile(original), 0, false).file;
            var after = manager.LoadAssetsFileFromBundle(manager.LoadBundleFile(rebuilt), 0, false).file;
            byte[] TypeBytes(AssetsFile file)
            {
                using var buffer = new MemoryStream();
                using var writer = new AssetsFileWriter(buffer);
                foreach (var type in file.Metadata.TypeTreeTypes.Concat(file.Metadata.RefTypes)) type.Write(writer, file.Header.Version, file.Metadata.TypeTreeEnabled);
                return buffer.ToArray();
            }
            if (before.Header.Version != after.Header.Version || before.Metadata.UnityVersion != after.Metadata.UnityVersion ||
                before.Metadata.TargetPlatform != after.Metadata.TargetPlatform || before.Metadata.TypeTreeEnabled != after.Metadata.TypeTreeEnabled ||
                before.Metadata.TypeTreeTypes.Count != after.Metadata.TypeTreeTypes.Count || before.Metadata.RefTypes.Count != after.Metadata.RefTypes.Count ||
                !TypeBytes(before).AsSpan().SequenceEqual(TypeBytes(after)) ||
                !before.AssetInfos.Select(i => (i.PathId, i.TypeIdOrIndex)).SequenceEqual(after.AssetInfos.Select(i => (i.PathId, i.TypeIdOrIndex))))
                throw new InvalidDataException("封包类型结构发生变化，已阻止写入游戏。");
        }
        finally { manager.UnloadAll(); }
    }

    private static string Rewrite(string source, string destination, Func<AssetsManager, AssetsFileInstance, AssetsReplacer> change)
    {
        var manager = Manager();
        try
        {
            var bundle = manager.LoadBundleFile(source);
            var file = manager.LoadAssetsFileFromBundle(bundle, 0, false);
            manager.LoadClassDatabaseFromPackage(file.file.Metadata.UnityVersion);
            var replacement = change(manager, file);
            byte[] bytes;
            using (var buffer = new MemoryStream())
            {
                using var writer = new AssetsFileWriter(buffer);
                file.file.Write(writer, 0, new List<AssetsReplacer> { replacement }, null);
                bytes = buffer.ToArray();
            }
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            using (var writer = new AssetsFileWriter(destination))
                bundle.file.Write(writer, new List<BundleReplacer> { new BundleReplacerFromMemory(file.name, file.name, true, bytes, -1) });
            string infoPath = Path.Combine(Path.GetDirectoryName(source)!, "__info");
            if (Path.GetFileName(source) == "__data" && File.Exists(infoPath)) File.Copy(infoPath, Path.Combine(Path.GetDirectoryName(destination)!, "__info"));
            return file.file.Metadata.TargetPlatform.ToString();
        }
        finally { manager.UnloadAll(); }
    }
}
