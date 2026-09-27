using System.Drawing;
using System.Text;
using System.Text.Json;
using System.Reflection;
using JixModMaker;

internal static class SkillMovieChecks
{
    public static void Run(Action<bool, string> check)
    {
        foreach (var (name, skin, monster) in new[]
        {
            ("VSkill_Hero101", "原皮", false), ("VSkill_Hero101_02", "皮肤02", false),
            ("VSkill_Hero101_MAX", "Max 皮肤", false), ("VSkill_Hero101_Max_sfw", "Max 皮肤", false),
            ("VSkill_Monster_1011", "原皮", true), ("VSkill_Hero103__sfw", "原皮", false)
        })
        {
            var parsed = NameParser.Parse(name);
            check(parsed.Kind == "SkillMovie" && parsed.Skin == skin && parsed.IsMonster == monster,
                "native cut-in parses exact character/skin: " + name);
        }
        check(NameParser.Parse("VSkill_Hero103__sfw").Sfw, "double-underscore safe variant stays distinct");
        check(!NameParser.IsSkillMovie("Talent-001") && !NameParser.IsSkillMovie("VHandCard_13021002"),
            "chibi atlases and hand-card slots are not skill cut-ins");
        check(ResourceKinds.InBrowser(ResourceKinds.SkillMovie, ResourceKinds.Texture), "native cut-ins share the portrait browser");
        var workspace = new ModManifest();
        PackService.Upsert(workspace, new ModEntry { Bundle = "skill.bundle", PathId = -42, TextureName = "VSkill_Hero101", Kind = ResourceKinds.SkillMovie });
        check(PackService.Contains(workspace, "skill.bundle", -42), "negative Unity path IDs retain modified status");
        string root = Path.Combine(Path.GetTempPath(), "JixSkillCatalog-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string main = new('a', 32), shared = new('b', 32);
            using var keys = new MemoryStream();
            using var buckets = new MemoryStream();
            using (var writer = new BinaryWriter(buckets, Encoding.UTF8, true))
            {
                writer.Write(3);
                foreach (var (key, ids) in new[]
                {
                    ("VSkill_Hero101_02", new[] { 0 }), ("deps", new[] { 1, 2 }),
                    ("VSkill_Hero102", new[] { 2 })
                })
                {
                    writer.Write((int)keys.Position);
                    using (var keyWriter = new BinaryWriter(keys, Encoding.UTF8, true))
                    {
                        var bytes = Encoding.ASCII.GetBytes(key);
                        keyWriter.Write((byte)0); keyWriter.Write(bytes.Length); keyWriter.Write(bytes);
                    }
                    writer.Write(ids.Length);
                    foreach (int id in ids) writer.Write(id);
                }
            }
            using var entries = new MemoryStream();
            using (var writer = new BinaryWriter(entries, Encoding.UTF8, true))
            {
                writer.Write(3);
                foreach (var row in new[] { new[] { 0, 0, 1, 0, 0, 0, 0 }, new[] { 1, 0, -1, 0, 0, 0, 1 }, new[] { 2, 0, -1, 0, 0, 0, 1 } })
                    foreach (int value in row) writer.Write(value);
            }
            File.WriteAllText(Path.Combine(root, "catalog.json"), JsonSerializer.Serialize(new
            {
                m_KeyDataString = Convert.ToBase64String(keys.ToArray()), m_BucketDataString = Convert.ToBase64String(buckets.ToArray()),
                m_EntryDataString = Convert.ToBase64String(entries.ToArray()),
                m_InternalIds = new[] { "f", "StandaloneWindows64/" + main + ".bundle", "StandaloneWindows64/" + shared + ".bundle" },
                m_resourceTypes = new[] { new { m_ClassName = "CriWare.Assets.CriManaUsmAsset" }, new { m_ClassName = "IAssetBundleResource" } }
            }));
            var movies = CatalogOwnership.LoadSkillMovies(root, false);
            check(movies.Count == 1 && movies[0].Name == "VSkill_Hero101_02" && movies[0].Bundle == main + ".bundle",
                "catalog maps primary movie bundle, never shared script dependency or wrong asset type");
            string wrapped = Path.Combine(root, "cache", main);
            Directory.CreateDirectory(wrapped);
            File.WriteAllBytes(Path.Combine(wrapped, "__data"), new byte[] { 0 });
            File.WriteAllText(Path.Combine(wrapped, "__info"), "fixture");
            var index = new GameIndex
            {
                GameDir = root, IncludeHotCache = false,
                BundleStamps = new() { ["cache/" + main + "/__data"] = Path.Combine(wrapped, "__data") + "|1|0" }
            };
            typeof(IndexService).GetMethod("AddSkillMovies", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { index });
            check(index.Items.Single(item => item.Kind == ResourceKinds.SkillMovie).BundlePath == Path.Combine(wrapped, "__data"),
                "local wrapped movie index resolves catalog hash to nested __data");
        }
        finally { Directory.Delete(root, true); }
    }

    public static async Task SmokeAsync(string original, string name, string root, Action<bool, string> check)
    {
        if (Directory.Exists(root)) throw new IOException("Smoke destination must be new");
        Directory.CreateDirectory(root);
        string originalHash = SkillMovieEngine.Hash(original);
        string copy = Path.Combine(root, "skill.bundle");
        File.Copy(original, copy);
        var info = await SkillMovieEngine.InspectAsync(copy, name, CancellationToken.None);
        check(info.Loop == 0 && info.Timing.TotalFrames > 1, "real cut-in is finite and non-looping");
        Console.WriteLine($"NATIVE {info.Timing.Width}x{info.Timing.Height}, {info.Timing.TotalFrames} frames, {info.Timing.Duration:0.###} seconds");
        await File.WriteAllBytesAsync(Path.Combine(root, "original.png"), await SkillMovieEngine.PreviewAsync(copy, name, false, CancellationToken.None));
        await File.WriteAllBytesAsync(Path.Combine(root, "original.gif"), await SkillMovieEngine.PreviewAsync(copy, name, true, CancellationToken.None));
        string input = Path.Combine(root, "short.mp4");
        await PortraitVideoConverter.RunAsync(PortraitVideoSettings.Load().Ffmpeg, new[]
        {
            "-nostdin", "-v", "error", "-y", "-f", "lavfi", "-i",
            "color=c=0x006000:s=1920x1080:r=30:d=0.4,drawbox=x=650:y=350:w=240:h=240:color=red:t=fill",
            "-c:v", "libx264", "-crf", "10", input
        }, CancellationToken.None);
        var asset = new TexRef { BundlePath = copy, BundleName = "skill.bundle", Name = name, Kind = ResourceKinds.SkillMovie, PathId = info.PathId };
        string backupDir = Path.Combine(root, "_原始备份");
        await SkillMovieEngine.ReplaceAsync(asset, info, input, backupDir, Path.Combine(root, "write"),
            new(RemoveGreen: true, Crop: VideoCrop.Fit(1920, 1080, info.Timing.Width, info.Timing.Height)), CancellationToken.None);
        var after = await SkillMovieEngine.InspectAsync(copy, name, CancellationToken.None);
        check(after.Timing == info.Timing && after.Loop == info.Loop && after.PathId == info.PathId,
            "native geometry, exact frame count, timing, alpha and non-looping flags survive replacement");
        check(after.BundleHash != info.BundleHash, "native movie copy actually changed");
        check(SkillMovieEngine.Hash(original) == originalHash, "live game bundle remains byte-identical");
        byte[] preview = await SkillMovieEngine.PreviewAsync(copy, name, false, CancellationToken.None);
        await File.WriteAllBytesAsync(Path.Combine(root, "replaced.png"), preview);
        using (var image = new Bitmap(new MemoryStream(preview)))
        {
            check(image.GetPixel(8, 8).A < 5, "dark green is removed from written USM, not only source preview");
            int x0 = image.Width, y0 = image.Height, x1 = -1, y1 = -1;
            for (int y = 0; y < image.Height; y++)
            for (int x = 0; x < image.Width; x++)
            {
                var pixel = image.GetPixel(x, y);
                if (pixel.A < 200 || pixel.R < 150 || pixel.G > 80) continue;
                x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); x1 = Math.Max(x1, x); y1 = Math.Max(y1, y);
            }
            check(x1 > x0 && y1 > y0 && Math.Abs((x1 - x0 + 1d) / (y1 - y0 + 1) - 1) < .025,
                "square marker stays square after crop, MPEG/alpha encode and native bundle decoding");
        }
        try
        {
            await SkillMovieEngine.ReplaceAsync(asset, info, input, backupDir, Path.Combine(root, "stale"), new(), CancellationToken.None);
            throw new Exception("stale bundle hash accepted");
        }
        catch (IOException) { check(SkillMovieEngine.Hash(copy) == after.BundleHash, "stale window cannot overwrite newer edits"); }
        string zip = Path.Combine(root, "skill.zip");
        ReplacementZip.Export(root, new[] { copy }, zip);
        using (var archive = System.IO.Compression.ZipFile.OpenRead(zip))
            check(archive.Entries.Count == 1 && archive.Entries[0].FullName == "skill.bundle", "skill export contains only its native bundle");
        check(SkillMovieEngine.Restore(asset, after, backupDir), "native skill bundle restores through verified backup");
        check(SkillMovieEngine.Hash(copy) == info.BundleHash && SkillMovieEngine.Hash(original) == originalHash,
            "restored copy is byte-identical and live source untouched");
    }

    public static void RunUi(string root, Action<bool, string> check)
    {
        foreach (float scale in new[] { 1f, 1.5f })
        {
            var asset = new TexRef { Name = "VSkill_Hero101", BundlePath = Path.Combine(root, "native.bundle"), BundleName = "native.bundle", Kind = ResourceKinds.SkillMovie, Display = "技能特写" };
            using var dialog = new SkillAnimationDialog(asset, new SkillMovieInfo(asset.Name, -42, 0, new(1504, 1080, 30, 1, 40, true, false), "test"), Path.Combine(root, "backup"));
            dialog.StartPosition = FormStartPosition.Manual;
            dialog.Location = new(-5000, -5000);
            dialog.Scale(new SizeF(scale, scale));
            dialog.Show();
            Application.DoEvents();
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
            var timing = (Label)typeof(SkillAnimationDialog).GetField("_timing", flags).GetValue(dialog);
            check(timing.Visible && timing.Text.Contains("1.333") && timing.Text.Contains("40 帧"), "native duration notice is persistent before importing: " + scale);
            typeof(SkillAnimationDialog).GetField("_sourceDuration", flags).SetValue(dialog, (double?)10);
            typeof(SkillAnimationDialog).GetMethod("UpdateTimingNotice", flags).Invoke(dialog, null);
            Application.DoEvents();
            check(timing.Text.Contains("8.667") && timing.ForeColor == Theme.Accent, "long clip has a visible truncation warning: " + scale);
            check(timing.Height >= timing.GetPreferredSize(new Size(timing.Width, 0)).Height && timing.Right <= timing.Parent.ClientSize.Width,
                "duration warning wraps without clipping: " + scale);
            var footer = dialog.Controls[0].Controls.OfType<FlowLayoutPanel>().Single();
            check(footer.Controls.OfType<Button>().Any(button => button.Visible && button.Text == "替换技能特写"), "native movie dialog has an explicit replacement action: " + scale);
            foreach (var button in footer.Controls.OfType<Button>().Where(button => button.Visible))
                check(button.Right <= footer.Width && button.Bottom <= footer.Height && TextRenderer.MeasureText(button.Text, button.Font).Width <= button.Width,
                    "native movie dialog controls fit: " + button.Text + " at " + scale);
            using var bitmap = new Bitmap(dialog.Width, dialog.Height);
            dialog.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            bitmap.Save(Path.Combine(AppContext.BaseDirectory, $"native-skill-{scale}.png"));
            dialog.Close();
            Application.DoEvents();
        }
    }

    private sealed class BrowserForm : MainForm
    {
        protected override void OnShown(EventArgs e) { }
    }

    public static void BrowserUi(string gameDir, string output, Action<bool, string> check)
    {
        Exception failure = null;
        var thread = new Thread(() =>
        {
            Control.CheckForIllegalCrossThreadCalls = true;
            WindowsFormsSynchronizationContext.AutoInstall = false;
            using var context = new WindowsFormsSynchronizationContext();
            SynchronizationContext.SetSynchronizationContext(context);
            try
            {
                Directory.CreateDirectory(output);
                var index = new IndexService().Load(gameDir, false, true, false) ?? throw new Exception("Missing current index");
                const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
                foreach (float scale in new[] { 1f, 1.5f })
                {
                    using var form = new BrowserForm { StartPosition = FormStartPosition.Manual, Location = new(-5000, -5000) };
                    void Set(string name, object value) => typeof(MainForm).GetField(name, flags).SetValue(form, value);
                    object Field(string name) => typeof(MainForm).GetField(name, flags).GetValue(form);
                    object Call(string method, params object[] values) => typeof(MainForm).GetMethod(method, flags).Invoke(form, values);
                    Set("_index", index);
                    Set("_ws", new ModManifest());
                    Set("_selectedCategoryId", ResourceCategories.CharacterId);
                    Set("_selectedHeroGroupKey", "角色 101");
                    Set("_selectedHeroSkin", "原皮");
                    form.Show();
                    form.Scale(new SizeF(scale, scale));
                    form.MinimumSize = Size.Empty;
                    form.Size = new(1400, 900);
                    Call("Navigate", "browse");
                    var entry = index.Items.Single(item => item.Name == "VSkill_Hero101");
                    var asset = (TexRef)Call("ToTexRef", entry);
                    Call("SelectAsset", asset, null);
                    var preview = (PictureBox)Field("_preview");
                    var timer = System.Diagnostics.Stopwatch.StartNew();
                    while (preview.Image == null && timer.Elapsed < TimeSpan.FromSeconds(30)) { Application.DoEvents(); Thread.Sleep(15); }
                    check(preview.Image != null && ImageAnimator.CanAnimate(preview.Image), "real native cut-in animates in detail preview: " + scale);
                    check(((Label)Field("_detailHint")).Text.Contains("1.333"), "native duration is shown in the browser without opening replacement: " + scale);
                    var title = (Label)Field("_detailTitle");
                    check(title.Text.Contains("1.333") && title.Text.Contains("40 帧") && title.Bottom <= title.Parent.Parent.ClientSize.Height,
                        "native duration appears under the resource name without scrolling: " + scale);
                    var flow = (FlowLayoutPanel)Field("_flow");
                    check(flow.Controls.Cast<Control>().Any(control => control.Tag is TexRef item && item.Name == "VSkill_Hero101"),
                        "native skill cut-in appears beside Hero101 original-skin portraits: " + scale);
                    var button = (Button)Field("_replaceAnimationBtn");
                    check(button.Enabled && button.Text == "替换技能特写", "real selected video enables skill replacement: " + scale);
                    timer.Restart();
                    while (timer.Elapsed < TimeSpan.FromSeconds(3)) { Application.DoEvents(); Thread.Sleep(15); }
                    using (var bitmap = new Bitmap(form.Width, form.Height))
                    {
                        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                        bitmap.Save(Path.Combine(output, $"browser-{scale}.png"));
                    }
                    foreach (var page in new[] { "tools", "pack" })
                    {
                        Call("Navigate", page);
                        Application.DoEvents();
                    }
                    form.Close();
                    Application.DoEvents();
                }
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure != null) throw failure;
    }
}
