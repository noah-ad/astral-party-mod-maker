using JixModMaker;

static void Check(bool condition, string name)
{
    if (!condition) throw new Exception(name);
    Console.WriteLine("PASS " + name);
}
if (args.Length == 4 && args[0] == "--decode-texture")
{
    File.WriteAllBytes(args[3], new ModEngine().DecodePng(args[1], long.Parse(args[2])));
    return;
}
if (args.Length == 4 && args[0] == "--patch-card-dll")
{
    var original = File.ReadAllBytes(args[1]);
    var dependencies = Directory.EnumerateFiles(Path.GetDirectoryName(args[1])!, "*.dll")
        .ToDictionary(path => Path.GetFileNameWithoutExtension(path), File.ReadAllBytes, StringComparer.OrdinalIgnoreCase);
    var patched = AnimatedPortraitPatch.PatchAssembly(original, args[3], AnimatedPortraitPatch.VideoSlot, dependencies);
    File.WriteAllBytes(args[2], patched);
    using var resolver = new Mono.Cecil.DefaultAssemblyResolver();
    resolver.AddSearchDirectory(Path.GetDirectoryName(args[1])!);
    using var module = Mono.Cecil.ModuleDefinition.ReadModule(new MemoryStream(patched),
        new Mono.Cecil.ReaderParameters { AssemblyResolver = resolver });
    var helperNames = new[]
    {
        "JixMapAnimatedCard", "JixCardVideoLayer", "JixCardVideoLayout",
        "JixPrepareAnimatedCard", "JixPrepareCardGraph", "JixCanAttachCardGraph"
    };
    foreach (var name in helperNames)
        Check(module.Types.SelectMany(t => t.Methods).Count(m => m.Name == name && m.HasBody) == 1,
            "real card patch contains one " + name);
    var renderer = module.GetType("UI.CommonUIManager").Methods.Single(m => m.Name == "RendererCardContent");
    Check(renderer.Body.Instructions.Any(i => i.Operand is Mono.Cecil.MethodReference m && m.Name == "JixPrepareAnimatedCard"),
        "real card renderer enters through guarded preparation helper");
    var bridge = module.GetType("UI.CriManaMovieBridge").Methods.Single(m => m.Name == "AddVideoGraph");
    Check(bridge.Body.Instructions.Any(i => i.Operand is Mono.Cecil.MethodReference m && m.Name == "JixCanAttachCardGraph"),
        "real movie bridge rejects stale graph callbacks");
    using var rewritten = new MemoryStream();
    module.Write(rewritten);
    using var reopened = Mono.Cecil.ModuleDefinition.ReadModule(new MemoryStream(rewritten.ToArray()),
        new Mono.Cecil.ReaderParameters { AssemblyResolver = resolver });
    Check(reopened.GetType("UI.CommonUIManager").Methods.Any(m => m.Name == "JixMapAnimatedCard"),
        "patched real DLL survives a second Cecil round trip");
    Console.WriteLine("PATCHED COPY " + Path.GetFullPath(args[2]));
    return;
}
if (args.Length == 3 && args[0] == "--inspect-skill")
{
    var info = SkillAnimationEngine.Inspect(args[1], long.Parse(args[2]));
    Check(info.FrameNames.Count > 1, "skill atlas contains an ordered Sprite sequence");
    Console.WriteLine($"{info.TextureName}: {info.FrameNames.Count} frames, {info.FrameWidth}x{info.FrameHeight}, atlas {info.AtlasWidth}x{info.AtlasHeight}");
    return;
}
if (args.Length == 4 && args[0] == "--decode-skill-frame")
{
    File.WriteAllBytes(args[3], SkillAnimationEngine.DecodeFramePreview(args[1], long.Parse(args[2]), 0));
    return;
}
if (args.Length == 3 && args[0] == "--catalog-owner")
{
    var owners = CatalogOwnership.Load(args[1], new[] { args[2] });
    Check(owners.TryGetValue(args[2], out var owner), "catalog maps the animation bundle to a character");
    Console.WriteLine($"{args[2]} -> {owner.CatalogKey} ({owner.HeroId}/{owner.Variant})");
    return;
}
if (args.Length == 2 && args[0] == "--index-skill")
{
    var timer = System.Diagnostics.Stopwatch.StartNew();
    var service = new IndexService();
    var index = service.Load(args[1], heroOnly: false, includeHotCache: true, recursive: false)
        ?? service.Build(args[1], heroOnly: false, includeHotCache: true, recursive: false);
    service.Save(index);
    var skill = index.Items.First(item => item.Bundle.Equals("17f54c024eb7245b398b7a84b3f4ec42.bundle", StringComparison.OrdinalIgnoreCase)
        && item.Name == "Talent-001");
    Check(skill.IsSkillAnimation && skill.OwnerHeroId == "101" && skill.CategoryId == ResourceCategories.CharacterId,
        "cached Talent atlas is enriched into Hero 101 without rescanning bundles");
    Console.WriteLine($"INDEX LOAD {timer.ElapsedMilliseconds} ms");
    return;
}
if (args.Length == 2 && args[0] == "--index-profile")
{
    var timer = System.Diagnostics.Stopwatch.StartNew();
    string cachePath = IndexService.CachePath(args[1], heroOnly: false, includeHotCache: true, recursive: false);
    var index = System.Text.Json.JsonSerializer.Deserialize<GameIndex>(File.ReadAllText(cachePath));
    Console.WriteLine($"DESERIALIZE {timer.ElapsedMilliseconds} ms");
    timer.Restart();
    IndexService.ComputeSourceStamp(args[1], includeHotCache: true, recursive: false);
    Console.WriteLine($"SOURCE STAMP {timer.ElapsedMilliseconds} ms");
    timer.Restart();
    IndexService.ComputeQuickSourceStamp(args[1], includeHotCache: true, recursive: false);
    Console.WriteLine($"QUICK STAMP {timer.ElapsedMilliseconds} ms");
    timer.Restart();
    CatalogOwnership.Load(args[1], index.Items.Select(item => item.Bundle).Distinct(StringComparer.OrdinalIgnoreCase));
    Console.WriteLine($"CATALOG OWNERS {timer.ElapsedMilliseconds} ms");
    return;
}
if (args.Length == 5 && args[0] == "--skill-smoke")
{
    string sourceHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(args[1])));
    File.Copy(args[1], args[4], true);
    string work = Path.Combine(Path.GetTempPath(), "JixSkillSmoke-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(work);
    try
    {
        var before = SkillAnimationEngine.Inspect(args[4], long.Parse(args[2]));
        var after = SkillAnimationEngine.ReplaceAsync(args[4], long.Parse(args[2]), before.TextureName, args[3],
            Path.Combine(work, "backup"), Path.GetFileName(args[4]), Path.Combine(work, "work"),
            new PortraitVideoConverter.Options(), CancellationToken.None).GetAwaiter().GetResult();
        Check(before.FrameNames.SequenceEqual(after.FrameNames), "skill replacement preserves native frame order and count");
        Check(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(args[1]))) == sourceHash,
            "skill smoke test leaves the game bundle byte-identical");
        Check(!File.ReadAllBytes(args[4]).SequenceEqual(File.ReadAllBytes(args[1])), "skill atlas copy is replaced");
        Console.WriteLine("SKILL COPY " + Path.GetFullPath(args[4]));
    }
    finally { try { Directory.Delete(work, true); } catch { } }
    return;
}
if (args.Length == 2 && args[0] == "--crop-ui")
{
    Exception failure = null;
    var thread = new Thread(() =>
    {
        try
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Control.CheckForIllegalCrossThreadCalls = true;
            WindowsFormsSynchronizationContext.AutoInstall = false;
            using var uiContext = new WindowsFormsSynchronizationContext();
            SynchronizationContext.SetSynchronizationContext(uiContext);
            IEnumerable<Control> All(Control parent) => parent.Controls.Cast<Control>().SelectMany(c => new[] { c }.Concat(All(c)));
            foreach (float scale in new[] { 1f, 1.5f })
            {
                using var dialog = new AnimatedPortraitDialog(@"D:\example\AssetBundles", "UT_Hero_Card_101", args[1], new(880, 1205));
                dialog.StartPosition = FormStartPosition.Manual;
                dialog.Location = new(-4000, -4000);
                dialog.Scale(new System.Drawing.SizeF(scale, scale));
                dialog.Show();
                var timer = System.Diagnostics.Stopwatch.StartNew();
                while (!dialog.ResultMessage.StartsWith("取景已就绪") && timer.Elapsed < TimeSpan.FromSeconds(20))
                {
                    Application.DoEvents();
                    Thread.Sleep(10);
                }
                Check(dialog.ResultMessage.StartsWith("取景已就绪"), "real video crop ready at scale " + scale);
                var crop = All(dialog).OfType<VideoCropControl>().Single();
                var before = crop.Crop;
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                void Mouse(string method, int x, int y) => typeof(VideoCropControl).GetMethod(method, flags).Invoke(crop, new object[] { new MouseEventArgs(MouseButtons.Left, 1, x, y, 0) });
                Mouse("OnMouseDown", crop.Width / 2, crop.Height / 2);
                Mouse("OnMouseMove", crop.Width / 2 + 70, crop.Height / 2);
                Mouse("OnMouseUp", crop.Width / 2 + 70, crop.Height / 2);
                Check(crop.Crop.X > before.X && crop.Crop.Width == before.Width, "drag moves locked-aspect crop at scale " + scale);
                var zoom = All(dialog).OfType<TrackBar>().Single();
                zoom.Value = 130;
                Check(crop.Crop.Width < before.Width, "zoom slider changes crop at scale " + scale);
                var footer = dialog.Controls[0].Controls.OfType<FlowLayoutPanel>().Single();
                Check(footer.Controls.Cast<Control>().Where(c => c.Visible).All(c => c.Right <= footer.ClientSize.Width && c.Bottom <= footer.ClientSize.Height), "action row fits at scale " + scale);
                using (var bitmap = new System.Drawing.Bitmap(dialog.Width, dialog.Height))
                {
                    dialog.DrawToBitmap(bitmap, new(0, 0, dialog.Width, dialog.Height));
                    bitmap.Save(Path.Combine(AppContext.BaseDirectory, "crop-video-" + scale + ".png"));
                }
                All(dialog).OfType<PortraitViewTabs>().Single().SelectedIndex = 1;
                timer.Restart();
                while (!dialog.ResultMessage.StartsWith("预览已就绪") && timer.Elapsed < TimeSpan.FromSeconds(30))
                {
                    Application.DoEvents();
                    Thread.Sleep(10);
                }
                Check(dialog.ResultMessage.StartsWith("预览已就绪"), "real video preview ready at scale " + scale);
                using (var bitmap = new System.Drawing.Bitmap(dialog.Width, dialog.Height))
                {
                    dialog.DrawToBitmap(bitmap, new(0, 0, dialog.Width, dialog.Height));
                    bitmap.Save(Path.Combine(AppContext.BaseDirectory, "crop-preview-" + scale + ".png"));
                }
                dialog.Close();
                Application.DoEvents();
            }
        }
        catch (Exception ex) { failure = ex; }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join();
    if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    return;
}
if (args.Length == 4 && args[0] == "--preview-file")
{
    PortraitVideoConverter.PreviewAsync(args[2], args[3], new() { Ffmpeg = args[1] }, new(), CancellationToken.None).GetAwaiter().GetResult();
    return;
}
if (args.Length is 3 or 4 && args[0] == "--convert-file")
{
    if (args.Length == 4)
    {
        string data = Path.GetFullPath(args[3]);
        var packageVersion = System.Diagnostics.FileVersionInfo.GetVersionInfo(Path.Combine(data, "JixModMaker.dll")).ProductVersion;
        var testVersion = System.Diagnostics.FileVersionInfo.GetVersionInfo(typeof(MainForm).Assembly.Location).ProductVersion;
        Check(packageVersion == testVersion, "packaged conversion components match tested application version");
        AppContext.SetData("APP_CONTEXT_BASE_DIRECTORY", data + Path.DirectorySeparatorChar);
    }
    Directory.CreateDirectory(args[2]);
    Console.WriteLine(PortraitVideoConverter.ConvertAsync(args[1], args[2], PortraitVideoSettings.Load(), new(), CancellationToken.None,
        new Progress<string>(Console.WriteLine)).GetAwaiter().GetResult());
    return;
}
if (args.Length == 3 && args[0] == "--crop-file")
{
    Directory.CreateDirectory(args[2]);
    var tools = PortraitVideoSettings.Load();
    var frame = Path.Combine(args[2], "source.png");
    PortraitVideoConverter.SourceFrameAsync(args[1], frame, tools, CancellationToken.None).GetAwaiter().GetResult();
    using var bitmap = new System.Drawing.Bitmap(frame);
    var options = new PortraitVideoConverter.Options(true, Crop: VideoCrop.Fit(bitmap.Width, bitmap.Height, 880, 1205, centerX: .65));
    Console.WriteLine(PortraitVideoConverter.ConvertAsync(args[1], args[2], tools, options, CancellationToken.None,
        new Progress<string>(Console.WriteLine)).GetAwaiter().GetResult());
    return;
}
if (args.Length == 5 && args[0] == "--animation-install-smoke")
{
    var copyRoot = Path.Combine(AppContext.BaseDirectory, "install-smoke-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(copyRoot);
    string Clone(string path)
    {
        string relative = Path.GetRelativePath(args[1], path);
        if (relative.StartsWith("..") || Path.IsPathRooted(relative)) throw new Exception("source must stay in root");
        string targetPath = Path.Combine(copyRoot, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath));
        File.Copy(path, targetPath);
        string info = Path.Combine(Path.GetDirectoryName(path), "__info");
        if (File.Exists(info)) File.Copy(info, Path.Combine(Path.GetDirectoryName(targetPath), "__info"));
        return targetPath;
    }
    string HashFile(string path) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));
    var originalHashes = new[] { HashFile(args[2]), HashFile(args[3]) };
    var request = new AnimatedPortraitPatch.Request(copyRoot, Clone(args[2]), Clone(args[3]), "UT_Hero_Card_101", args[4], "");
    var firstInstall = PortraitReplacement.Apply(request, sourceName: "first.mp4");
    Check(PortraitReplacement.IsInstalled(copyRoot, firstInstall), "isolated install verified against written file hashes");
    var secondInstall = PortraitReplacement.Apply(request, sourceName: "second.mp4", removeGreen: true);
    Check(secondInstall.BaselineRestoreZip == firstInstall.RestoreZip, "repeat replacement keeps first clean backup");
    Check(PortraitReplacement.ReadReceipt(copyRoot).SourceName == "second.mp4", "persistent replacement receipt updated");
    Check(HashFile(request.VideoBundle) != originalHashes[1], "official video slot is replaced");
    try
    {
        PortraitReplacement.Apply(request with { TextureName = "UT_Hero_Card_102" }, sourceName: "third.mp4");
        throw new Exception("shared video slot accepted a second portrait");
    }
    catch (InvalidOperationException) { Console.WriteLine("PASS one native slot cannot be assigned to two portraits"); }
    string cacheInfo = Path.Combine(Path.GetDirectoryName(request.RuntimeBundle), "__info");
    if (File.Exists(cacheInfo))
    {
        var lines = File.ReadAllLines(cacheInfo);
        lines[1] = "1234567890";
        File.WriteAllLines(cacheInfo, lines);
        Check(PortraitReplacement.IsInstalled(copyRoot, secondInstall), "Unity cache access time does not invalidate installed mod");
    }
    var tamperPath = request.RuntimeBundle;
    var intact = File.ReadAllBytes(tamperPath);
    File.WriteAllBytes(tamperPath, new byte[] { 7, 8, 9 });
    Check(!PortraitReplacement.IsInstalled(copyRoot, secondInstall), "changed resource is not reported as installed");
    try { PortraitReplacement.Restore(copyRoot); throw new Exception("changed resource overwritten by restore"); }
    catch (IOException) { Check(File.ReadAllBytes(tamperPath).SequenceEqual(new byte[] { 7, 8, 9 }), "restore refuses to overwrite other changes"); }
    File.WriteAllBytes(tamperPath, intact);
    PortraitReplacement.Restore(copyRoot);
    Check(PortraitReplacement.ReadReceipt(copyRoot) == null, "restoring clears active replacement status");
    if (File.Exists(cacheInfo)) Check(File.ReadAllLines(cacheInfo)[1] == "1234567890", "restore preserves Unity cache access metadata");
    Check(HashFile(request.RuntimeBundle) == originalHashes[0] && HashFile(request.VideoBundle) == originalHashes[1], "restore returns both exact pre-first-replacement bundles");
    Check(HashFile(args[2]) == originalHashes[0] && HashFile(args[3]) == originalHashes[1], "user game bundles untouched by isolated test");
    Console.WriteLine("ISOLATED COPY " + copyRoot);
    return;
}

Check(NameParser.Parse("UT_Hero_Card_135_06_0").Skin == "皮肤06", "new hero and skin 06");
Check(NameParser.Parse("UT_Hero_Card_135").Skin == "原皮", "base skin label");
var groupedSkill = NameParser.Parse("Talent-001", "115", "02", true, false);
Check(groupedSkill.GroupKey == "角色 115" && groupedSkill.Skin == "皮肤02" && groupedSkill.Kind == "SkillAnimation",
    "skill animation groups with its owning character skin");
var landscapeCrop = VideoCrop.Fit(1920, 1080, 880, 1205);
Check(Math.Abs(landscapeCrop.Width * 1920 / (landscapeCrop.Height * 1080) - 880d / 1205) < .000001, "landscape video is cropped to portrait aspect, not squeezed");
var edgeCrop = landscapeCrop.Move(5, -2);
Check(edgeCrop.X + edgeCrop.Width <= 1 && edgeCrop.Y == 0, "drag is clamped to video bounds");
var zoomCrop = VideoCrop.Fit(1920, 1080, 880, 1205, 2, .9, .9);
Check(zoomCrop.Width == landscapeCrop.Width / 2 && zoomCrop.X + zoomCrop.Width <= 1 && zoomCrop.Y + zoomCrop.Height <= 1, "zoom preserves aspect and stays inside image");
Check(!new PortraitVideoConverter.Options().RemoveGreen, "converter green-screen removal defaults to off");
var originalAssembly = File.ReadAllBytes(typeof(SkinStandingPaintingConfigureItem).Assembly.Location);
if (args.Length == 3 && args[0] == "--reject-type-layout")
{
    try
    {
        AnimatedPortraitPatch.VerifyTypeLayout(args[1], args[2]);
        throw new Exception("broken type table accepted");
    }
    catch (InvalidDataException) { Console.WriteLine("PASS old malformed TextAsset type table rejected"); }
    return;
}
var patchedAssembly = AnimatedPortraitPatch.PatchAssembly(originalAssembly, "UT_Hero_Card_101", AnimatedPortraitPatch.VideoSlot);
Check(patchedAssembly.SequenceEqual(AnimatedPortraitPatch.PatchAssembly(patchedAssembly, "UT_Hero_Card_101", AnimatedPortraitPatch.VideoSlot)),
    "repeating the same native-slot mapping does not stack patches");
using (var module = Mono.Cecil.ModuleDefinition.ReadModule(new MemoryStream(patchedAssembly)))
{
    Check(!module.Types.Any(t => t.Namespace is "Jix.DynamicPortrait" or "Jix.DynamicRendering"),
        "native-slot patch embeds no independent video resources or renderers");
    Check(module.Types.Single(t => t.Name == "SkinStandingPaintingConfigureItem").Methods.Count(m => m.Name == "JixMapAnimatedPortrait") == 1,
        "native-slot patch adds one small portrait mapping helper");
}
try
{
    AnimatedPortraitPatch.PatchAssembly(patchedAssembly, "UT_Hero_Card_102", AnimatedPortraitPatch.VideoSlot);
    throw new Exception("one video slot accepted two portrait mappings");
}
catch (InvalidOperationException) { Console.WriteLine("PASS native video slot remains single-owner"); }
foreach (string supported in new[] { "UT_HandCard_21002", "UT_Event_12703", "UT_MapEvent_31001", "UT_HandCard_21002_sfw" })
    Check(AnimatedPortraitPatch.IsSupportedTarget(supported), "dynamic card target accepted: " + supported);
foreach (string unsupported in new[] { "UT_Hero_Card_", "UT_HandCard_", "UT_Event_frame", "PlatformEvent", "LandEvent" })
{
    try { AnimatedPortraitPatch.PatchAssembly(originalAssembly, unsupported, AnimatedPortraitPatch.VideoSlot); throw new Exception("unsupported dynamic target accepted"); }
    catch (ArgumentException) { Console.WriteLine("PASS unsupported dynamic target rejected: " + unsupported); }
}
var context = new System.Runtime.Loader.AssemblyLoadContext("portrait-test", true);
using (var stream = new MemoryStream(patchedAssembly))
{
    var assembly = context.LoadFromStream(stream);
    var fixture = assembly.GetType("SkinStandingPaintingConfigureItem");
    var instance = Activator.CreateInstance(fixture);
    (string, bool) Invoke(string method) => ((string, bool))fixture.GetMethod(method).Invoke(instance, null);
    Check(Invoke("GetCharacter") == (AnimatedPortraitPatch.VideoSlot, true), "selected static portrait routes to official video slot");
    Check(Invoke("GetCharacterInGame") == (AnimatedPortraitPatch.VideoSlot, true), "in-game fallback routes to official video slot");
    fixture.GetProperty("InGameCharacter").SetValue(instance, "UT_Hero_Card_101");
    Check(Invoke("GetCharacterInGame") == (AnimatedPortraitPatch.VideoSlot, true), "explicit in-game portrait routes to video");
    fixture.GetProperty("Character").SetValue(instance, "UT_Hero_Card_103");
    Check(Invoke("GetCharacter") == ("UT_Hero_Card_103", false), "unmodified portraits remain static");
    fixture.GetProperty("SafeMode").SetValue(instance, true);
    Check(Invoke("GetCharacter") == ("UT_Hero_Card_101_sfw", false), "safe-mode portrait remains unchanged");
}
context.Unload();
if (args.Length == 2 && args[0] == "--animation-find")
{
    var found = AnimatedPortraitPatch.FindBundles(args[1], CancellationToken.None);
    Check(File.Exists(found.Runtime) && File.Exists(found.Video), "runtime and official video slot discovery");
    Console.WriteLine(found.Runtime);
    Console.WriteLine(found.Video);
    return;
}
if (args.Length is 5 or 6 && args[0] == "--animation-smoke")
{
    string Hash(string path) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));
    var runtimeHash = Hash(args[2]);
    var videoHash = Hash(args[3]);
    var destination = Path.Combine(AppContext.BaseDirectory, "animation-smoke-" + Guid.NewGuid().ToString("N") + ".zip");
    string animationTarget = args.Length == 6 ? args[5] : "UT_Hero_Card_101";
    var result = AnimatedPortraitPatch.Export(new(args[1], args[2], args[3], animationTarget, args[4], destination));
    Check(Hash(args[2]) == runtimeHash && Hash(args[3]) == videoHash, "real input bundles remain byte-identical");
    using var patchZip = System.IO.Compression.ZipFile.OpenRead(result.ReplacementZip);
    using var restoreZip = System.IO.Compression.ZipFile.OpenRead(result.RestoreZip);
    Check(patchZip.Entries.Select(e => e.FullName).SequenceEqual(restoreZip.Entries.Select(e => e.FullName)), "real patch and restore have identical replacement paths");
    Check(patchZip.Entries.Count(entry => Path.GetFileName(entry.FullName) != "__info") == 2,
        "ZIP changes only the runtime and official video bundles");
    Check(patchZip.GetEntry(Path.GetRelativePath(args[1], args[3]).Replace('\\', '/')) != null, "ZIP contains the official alternate-art video slot");
    foreach (var path in new[] { args[2], args[3] })
    {
        var relative = Path.GetRelativePath(args[1], path).Replace('\\', '/');
        using var entry = restoreZip.GetEntry(relative).Open();
        var restoredHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(entry));
        Check(restoredHash == Hash(path), "restore contains exact original: " + relative);
    }
    Console.WriteLine($"SMOKE ONLY {animationTarget} / {result.Platform}: {result.ReplacementZip}");
    return;
}
var now = DateTimeOffset.UtcNow;
Check(!ExportSelectionDialog.MatchesRange(new ModEntry(), 1, now), "unknown date excluded from recent");
Check(ExportSelectionDialog.MatchesRange(new ModEntry(), 3, now), "unknown date filter");
Check(ExportSelectionDialog.MatchesRange(new ModEntry { ModifiedAt = now.AddHours(-2) }, 1, now), "recent entry included");
Check(!ExportSelectionDialog.MatchesRange(new ModEntry { ModifiedAt = now.AddDays(-8) }, 2, now), "old entry excluded");
var workspace = new ModManifest();
PackService.Upsert(workspace, new ModEntry { Bundle = "a", PathId = 1 });
Check(workspace.Entries.Single().ModifiedAt >= now, "replacement gets timestamp");

var root = Path.Combine(AppContext.BaseDirectory, "fixtures-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var installRoot = Path.Combine(root, "install");
Directory.CreateDirectory(installRoot);
var target = Path.Combine(installRoot, "resource.bundle");
File.WriteAllText(target, "original");
var restoreTest = Path.Combine(root, "restore.zip");
var replaceTest = Path.Combine(root, "replace.zip");
using (var archive = System.IO.Compression.ZipFile.Open(restoreTest, System.IO.Compression.ZipArchiveMode.Create))
using (var writer = new StreamWriter(archive.CreateEntry("resource.bundle").Open())) writer.Write("original");
using (var archive = System.IO.Compression.ZipFile.Open(replaceTest, System.IO.Compression.ZipArchiveMode.Create))
using (var writer = new StreamWriter(archive.CreateEntry("resource.bundle").Open())) writer.Write("changed");
PortraitReplacement.Install(installRoot, replaceTest, restoreTest);
Check(File.ReadAllText(target) == "changed", "direct replacement installs bytes");
try
{
    PortraitReplacement.Install(installRoot, replaceTest, restoreTest);
    throw new Exception("changed target overwritten");
}
catch (IOException) { Check(File.ReadAllText(target) == "changed", "concurrent target changes rejected"); }
var bundle = Path.Combine(root, "test.bundle");
File.WriteAllBytes(bundle, new byte[] { 1 });
var fakeVideo = Path.Combine(root, "video.bundle");
File.WriteAllBytes(fakeVideo, new byte[] { 2 });
var fakeUsm = Path.Combine(root, "renamed.usm");
File.WriteAllBytes(fakeUsm, new byte[32]);
var rejectedZip = Path.Combine(root, "rejected.zip");
try
{
    AnimatedPortraitPatch.Export(new(root, bundle, fakeVideo, "UT_Hero_Card_101", fakeUsm, rejectedZip));
    throw new Exception("renamed file accepted as USM");
}
catch (InvalidDataException) { Console.WriteLine("PASS non-USM input rejected"); }
Check(!File.Exists(rejectedZip) && !File.Exists(Path.ChangeExtension(rejectedZip, ".restore.zip")), "failed export publishes neither ZIP");
if (args.Length == 2 && args[0] == "--video-test")
{
    var source = Path.Combine(root, "green source.png");
    using (var bitmap = new System.Drawing.Bitmap(64, 64))
    {
        using var graphics = System.Drawing.Graphics.FromImage(bitmap);
        graphics.Clear(System.Drawing.Color.Lime);
        graphics.FillRectangle(System.Drawing.Brushes.Red, 20, 20, 24, 24);
        using var darkGreen = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(10, 65, 14));
        graphics.FillRectangle(darkGreen, 48, 0, 16, 16);
        using var gray = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(30, 30, 30));
        graphics.FillRectangle(gray, 48, 20, 16, 16);
        bitmap.SetPixel(0, 0, System.Drawing.Color.Transparent);
        bitmap.Save(source);
    }
    var tools = new PortraitVideoSettings { Ffmpeg = args[1] };
    var keyed = Path.Combine(root, "keyed.png");
    PortraitVideoConverter.PreviewAsync(source, keyed, tools, new(RemoveGreen: false), CancellationToken.None).GetAwaiter().GetResult();
    using (var bitmap = new System.Drawing.Bitmap(keyed))
        Check(bitmap.GetPixel(8, 8).A == 255 && bitmap.GetPixel(8, 8).G > 200, "unchecked green removal preserves background");
    PortraitVideoConverter.PreviewAsync(source, keyed, tools, new(true), CancellationToken.None).GetAwaiter().GetResult();
    using (var bitmap = new System.Drawing.Bitmap(keyed))
    {
        Check(bitmap.GetPixel(8, 8).A == 0, "green background becomes transparent");
        Check(bitmap.GetPixel(32, 32).A == 255 && bitmap.GetPixel(32, 32).R > 200, "foreground retained");
        Check(bitmap.GetPixel(0, 0).A == 0, "source alpha preserved with key enabled");
        Check(bitmap.GetPixel(55, 8).A == 0, "dark green screen removed");
        Check(bitmap.GetPixel(55, 28).A == 255, "dark neutral foreground retained");
    }
    var gif = Path.Combine(root, "green animation.gif");
    PortraitVideoConverter.RunAsync(args[1], new[] { "-nostdin", "-v", "error", "-y", "-loop", "1", "-i", source, "-t", "0.5", "-r", "10", gif }, CancellationToken.None).GetAwaiter().GetResult();
    PortraitVideoConverter.PreviewAsync(gif, keyed, tools, new(true), CancellationToken.None).GetAwaiter().GetResult();
    using (var bitmap = new System.Drawing.Bitmap(keyed)) Check(bitmap.GetPixel(8, 8).A == 0, "GIF input keyed");
    var mp4 = Path.Combine(root, "green video.mp4");
    PortraitVideoConverter.RunAsync(args[1], new[] { "-nostdin", "-v", "error", "-y", "-i", gif, "-vf", "scale=out_color_matrix=bt709", "-colorspace", "bt709", "-color_primaries", "bt709", "-color_trc", "bt709", "-c:v", "libx264", "-pix_fmt", "yuv420p", mp4 }, CancellationToken.None).GetAwaiter().GetResult();
    PortraitVideoConverter.PreviewAsync(mp4, keyed, tools, new(true), CancellationToken.None).GetAwaiter().GetResult();
    using (var bitmap = new System.Drawing.Bitmap(keyed)) Check(bitmap.GetPixel(8, 8).A == 0, "MP4 input keyed");
    var avi = Path.Combine(root, "alpha.avi");
    PortraitVideoConverter.RunAsync(args[1], new[] { "-nostdin", "-v", "error", "-y", "-i", gif, "-an", "-vf", PortraitVideoConverter.Filter(new(true)) + ",fps=30", "-c:v", "rawvideo", "-pix_fmt", "bgra", avi }, CancellationToken.None).GetAwaiter().GetResult();
    PortraitVideoConverter.PreviewAsync(avi, keyed, tools, new(false), CancellationToken.None).GetAwaiter().GetResult();
    using (var bitmap = new System.Drawing.Bitmap(keyed)) Check(bitmap.GetPixel(8, 8).A == 0, "AVI intermediate retains alpha");
    var converted = PortraitVideoConverter.ConvertAsync(gif, root, tools, new(), CancellationToken.None).GetAwaiter().GetResult();
    Check(File.ReadAllBytes(converted).Take(4).SequenceEqual("CRID"u8.ToArray()), "GIF converts to verified USM without encoder configuration");
    PortraitMovieMetadata.Read(converted).Validate(File.ReadAllBytes(converted));
    var alphaPlane = Path.Combine(root, "encoded-alpha.png");
    PortraitVideoConverter.RunAsync(args[1], new[] { "-nostdin", "-v", "error", "-y", "-i", Path.Combine(root, "alpha.m1v"), "-vf", "extractplanes=y", "-frames:v", "1", alphaPlane }, CancellationToken.None).GetAwaiter().GetResult();
    using (var bitmap = new System.Drawing.Bitmap(alphaPlane))
        Check(bitmap.GetPixel(32, 32).R >= 253, "opaque video remains fully opaque in encoded raw alpha plane");
    PortraitVideoConverter.ConvertAsync(gif, root, tools, new(true), CancellationToken.None).GetAwaiter().GetResult();
    PortraitVideoConverter.RunAsync(args[1], new[] { "-nostdin", "-v", "error", "-y", "-i", Path.Combine(root, "alpha.m1v"), "-vf", "extractplanes=y", "-frames:v", "1", alphaPlane }, CancellationToken.None).GetAwaiter().GetResult();
    using (var bitmap = new System.Drawing.Bitmap(alphaPlane))
    {
        Check(bitmap.GetPixel(8, 8).R <= 2, "removed green is zero coverage after video encoding");
        Check(bitmap.GetPixel(32, 32).R >= 253, "foreground alpha is not reduced to studio-range 235");
    }
    var alphaGradient = Path.Combine(root, "alpha-gradient.png");
    using (var bitmap = new System.Drawing.Bitmap(256, 64))
    {
        for (int x = 0; x < bitmap.Width; x++)
        for (int y = 0; y < bitmap.Height; y++)
            bitmap.SetPixel(x, y, System.Drawing.Color.FromArgb(x, 255, 255, 255));
        bitmap.Save(alphaGradient);
    }
    PortraitVideoConverter.ConvertAsync(alphaGradient, root, tools, new(), CancellationToken.None).GetAwaiter().GetResult();
    PortraitVideoConverter.RunAsync(args[1], new[] { "-nostdin", "-v", "error", "-y", "-i", Path.Combine(root, "alpha.m1v"), "-vf", "extractplanes=y", "-frames:v", "1", alphaPlane }, CancellationToken.None).GetAwaiter().GetResult();
    using (var bitmap = new System.Drawing.Bitmap(alphaPlane))
    {
        int maxError = Enumerable.Range(0, 256).Max(x => Math.Abs(bitmap.GetPixel(x, 32).R - x));
        Check(maxError <= 3, "semi-transparent coverage preserved across full alpha range; max error " + maxError);
    }
    PortraitVideoConverter.ConvertAsync(mp4, root, tools, new(), CancellationToken.None).GetAwaiter().GetResult();
    var sourceColors = Path.Combine(root, "source-colors.png");
    var encodedColors = Path.Combine(root, "encoded-colors.png");
    PortraitVideoConverter.SourceFrameAsync(mp4, sourceColors, tools, CancellationToken.None).GetAwaiter().GetResult();
    PortraitVideoConverter.SourceFrameAsync(Path.Combine(root, "color.m1v"), encodedColors, tools, CancellationToken.None).GetAwaiter().GetResult();
    using (var sourceBitmap = new System.Drawing.Bitmap(sourceColors))
    using (var encodedBitmap = new System.Drawing.Bitmap(encodedColors))
    {
        int maxError = 0;
        foreach (var point in new[] { new System.Drawing.Point(8, 8), new System.Drawing.Point(32, 32), new System.Drawing.Point(55, 8), new System.Drawing.Point(55, 28) })
        {
            var expected = sourceBitmap.GetPixel(point.X, point.Y);
            var actual = encodedBitmap.GetPixel(point.X, point.Y);
            maxError = new[] { maxError, Math.Abs(expected.R - actual.R), Math.Abs(expected.G - actual.G), Math.Abs(expected.B - actual.B) }.Max();
        }
        Check(maxError <= 8, "BT.709 source color survives MPEG-1 metadata loss; max channel error " + maxError);
    }
    var widescreen = Path.Combine(root, "wide.png");
    using (var bitmap = new System.Drawing.Bitmap(1920, 1080))
    {
        using var graphics = System.Drawing.Graphics.FromImage(bitmap);
        graphics.Clear(System.Drawing.Color.Red);
        graphics.FillRectangle(System.Drawing.Brushes.Blue, 960, 0, 960, 1080);
        bitmap.Save(widescreen);
    }
    var cropLeft = VideoCrop.Fit(1920, 1080, 880, 1205, centerX: 0);
    PortraitVideoConverter.PreviewAsync(widescreen, keyed, tools, new(Crop: cropLeft), CancellationToken.None).GetAwaiter().GetResult();
    using (var bitmap = new System.Drawing.Bitmap(keyed))
    {
        Check(bitmap.GetPixel(bitmap.Width / 2, bitmap.Height / 2).R > 220, "left crop uses selected source region");
        Check(Math.Abs(bitmap.Width / (double)bitmap.Height - 880d / 1205) < .015, "rendered crop matches target aspect with codec alignment");
    }
    PortraitVideoConverter.PreviewAsync(widescreen, keyed, tools, new(Crop: cropLeft.Move(1, 0)), CancellationToken.None).GetAwaiter().GetResult();
    using (var bitmap = new System.Drawing.Bitmap(keyed)) Check(bitmap.GetPixel(bitmap.Width / 2, bitmap.Height / 2).B > 220, "moving crop changes rendered source region");
    var hdVideo = Path.Combine(root, "wide.mp4");
    PortraitVideoConverter.RunAsync(args[1], new[] { "-nostdin", "-v", "error", "-y", "-loop", "1", "-i", widescreen, "-t", "0.3", "-c:v", "libx264", "-pix_fmt", "yuv420p", hdVideo }, CancellationToken.None).GetAwaiter().GetResult();
    PortraitVideoConverter.ConvertAsync(hdVideo, root, tools, new(), CancellationToken.None).GetAwaiter().GetResult();
    var hdMeta = PortraitMovieMetadata.Read(converted);
    Check(hdMeta.Width == 1920 && hdMeta.Height == 1080, "HD conversion preserves 1080p without old 1024px downscale");
    PortraitVideoConverter.ConvertAsync(hdVideo, root, tools, new(Crop: cropLeft), CancellationToken.None).GetAwaiter().GetResult();
    var croppedMeta = PortraitMovieMetadata.Read(converted);
    Check(croppedMeta.Width < hdMeta.Width && croppedMeta.Height == 1080 && Math.Abs(croppedMeta.Width / (double)croppedMeta.Height - 880d / 1205) < .015, "encoded crop uses selected aspect at native detail");
    var anamorphic = Path.Combine(root, "anamorphic.mp4");
    PortraitVideoConverter.RunAsync(args[1], new[] { "-nostdin", "-v", "error", "-y", "-i", gif, "-vf", "setsar=2/1", "-c:v", "libx264", "-pix_fmt", "yuv420p", anamorphic }, CancellationToken.None).GetAwaiter().GetResult();
    PortraitVideoConverter.ConvertAsync(anamorphic, root, tools, new(), CancellationToken.None).GetAwaiter().GetResult();
    var anamorphicMeta = PortraitMovieMetadata.Read(converted);
    Check(anamorphicMeta.Width == 128 && anamorphicMeta.Height == 64, "non-square source pixels normalize before conversion without distortion");
    using var cancelled = new CancellationTokenSource();
    cancelled.Cancel();
    try
    {
        PortraitVideoConverter.ConvertAsync(mp4, root, tools, new(), cancelled.Token).GetAwaiter().GetResult();
        throw new Exception("cancelled conversion accepted");
    }
    catch (OperationCanceledException) { Console.WriteLine("PASS conversion cancellation"); }
}
var first = IndexService.ComputeSourceStamp(root, false, false);
File.WriteAllBytes(bundle, new byte[] { 1, 2 });
Check(first != IndexService.ComputeSourceStamp(root, false, false), "same filename update invalidates index");
first = IndexService.ComputeSourceStamp(root, false, false);
var quickBeforeNewBundle = IndexService.ComputeQuickSourceStamp(root, false, false);
File.WriteAllBytes(Path.Combine(root, "new-hero.bundle"), new byte[] { 3 });
Check(first != IndexService.ComputeSourceStamp(root, false, false), "new bundle invalidates index");
Check(quickBeforeNewBundle != IndexService.ComputeQuickSourceStamp(root, false, false),
    "new bundle invalidates the fast startup fingerprint");
Console.WriteLine("All regression checks passed.");
var wrapped = Path.Combine(root, "hash-a", "hash-b");
Directory.CreateDirectory(wrapped);
File.WriteAllBytes(Path.Combine(wrapped, "__data"), new byte[] { 4, 5 });
File.WriteAllText(Path.Combine(wrapped, "__info"), "metadata");
var output = Path.Combine(root, "replacement.zip");
ReplacementZip.Export(root, new[] { bundle, Path.Combine(wrapped, "__data") }, output);
using (var zip = System.IO.Compression.ZipFile.OpenRead(output))
{
    Check(zip.Entries.Select(e => e.FullName).Order().SequenceEqual(new[] { "hash-a/hash-b/__data", "hash-a/hash-b/__info", "test.bundle" }), "replacement ZIP preserves exact paths without wrappers");
    using var data = zip.GetEntry("hash-a/hash-b/__data").Open();
Check(data.ReadByte() == 4 && data.ReadByte() == 5 && data.ReadByte() == -1, "replacement bytes unchanged");
}
var externalBackupRoot = Path.Combine(root, "game", "_原始备份");
var externalWrappedA = Path.Combine(root, "hot", "a", "first", "__data");
var externalWrappedB = Path.Combine(root, "hot", "b", "second", "__data");
var externalBackupA = ResourceLocator.BackupPath(externalWrappedA, externalBackupRoot, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.bundle");
var externalBackupB = ResourceLocator.BackupPath(externalWrappedB, externalBackupRoot, "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb.bundle");
Check(!string.Equals(externalBackupA, externalBackupB, StringComparison.OrdinalIgnoreCase) &&
    Path.GetFileName(externalBackupA) == "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.bundle" &&
    Path.GetFileName(externalBackupB) == "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb.bundle",
    "external hot-cache bundles receive distinct logical backup names");
try
{
    ReplacementZip.Export(wrapped, new[] { bundle }, output);
    throw new Exception("external path accepted");
}
catch (InvalidOperationException) { Console.WriteLine("PASS external resource rejected"); }
Exception uiError = null;
var ui = new Thread(() =>
{
    Control.CheckForIllegalCrossThreadCalls = true;
    // This offscreen runner pumps messages without Application.Run; keep async callbacks on its STA.
    WindowsFormsSynchronizationContext.AutoInstall = false;
    using var uiContext = new WindowsFormsSynchronizationContext();
    SynchronizationContext.SetSynchronizationContext(uiContext);
    try
    {
        DetailPanelChecks.Run(root, Check);
        using var dialog = new ExportSelectionDialog(new[]
        {
            new ModEntry { TextureName = "UT_Hero_Card_135_06", Bundle = "new-hero.bundle", ModifiedAt = now },
            new ModEntry { TextureName = "UT_Hero_Card_101", Bundle = "old.bundle" }
        }, true);
        dialog.StartPosition = FormStartPosition.Manual;
        dialog.Location = new System.Drawing.Point(-2000, -2000);
        dialog.Show();
        Application.DoEvents();
        dialog.PerformLayout();
        using var bitmap = new System.Drawing.Bitmap(dialog.Width, dialog.Height);
        dialog.DrawToBitmap(bitmap, new System.Drawing.Rectangle(0, 0, dialog.Width, dialog.Height));
        bitmap.Save(Path.Combine(AppContext.BaseDirectory, "export-dialog.png"));
        Check(dialog.SelectedEntries.Count == 2, "export selection retains both entries");
        foreach (float scale in new[] { 1f, 1.5f })
        {
            using var animation = new AnimatedPortraitDialog(@"D:\example\AssetBundles", "UT_Hero_Card_101");
            animation.StartPosition = FormStartPosition.Manual;
            animation.Location = new System.Drawing.Point(-3000, -3000);
            animation.Scale(new System.Drawing.SizeF(scale, scale));
            animation.Show();
            Application.DoEvents();
            IEnumerable<Control> Descendants(Control parent) => parent.Controls.Cast<Control>().SelectMany(c => new[] { c }.Concat(Descendants(c)));
            var greenToggle = Descendants(animation).OfType<CheckBox>().Single(c => c.Text == "去绿幕");
            Check(!greenToggle.Checked, "green removal is opt-in at scale " + scale);
            using var shot = new System.Drawing.Bitmap(animation.Width, animation.Height);
            animation.DrawToBitmap(shot, new System.Drawing.Rectangle(0, 0, animation.Width, animation.Height));
            shot.Save(Path.Combine(AppContext.BaseDirectory, $"animation-dialog-{scale}.png"));
        }
        foreach (float scale in new[] { 1f, 1.5f })
        {
            var skillAsset = new TexRef
            {
                BundlePath = Path.Combine(root, "skill.bundle"),
                BundleName = "skill.bundle",
                Name = "Talent-001",
                Display = "技能动画 · Talent-001",
                Kind = ResourceKinds.Texture,
                IsSkillAnimation = true,
                OwnerHeroId = "101",
                Width = 3024,
                Height = 3024
            };
            var skillInfo = new SkillAnimationInfo("Talent-001", 42, 3024, 3024, 375, 350,
                Enumerable.Range(1, 52).Select(i => "Talent_" + i.ToString("000")).ToArray());
            using var skill = new SkillAnimationDialog(skillAsset, skillInfo, Path.Combine(root, "backups"));
            skill.StartPosition = FormStartPosition.Manual;
            skill.Location = new System.Drawing.Point(-3500, -3500);
            skill.Scale(new System.Drawing.SizeF(scale, scale));
            skill.Show();
            Application.DoEvents();
            IEnumerable<Control> SkillControls(Control parent) => parent.Controls.Cast<Control>()
                .SelectMany(c => new[] { c }.Concat(SkillControls(c)));
            var controls = SkillControls(skill).ToArray();
            Check(!controls.OfType<CheckBox>().Single(c => c.Text == "去绿幕").Checked,
                "skill green removal is opt-in at scale " + scale);
            Check(controls.OfType<VideoCropControl>().Single().Visible,
                "skill crop surface is visible at scale " + scale);
            var skillButton = controls.OfType<Button>().Single(c => c.Text == "替换技能动画");
            Check(skillButton.Visible, "skill replacement action is visible at scale " + scale);
            var footer = skill.Controls[0].Controls.OfType<FlowLayoutPanel>().Single();
            Check(footer.Controls.Cast<Control>().Where(c => c.Visible)
                .All(c => c.Left >= 0 && c.Right <= footer.ClientSize.Width && c.Bottom <= footer.ClientSize.Height),
                "skill action row fits at scale " + scale);
            foreach (var button in footer.Controls.OfType<Button>().Where(button => button.Visible))
            {
                var measured = TextRenderer.MeasureText(button.Text, button.Font);
                Check(measured.Width + button.Padding.Horizontal <= button.Width && measured.Height <= button.Height,
                    "skill dialog button text fits: " + button.Text + " at scale " + scale);
            }
            using var shot = new System.Drawing.Bitmap(skill.Width, skill.Height);
            skill.DrawToBitmap(shot, new System.Drawing.Rectangle(0, 0, skill.Width, skill.Height));
            shot.Save(Path.Combine(AppContext.BaseDirectory, $"skill-dialog-{scale}.png"));
            skill.Close();
            Application.DoEvents();
        }
        if (File.Exists(Path.Combine(root, "green animation.gif")))
        {
            using var animation = new AnimatedPortraitDialog(root, "UT_Hero_Card_101", Path.Combine(root, "green animation.gif"));
            animation.StartPosition = FormStartPosition.Manual;
            animation.Location = new System.Drawing.Point(-3000, -3000);
            animation.Show();
            var timeout = System.Diagnostics.Stopwatch.StartNew();
            while (!animation.ResultMessage.StartsWith("取景已就绪") && timeout.Elapsed < TimeSpan.FromSeconds(20))
            {
                Application.DoEvents();
                Thread.Sleep(10);
            }
            Check(animation.ResultMessage.StartsWith("取景已就绪"), "dropping GIF produces adjustable crop without writing");
            IEnumerable<Control> AnimationControls(Control parent) => parent.Controls.Cast<Control>().SelectMany(c => new[] { c }.Concat(AnimationControls(c)));
            var tabs = AnimationControls(animation).OfType<PortraitViewTabs>().Single();
            tabs.SelectedIndex = 1;
            timeout.Restart();
            while (!animation.ResultMessage.StartsWith("预览已就绪") && timeout.Elapsed < TimeSpan.FromSeconds(20))
            {
                Application.DoEvents();
                Thread.Sleep(10);
            }
            Check(animation.ResultMessage.StartsWith("预览已就绪"), "cropped animated preview is ready");
            var picture = AnimationControls(animation).OfType<PictureBox>().Single();
            Check(System.Drawing.ImageAnimator.CanAnimate(picture.Image), "portrait preview contains animated frames");
            Check(PortraitReplacement.ReadReceipt(root) == null, "preview does not install a patch");
            var toggle = animation.Controls[0].Controls.OfType<FlowLayoutPanel>().Single().Controls.OfType<CheckBox>().Single();
            toggle.Checked = true;
            timeout.Restart();
            while (!animation.ResultMessage.StartsWith("预览已就绪") && timeout.Elapsed < TimeSpan.FromSeconds(20))
            {
                Application.DoEvents();
                Thread.Sleep(10);
            }
            Check(animation.ResultMessage.StartsWith("预览已就绪"), "manual green-screen toggle regenerates animated preview");
            using (var frame = new System.Drawing.Bitmap(picture.Image))
                Check(frame.GetPixel(8, 8).A == 0, "checked preview removes green background");
            using var shot = new System.Drawing.Bitmap(animation.Width, animation.Height);
            try
            {
                animation.DrawToBitmap(shot, new System.Drawing.Rectangle(0, 0, animation.Width, animation.Height));
                shot.Save(Path.Combine(AppContext.BaseDirectory, "animation-dialog-ready.png"));
            }
            catch (Exception ex) { Console.WriteLine("SNAPSHOT " + ex); throw; }
            animation.Close();
            Application.DoEvents();
            Check(animation.IsDisposed, "animated preview closes cleanly");
            var receipt = new PortraitReplacement.Receipt("UT_Hero_Card_101", "VHandCard_13021002", "test-video.mp4", false, now,
                Path.Combine(root, "green animation.gif"), replaceTest, restoreTest, restoreTest,
                new() { ["resource.bundle"] = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(target))) });
            var receiptDir = Path.Combine(installRoot, "_原始备份", "动态立绘");
            Directory.CreateDirectory(receiptDir);
            File.WriteAllText(Path.Combine(receiptDir, "current.json"), System.Text.Json.JsonSerializer.Serialize(receipt));
            using var installedDialog = new AnimatedPortraitDialog(installRoot, receipt.Texture);
            installedDialog.StartPosition = FormStartPosition.Manual;
            installedDialog.Location = new System.Drawing.Point(-3000, -3000);
            installedDialog.ClientSize = new System.Drawing.Size(540, 420);
            installedDialog.Show();
            timeout.Restart();
            while (!installedDialog.ResultMessage.StartsWith("已写入并校验") && timeout.Elapsed < TimeSpan.FromSeconds(5))
            {
                Application.DoEvents();
                Thread.Sleep(10);
            }
            Check(installedDialog.ResultMessage.StartsWith("已写入并校验"), "reopened dialog verifies installed status");
            var footer = installedDialog.Controls[0].Controls.OfType<FlowLayoutPanel>().Single();
            Check(footer.Controls.Cast<Control>().Where(c => c.Visible).All(c => c.Right <= footer.ClientSize.Width && c.Bottom <= footer.ClientSize.Height), "installed controls fit compact dialog");
            using var installedShot = new System.Drawing.Bitmap(installedDialog.Width, installedDialog.Height);
            installedDialog.DrawToBitmap(installedShot, new System.Drawing.Rectangle(0, 0, installedDialog.Width, installedDialog.Height));
            installedShot.Save(Path.Combine(AppContext.BaseDirectory, "animation-dialog-installed.png"));
            installedDialog.Close();
            Application.DoEvents();
        }
    }
    catch (Exception ex) { uiError = ex; }
});
ui.SetApartmentState(ApartmentState.STA);
ui.Start();
ui.Join();
if (uiError != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(uiError).Throw();
