using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using JixModMaker;

internal static class VideoSetupChecks
{
    private sealed class ReplyHandler(byte[] bytes, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public int Requests { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Requests++;
            token.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(status) { Content = new ByteArrayContent(bytes), RequestMessage = request });
        }
    }

    public static void Run(Action<bool, string> check)
    {
        string root = Path.Combine(Path.GetTempPath(), "JixVideoSetup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        object previousBase = AppContext.GetData("APP_CONTEXT_BASE_DIRECTORY");
        object previousCache = AppContext.GetData("JixModMaker.VideoRuntimeCache");
        try
        {
            check(VideoRuntime.Components.Count == 3 && VideoRuntime.Components.All(c =>
                new Uri(c.Url).Scheme == "https" && c.Sha256.Length == 64 && c.Size > 0),
                "runtime components have fixed HTTPS sources, lengths and SHA256 pins");
            byte[] bytes = "test-component"u8.ToArray();
            var component = new VideoComponent("fixture", "https://example.test/component.zip",
                Convert.ToHexString(SHA256.HashData(bytes)), bytes.Length, "python");
            string download = Path.Combine(root, "download.zip");
            using var handler = new ReplyHandler(bytes);
            using var client = new HttpClient(handler);
            VideoRuntime.DownloadAsync(client, component, download, null, CancellationToken.None).GetAwaiter().GetResult();
            check(File.ReadAllBytes(download).SequenceEqual(bytes), "download is committed only after size and hash verification");
            VideoRuntime.DownloadAsync(client, component, download, null, CancellationToken.None).GetAwaiter().GetResult();
            check(handler.Requests == 1, "verified download cache is reused without network");
            File.WriteAllBytes(download, new byte[bytes.Length]);
            VideoRuntime.DownloadAsync(client, component, download, null, CancellationToken.None).GetAwaiter().GetResult();
            check(handler.Requests == 2 && File.ReadAllBytes(download).SequenceEqual(bytes), "corrupt download cache is detected and replaced");
            string failed = Path.Combine(root, "failed.zip");
            void Reject(VideoComponent spec, string reason)
            {
                try
                {
                    VideoRuntime.DownloadAsync(client, spec, failed, null, CancellationToken.None).GetAwaiter().GetResult();
                    throw new Exception("invalid download accepted");
                }
                catch (InvalidDataException) { check(!File.Exists(failed) && !File.Exists(failed + ".part"), reason); }
            }
            Reject(component with { Sha256 = new string('0', 64) }, "SHA256 mismatch leaves no executable or partial download");
            Reject(component with { Size = bytes.Length + 1 }, "wrong download size is rejected");
            Reject(component with { Url = "http://example.test/component.zip" }, "insecure component source is rejected");
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            try
            {
                VideoRuntime.DownloadAsync(client, component, failed, null, cancelled.Token).GetAwaiter().GetResult();
                throw new Exception("cancelled download accepted");
            }
            catch (OperationCanceledException) { check(!File.Exists(failed), "cancelled setup produces no runtime"); }
            using (var offline = new HttpClient(new ReplyHandler(bytes, HttpStatusCode.ServiceUnavailable)))
            {
                try { VideoRuntime.DownloadAsync(offline, component, failed, null, CancellationToken.None).GetAwaiter().GetResult(); throw new Exception("HTTP error ignored"); }
                catch (HttpRequestException) { check(!File.Exists(failed), "upstream failure is retryable without partial installation"); }
            }

            string archive = Path.Combine(root, "fixture.zip");
            void Zip(string entry)
            {
                if (File.Exists(archive)) File.Delete(archive);
                using var zip = ZipFile.Open(archive, ZipArchiveMode.Create);
                using var output = new StreamWriter(zip.CreateEntry(entry).Open());
                output.Write("fixture");
            }
            Zip("module/NOTICE.txt");
            VideoRuntime.ExtractArchive(archive, Path.Combine(root, "extracted"), CancellationToken.None);
            check(File.ReadAllText(Path.Combine(root, "extracted/module/NOTICE.txt")) == "fixture", "component metadata and notices survive extraction");
            foreach (string path in new[] { "../escape.txt", "..\\escape.txt", "C:/escape.txt", "module/NOTICE.txt:stream" })
            {
                Zip(path);
                try { VideoRuntime.ExtractArchive(archive, Path.Combine(root, "unsafe"), CancellationToken.None); throw new Exception("unsafe archive accepted"); }
                catch (InvalidDataException) { check(!File.Exists(Path.Combine(root, "escape.txt")), "unsafe ZIP path rejected: " + path); }
            }

            string installed = Path.Combine(root, "current");
            Directory.CreateDirectory(installed);
            File.WriteAllText(Path.Combine(installed, "baseline"), "previous");
            try { VideoRuntime.CommitInstall(Path.Combine(root, "nonexistent"), installed); throw new Exception("missing staging accepted"); }
            catch (DirectoryNotFoundException) { check(File.ReadAllText(Path.Combine(installed, "baseline")) == "previous", "failed activation restores the existing runtime"); }
            string staged = Path.Combine(root, "staged");
            Directory.CreateDirectory(staged);
            File.WriteAllText(Path.Combine(staged, "ready.txt"), "new");
            VideoRuntime.CommitInstall(staged, installed);
            check(!File.Exists(Path.Combine(installed, "baseline")) && File.Exists(Path.Combine(installed, "ready.txt")), "complete runtime is activated with a directory swap");

            AppContext.SetData("APP_CONTEXT_BASE_DIRECTORY", Path.Combine(root, "program") + Path.DirectorySeparatorChar);
            AppContext.SetData("JixModMaker.VideoRuntimeCache", Path.Combine(root, "cache"));
            Directory.CreateDirectory(VideoRuntime.ScriptRoot);
            File.WriteAllText(Path.Combine(VideoRuntime.ScriptRoot, "mux.py"), "# fixture");
            File.WriteAllText(Path.Combine(VideoRuntime.ScriptRoot, "inspect_movie.py"), "# fixture");
            void MockRuntime(string target)
            {
                Directory.CreateDirectory(Path.Combine(target, "python/cricodecs"));
                File.WriteAllBytes(Path.Combine(target, "ffmpeg.exe"), bytes);
                File.WriteAllBytes(Path.Combine(target, "python/python.exe"), bytes);
                File.WriteAllBytes(Path.Combine(target, "python/cricodecs/__init__.pyd"), bytes);
            }
            MockRuntime(VideoRuntime.InstalledRoot);
            check(!VideoRuntime.IsReady, "incomplete staged runtime is never selected without activation marker");
            File.WriteAllText(Path.Combine(VideoRuntime.InstalledRoot, "ready.txt"), VideoRuntime.RuntimeVersion);
            check(VideoRuntime.IsReady && VideoRuntime.Root == VideoRuntime.InstalledRoot, "standard package automatically resolves downloaded components");
            check(PortraitVideoSettings.Load().Ffmpeg == VideoRuntime.FfmpegPath, "conversion and previews use the installed FFmpeg");
            MockRuntime(VideoRuntime.ScriptRoot);
            check(VideoRuntime.Root == VideoRuntime.ScriptRoot, "existing full packages keep their bundled runtime");
        }
        finally
        {
            AppContext.SetData("APP_CONTEXT_BASE_DIRECTORY", previousBase);
            AppContext.SetData("JixModMaker.VideoRuntimeCache", previousCache);
            Directory.Delete(root, true);
        }
    }

    public static async Task SmokeAsync(string packageData, string work, Action<bool, string> check)
    {
        work = Path.GetFullPath(work);
        Directory.CreateDirectory(work);
        AppContext.SetData("APP_CONTEXT_BASE_DIRECTORY", Path.GetFullPath(packageData) + Path.DirectorySeparatorChar);
        AppContext.SetData("JixModMaker.VideoRuntimeCache", Path.Combine(work, "用户视频组件"));
        check(!VideoRuntime.HasFiles(VideoRuntime.ScriptRoot), "public package has no hidden local conversion binaries");
        await VideoRuntime.InstallAsync(new Progress<string>(Console.WriteLine), CancellationToken.None);
        check(VideoRuntime.IsReady && VideoRuntime.Root == VideoRuntime.InstalledRoot, "first-run upstream installation is ready without system Python or FFmpeg");
        var tools = PortraitVideoSettings.Load();
        string input = Path.Combine(work, "test source.mp4");
        await PortraitVideoConverter.RunAsync(tools.Ffmpeg, new[] { "-nostdin", "-v", "error", "-y", "-f", "lavfi", "-i",
            "color=c=green:s=320x240:r=30,drawbox=x=80:y=40:w=120:h=140:color=red:t=fill", "-t", "1", "-c:v", "mpeg4", input }, CancellationToken.None);
        string gif = Path.Combine(work, "preview.gif");
        var options = new PortraitVideoConverter.Options(true, Crop: VideoCrop.Fit(320, 240, 880, 1205));
        await PortraitVideoConverter.PreviewAnimationAsync(input, gif, tools, options, CancellationToken.None);
        check(new FileInfo(gif).Length > 100, "downloaded runtime generates green-screen and cropped animated previews");
        string usm = await PortraitVideoConverter.ConvertAsync(input, work, tools, options, CancellationToken.None);
        check(File.ReadAllBytes(usm).AsSpan().StartsWith("CRID"u8), "downloaded runtime converts MP4 into color and alpha USM");
        string metadata = Path.Combine(work, "movie.json");
        await PortraitVideoConverter.RunAsync(VideoRuntime.PythonPath,
            new[] { Path.Combine(VideoRuntime.ScriptRoot, "inspect_movie.py"), usm, metadata }, CancellationToken.None);
        var timing = System.Text.Json.JsonSerializer.Deserialize<NativeMovieTiming>(await File.ReadAllTextAsync(metadata));
        check(timing.HasAlpha && timing.TotalFrames == 30 && timing.Height == 240, "packaged inspection script reads the generated transparent movie and frame count");
        await VideoRuntime.ValidateAsync(VideoRuntime.Root, CancellationToken.None);
        check(true, "installed components start again offline from the user cache");
    }

    public static void Ui(string output, Action<bool, string> check)
    {
        Directory.CreateDirectory(output);
        Exception failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
                Application.EnableVisualStyles();
                IEnumerable<Control> All(Control control) => control.Controls.Cast<Control>().SelectMany(child => new[] { child }.Concat(All(child)));
                foreach (float scale in new[] { 1f, 1.5f })
                {
                    using var dialog = new VideoRuntimeDialog();
                    dialog.StartPosition = FormStartPosition.Manual;
                    dialog.Location = new(-4000, -4000);
                    dialog.Scale(new System.Drawing.SizeF(scale, scale));
                    dialog.Show();
                    Application.DoEvents();
                    using (var screenshot = new System.Drawing.Bitmap(dialog.Width, dialog.Height))
                    {
                        dialog.DrawToBitmap(screenshot, new System.Drawing.Rectangle(0, 0, dialog.Width, dialog.Height));
                        screenshot.Save(Path.Combine(output, "video-setup-" + scale + ".png"));
                    }
                    check(dialog.Text.Contains(AppBuildInfo.Version), "component setup identifies current version at " + scale);
                    var buttons = All(dialog).OfType<Button>().ToArray();
                    check(buttons.Length == 2 && buttons.All(button => button.Visible && button.Enabled), "component setup offers enable and cancel at " + scale);
                    foreach (var button in buttons)
                        check(button.Right <= button.Parent.ClientSize.Width && button.Bottom <= button.Parent.ClientSize.Height,
                            "component setup button fits: " + button.Text + " at " + scale);
                    foreach (var label in All(dialog).OfType<Label>())
                    {
                        var needed = TextRenderer.MeasureText(label.Text, label.Font, new System.Drawing.Size(label.ClientSize.Width, int.MaxValue),
                            TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.TextBoxControl);
                        check(label.Height - label.Padding.Vertical >= needed.Height,
                            $"component setup text is not clipped at {scale}: {label.Text.Split('\n')[0]} ({label.Height - label.Padding.Vertical}/{needed.Height})");
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
    }

    public static void FirstVideoUi(string packageData, string smokeRoot, Action<bool, string> check)
    {
        smokeRoot = Path.GetFullPath(smokeRoot);
        AppContext.SetData("APP_CONTEXT_BASE_DIRECTORY", Path.GetFullPath(packageData) + Path.DirectorySeparatorChar);
        string cache = Path.Combine(smokeRoot, "ui-flow-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(cache, "downloads"));
        foreach (string archive in Directory.EnumerateFiles(Path.Combine(smokeRoot, "用户视频组件/downloads")))
            File.Copy(archive, Path.Combine(cache, "downloads", Path.GetFileName(archive)));
        AppContext.SetData("JixModMaker.VideoRuntimeCache", cache);
        Exception failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
                Application.EnableVisualStyles();
                Control.CheckForIllegalCrossThreadCalls = true;
                bool clicked = false;
                using var timer = new System.Windows.Forms.Timer { Interval = 100 };
                var elapsed = System.Diagnostics.Stopwatch.StartNew();
                IEnumerable<Control> All(Control control) => control.Controls.Cast<Control>().SelectMany(child => new[] { child }.Concat(All(child)));
                timer.Tick += (_, _) =>
                {
                    var setup = Application.OpenForms.OfType<VideoRuntimeDialog>().FirstOrDefault();
                    if (setup == null) return;
                    setup.Location = new(-4000, -4000);
                    if (elapsed.Elapsed > TimeSpan.FromSeconds(60)) { setup.Close(); return; }
                    if (!clicked)
                    {
                        All(setup).OfType<Button>().Single(button => button.Text == "下载并启用").PerformClick();
                        clicked = true;
                    }
                };
                timer.Start();
                using var dialog = new AnimatedPortraitDialog(Path.Combine(smokeRoot, "empty-game"), "UT_Hero_Card_101",
                    Path.Combine(smokeRoot, "test source.mp4"), new(880, 1205));
                dialog.StartPosition = FormStartPosition.Manual;
                dialog.Location = new(-4000, -4000);
                dialog.Show();
                while (!dialog.ResultMessage.StartsWith("取景已就绪") && elapsed.Elapsed < TimeSpan.FromSeconds(65))
                {
                    Application.DoEvents();
                    Thread.Sleep(10);
                }
                timer.Stop();
                check(clicked && VideoRuntime.IsReady, "first video import opens one-click setup and activates the downloaded runtime");
                check(dialog.ResultMessage.StartsWith("取景已就绪"), "video import resumes automatically after component setup: " + dialog.ResultMessage);
                check(!Directory.Exists(Path.Combine(smokeRoot, "empty-game")), "component setup and initial preview never write game resources");
                dialog.Close();
                Application.DoEvents();
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
