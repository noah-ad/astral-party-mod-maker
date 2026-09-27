using System.IO.Compression;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using JixModMaker;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class IndependentVideoChecks
{
    public static void Run(Action<bool, string> check)
    {
        string root = Path.Combine(Path.GetTempPath(), "JixIndependent-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            byte[] original = File.ReadAllBytes(typeof(SkinStandingPaintingConfigureItem).Assembly.Location);
            byte[] patched = IndependentVideoPatch.PatchAssembly(original, new[] { "UT_Hero_Card_101", "UT_Hero_Card_102", "UT_HandCard_21001", "UT_Event_1001" });
            var context = new AssemblyLoadContext("independent-fixture-" + Guid.NewGuid(), true);
            try
            {
                var assembly = context.LoadFromStream(new MemoryStream(patched));
                assembly.GetType("UnityEngine.Application")!.GetProperty("persistentDataPath")!.SetValue(null, root);
                var configType = assembly.GetType("SkinStandingPaintingConfigureItem")!;
                var config = Activator.CreateInstance(configType);
                (string, bool) Portrait() => ((string, bool))configType.GetMethod("GetCharacter")!.Invoke(config, null)!;
                check(Portrait() == ("UT_Hero_Card_101", false), "independent missing file falls back to static portrait");
                string videoFolder = root + IndependentVideoPatch.PersistentFolder;
                Directory.CreateDirectory(videoFolder);
                File.WriteAllBytes(Path.Combine(videoFolder, IndependentVideoPatch.VideoKey("UT_Hero_Card_101") + ".usm"), new byte[] { 1 });
                check(Portrait() == (IndependentVideoPatch.VideoKey("UT_Hero_Card_101"), true), "independent portrait selects its own video key");
                configType.GetProperty("Character")!.SetValue(config, "UT_Hero_Card_102");
                File.WriteAllBytes(Path.Combine(videoFolder, IndependentVideoPatch.VideoKey("UT_Hero_Card_102") + ".usm"), new byte[] { 2 });
                check(Portrait() == (IndependentVideoPatch.VideoKey("UT_Hero_Card_102"), true), "second independent portrait has a distinct video");
                File.WriteAllText(Path.Combine(videoFolder, "disabled"), "");
                check(Portrait() == ("UT_Hero_Card_102", false), "independent kill switch retains original static artwork");
                File.Delete(Path.Combine(videoFolder, "disabled"));
                configType.GetProperty("SafeMode")!.SetValue(config, true);
                check(Portrait() == ("UT_Hero_Card_101_sfw", false), "independent mapping leaves unmatched safe-mode image intact");

                var managerType = assembly.GetType("UI.CriMovieManager")!;
                var manager = Activator.CreateInstance(managerType);
                object InvokeLoad(string key) => managerType.GetMethod("Load")!.Invoke(manager, new object[] { key })!;
                var completed = InvokeLoad(IndependentVideoPatch.VideoKey("UT_Hero_Card_101"));
                check((bool)completed.GetType().GetProperty("IsCompleted")!.GetValue(completed)! && (int)managerType.GetField("OfficialLoads")!.GetValue(manager)! == 0,
                    "independent key bypasses Addressables with an immediately completed result");
                InvokeLoad(AnimatedPortraitPatch.VideoSlot);
                InvokeLoad("JixVideo_unknown");
                check((int)managerType.GetField("OfficialLoads")!.GetValue(manager)! == 2, "official and unknown video keys use the unmodified loader");

                var controllerType = assembly.GetType("UI.CriManaMovieControllerForGGraph")!;
                var controller = Activator.CreateInstance(controllerType);
                controllerType.GetMethod("SetAsset")!.Invoke(controller, new object[] { IndependentVideoPatch.VideoKey("UT_Hero_Card_101"), null });
                var player = controllerType.GetProperty("player")!.GetValue(controller)!;
                check((string)player.GetType().GetField("File")!.GetValue(player)! == videoFolder + IndependentVideoPatch.VideoKey("UT_Hero_Card_101") + ".usm",
                    "controller initializes player and reads standalone USM via SetFile");
                check((bool)player.GetType().GetField("IsLooping")!.GetValue(player)!, "independent video loops");
                controllerType.GetMethod("SetAsset")!.Invoke(controller, new object[] { AnimatedPortraitPatch.VideoSlot, null });
                check((int)assembly.GetType("CriWare.Assets.CriManaPlayerExtentionForAsset")!.GetField("OfficialSets")!.GetValue(null)! == 1,
                    "official video still uses native asset setter after independent playback");
                Cards(assembly, videoFolder, check);
            }
            finally { context.Unload(); }
            try { IndependentVideoPatch.PatchAssembly(patched, new[] { "UT_Hero_Card_101" }); throw new Exception("accepted a patched source"); }
            catch (InvalidOperationException) { check(true, "independent build refuses stacking patches"); }
            try { IndependentVideoPatch.VideoKey("UT_Hero_Card_101/../../outside"); throw new Exception("accepted unsafe target"); }
            catch (ArgumentException) { check(true, "independent filenames reject path injection"); }
            Transactions(root, check);
        }
        finally { Directory.Delete(root, true); }
    }

    private static void Cards(Assembly assembly, string folder, Action<bool, string> check)
    {
        var type = assembly.GetType("CardView")!;
        var viewType = assembly.GetType("UI.UICom_Card")!;
        var view = Activator.CreateInstance(viewType);
        var renderer = assembly.GetType("UI.CommonUIManager")!.GetMethod("RendererCardContent")!;
        foreach (string target in new[] { "UT_HandCard_21001", "UT_Event_1001" })
        {
            object card = Activator.CreateInstance(type)!;
            type.GetField("Key")!.SetValue(card, target);
            type.GetField("Name")!.SetValue(card, "untouched title");
            renderer.Invoke(null, new[] { view, card });
            check(ReferenceEquals(viewType.GetField("Rendered")!.GetValue(view), card), "missing independent card file remains static: " + target);
            File.WriteAllText(Path.Combine(folder, IndependentVideoPatch.VideoKey(target) + ".usm"), "file");
            renderer.Invoke(null, new[] { view, card });
            object rendered = viewType.GetField("Rendered")!.GetValue(view)!;
            check(!ReferenceEquals(card, rendered) && (string)type.GetField("Key")!.GetValue(rendered)! == IndependentVideoPatch.VideoKey(target) &&
                (bool)type.GetField("IsVideo")!.GetValue(rendered)!, "independent card gets its own cloned video binding: " + target);
            check(!(bool)type.GetField("IsVideo")!.GetValue(card)! && (string)type.GetField("Name")!.GetValue(rendered)! == "untouched title",
                "independent card mapping preserves original object and other fields: " + target);
            check((int)viewType.GetField("Layer")!.GetValue(view)! == 1, "independent event and hand card use front video layer: " + target);
            object graph = viewType.GetField("video_FrontCard")!.GetValue(view)!;
            var bridgeType = assembly.GetType("UI.CriManaMovieBridge")!;
            var bridge = Activator.CreateInstance(bridgeType);
            bridgeType.GetField("_videoKey")!.SetValue(bridge, "stale-key");
            bridgeType.GetMethod("AddVideoGraph")!.Invoke(bridge, new[] { graph });
            check(graph.GetType().GetField("VideoKey")!.GetValue(graph) == null, "independent card rejects stale graph callback: " + target);
            bridgeType.GetField("_videoKey")!.SetValue(bridge, IndependentVideoPatch.VideoKey(target));
            bridgeType.GetMethod("AddVideoGraph")!.Invoke(bridge, new[] { graph });
            check((string)graph.GetType().GetField("VideoKey")!.GetValue(graph)! == IndependentVideoPatch.VideoKey(target), "independent card attaches matching callback: " + target);
            var gear = graph.GetType().GetMethod("GetGear")!.Invoke(graph, new object[] { 8 })!;
            File.Delete(Path.Combine(folder, IndependentVideoPatch.VideoKey(target) + ".usm"));
            renderer.Invoke(null, new[] { view, card });
            check(((string[])gear.GetType().GetProperty("pages")!.GetValue(gear)!).SequenceEqual(new[] { "original" }),
                "returning to static card restores original visibility pages: " + target);
        }
    }

    private static void Transactions(string root, Action<bool, string> check)
    {
        string live = Path.Combine(root, "transaction");
        Directory.CreateDirectory(live);
        string runtime = Path.Combine(live, "runtime");
        File.WriteAllText(runtime, "original");
        string replacementRuntime = Path.Combine(root, "new-runtime");
        string sourceVideo = Path.Combine(root, "source-video");
        File.WriteAllText(replacementRuntime, "patched");
        File.WriteAllText(sourceVideo, "video");
        string video = IndependentVideoPatch.RelativeFile("UT_HandCard_21001");
        string after = Path.Combine(root, "after.zip"), before = Path.Combine(root, "before.zip");
        IndependentVideoTransaction.Write(before, new Dictionary<string, string> { ["runtime"] = runtime }, new[] { video });
        IndependentVideoTransaction.Write(after, new Dictionary<string, string> { ["runtime"] = replacementRuntime, [video] = sourceVideo }, Array.Empty<string>());
        IndependentVideoTransaction.Apply(live, after, before);
        check(IndependentVideoTransaction.Matches(live, after), "transaction creates independent file and replaces runtime");
        IndependentVideoTransaction.Apply(live, before, after);
        check(File.ReadAllText(runtime) == "original" && !File.Exists(Path.Combine(live, video)), "restore removes only recorded new video and restores exact runtime");
        string videoPath = Path.Combine(live, video);
        File.WriteAllText(videoPath, "other mod");
        try { IndependentVideoTransaction.Apply(live, after, before); throw new Exception("overwrote a collision"); }
        catch (IOException) { check(File.ReadAllText(runtime) == "original" && File.ReadAllText(videoPath) == "other mod", "new file collision fails before any write"); }
        File.Delete(videoPath);
        File.WriteAllText(runtime, "updated game");
        try { IndependentVideoTransaction.Apply(live, after, before); throw new Exception("overwrote game update"); }
        catch (IOException) { check(!File.Exists(videoPath) && File.ReadAllText(runtime) == "updated game", "changed game runtime is never overwritten"); }
        File.WriteAllText(runtime, "original");
        using (var locked = new FileStream(runtime, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            try { IndependentVideoTransaction.Apply(live, after, before); throw new Exception("overwrote locked file"); }
            catch (IOException) { check(!File.Exists(videoPath), "commit failure rolls back an already created video"); }
        }
        check(File.ReadAllText(runtime) == "original", "rollback preserves original runtime");
        foreach (string invalid in new[] { "../outside", "JixVideos/../../outside", "C:/outside", "JixVideos\\outside", "JixVideos//outside" })
        {
            try { IndependentVideoTransaction.TargetPath(live, invalid); throw new Exception("unsafe path accepted"); }
            catch (InvalidDataException) { check(true, "transaction rejects unsafe path " + invalid); }
        }
    }

    public static void RealAssembly(string dll, string destination, Action<bool, string> check)
    {
        byte[] original = File.ReadAllBytes(dll);
        var dependencies = Directory.EnumerateFiles(Path.GetDirectoryName(dll)!, "*.dll")
            .ToDictionary(Path.GetFileNameWithoutExtension, File.ReadAllBytes, StringComparer.OrdinalIgnoreCase);
        var patched = IndependentVideoPatch.PatchAssembly(original, new[] { "UT_Hero_Card_101", "UT_HandCard_21001", "UT_Event_1001", "UT_MapEvent_1001" }, dependencies);
        using var before = ModuleDefinition.ReadModule(new MemoryStream(original));
        using var after = ModuleDefinition.ReadModule(new MemoryStream(patched));
        check(before.AssemblyReferences.Select(a => a.FullName).SequenceEqual(after.AssemblyReferences.Select(a => a.FullName)),
            "real independent patch introduces no external assembly dependency");
        check(before.Types.Select(t => t.FullName).SequenceEqual(after.Types.Select(t => t.FullName)), "real independent patch introduces no runtime type or startup initializer");
        var allowed = new HashSet<string>
        {
            "UI.CriMovieManager::Load", "UI.CriManaMovieControllerForGGraph::SetAsset",
            "SkinStandingPaintingConfigureItem::GetCharacter", "SkinStandingPaintingConfigureItem::GetCharacterInGame",
            "UI.CommonUIManager::RendererCardContent", "UI.CriManaMovieBridge::AddVideoGraph"
        };
        var afterMethods = AllTypes(after.Types).SelectMany(t => t.Methods).ToDictionary(m => m.FullName);
        int untouched = 0;
        foreach (var type in AllTypes(before.Types))
        foreach (var method in type.Methods.Where(m => m.HasBody && !allowed.Contains(type.FullName + "::" + m.Name)))
        {
            var match = afterMethods[method.FullName];
            if (Fingerprint(method) != Fingerprint(match)) throw new Exception("Unexpected change: " + method.FullName);
            untouched++;
        }
        check(true, $"{untouched} other real game methods including startup remain unchanged");
        var injected = AllTypes(after.Types).SelectMany(t => t.Methods).Where(m => m.Name.StartsWith("Jix", StringComparison.Ordinal)).ToArray();
        check(!injected.SelectMany(m => m.Body.Instructions).Any(i => i.OpCode == OpCodes.Ldstr && (string)i.Operand == AnimatedPortraitPatch.VideoSlot),
            "independent mappings contain no borrowed VHandCard key");
        check(injected.Any(m => m.Name == "JixMapAnimatedCard") && injected.Any(m => m.Name == "JixMapAnimatedPortrait"),
            "real patch supports portrait, hand card and event mappings together");
        check(after.GetType("UI.CriManaMovieControllerForGGraph").Methods.Single(m => m.Name == "SetAsset").Body.Instructions
            .Any(i => i.Operand is MethodReference m && m.Name == "SetFile"), "real controller uses raw-file native API");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination))!);
        File.WriteAllBytes(destination, patched);
        Console.WriteLine("PATCHED COPY " + Path.GetFullPath(destination));
    }

    public static void PackageSmoke(string sourceRoot, string runtime, string usm, string destination, Action<bool, string> check)
    {
        string sourceHash = Hash(runtime);
        Directory.CreateDirectory(destination);
        string relative = Path.GetRelativePath(sourceRoot, runtime);
        string copy = Path.Combine(destination, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
        File.Copy(runtime, copy);
        string cacheInfo = Path.Combine(Path.GetDirectoryName(copy)!, "__info");
        File.WriteAllText(cacheInfo, "fixture-access-time");
        string unrelated = Path.Combine(destination, "unrelated-mod");
        File.WriteAllText(unrelated, "keep");
        var request = new AnimatedPortraitPatch.Request(destination, copy, null, "UT_HandCard_21001", usm, "", Independent: true);
        var first = PortraitReplacement.Apply(request, sourceName: "first.mp4");
        check(PortraitReplacement.IsInstalled(destination, first), "independent real bundle install passes hash checks without any video bundle");
        var second = PortraitReplacement.Apply(request with { TextureName = "UT_Hero_Card_101" }, sourceName: "second.mp4");
        check(second.Portraits.Count == 2 && PortraitReplacement.ReadReceipt(destination, "UT_HandCard_21001")?.SourceName == "first.mp4",
            "second independent target preserves first target and its receipt");
        var third = PortraitReplacement.Apply(request with { TextureName = "UT_Event_1001" }, sourceName: "event.mp4");
        check(third.Portraits.Count == 3 && PortraitReplacement.IsInstalled(destination, third), "independent event coexists with portrait and hand card");
        var fourth = PortraitReplacement.Apply(request, sourceName: "updated.mp4");
        check(fourth.Portraits.Count == 3 && fourth.Portraits["UT_Hero_Card_101"].SourceName == "second.mp4", "replacing one independent video preserves other bindings");
        using (var zip = ZipFile.OpenRead(fourth.ReplacementZip))
            check(zip.Entries.Count == 5 && zip.Entries.All(e => e.FullName == relative.Replace('\\', '/') || e.FullName.EndsWith(".usm") || Path.GetFileName(e.FullName) == "__info"),
                "direct replacement ZIP contains only runtime bundle, cache metadata and standalone USMs");
        string newVideo = Path.Combine(destination, IndependentVideoPatch.RelativeFile(request.TextureName));
        byte[] intact = File.ReadAllBytes(newVideo);
        File.WriteAllText(newVideo, "someone changed this");
        try { PortraitReplacement.Restore(destination); throw new Exception("restored over changed independent video"); }
        catch (IOException) { check(File.ReadAllText(newVideo) == "someone changed this", "independent restore rejects externally changed videos"); }
        File.WriteAllBytes(newVideo, intact);
        byte[] baseline = File.ReadAllBytes(fourth.BaselineRestoreZip);
        using (var changedBackup = ZipFile.Open(fourth.BaselineRestoreZip, ZipArchiveMode.Update))
        {
            string runtimeEntry = relative.Replace('\\', '/');
            changedBackup.GetEntry(runtimeEntry)!.Delete();
            using var writer = new StreamWriter(changedBackup.CreateEntry(runtimeEntry).Open());
            writer.Write("bad backup");
        }
        try { PortraitReplacement.Restore(destination); throw new Exception("restored a modified baseline"); }
        catch (IOException) { check(PortraitReplacement.IsInstalled(destination, fourth), "modified first backup is rejected without changing installed mod"); }
        File.WriteAllBytes(fourth.BaselineRestoreZip, baseline);
        File.WriteAllText(cacheInfo, "new-access-time");
        File.Delete(Path.Combine(destination, IndependentVideoPatch.RelativeFile("UT_Hero_Card_101")));
        PortraitReplacement.Restore(destination);
        check(Hash(copy) == sourceHash && fourth.Portraits.Keys.All(t => !File.Exists(Path.Combine(destination, IndependentVideoPatch.RelativeFile(t)))),
            "restore returns original runtime and removes all added videos");
        check(PortraitReplacement.ReadReceipt(destination) == null && File.ReadAllText(unrelated) == "keep", "restore clears active state and keeps unrelated mods");
        check(File.ReadAllText(cacheInfo) == "new-access-time", "restore tolerates missing generated video and preserves Unity access metadata");
        check(Hash(runtime) == sourceHash, "independent smoke test never modifies the user's game runtime");
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private static IEnumerable<TypeDefinition> AllTypes(IEnumerable<TypeDefinition> types) => types.SelectMany(t => new[] { t }.Concat(AllTypes(t.NestedTypes)));
    private static string Fingerprint(MethodDefinition method) => string.Join("\n", method.Body.Instructions.Select(i => i.OpCode + " " + (i.Operand switch
    {
        Instruction target => "@" + method.Body.Instructions.IndexOf(target),
        Instruction[] targets => string.Join(",", targets.Select(t => method.Body.Instructions.IndexOf(t))),
        _ => i.Operand?.ToString()
    })));
}
