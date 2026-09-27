using JixModMaker;

internal static class VideoRuntimeChecks
{
    public static void Run(Action<bool, string> check)
    {
        string root = Path.Combine(Path.GetTempPath(), "JixMissingRuntime-" + Guid.NewGuid().ToString("N"));
        object previousBase = AppContext.GetData("APP_CONTEXT_BASE_DIRECTORY");
        object previousCache = AppContext.GetData("JixModMaker.VideoRuntimeCache");
        Directory.CreateDirectory(root);
        try
        {
            AppContext.SetData("APP_CONTEXT_BASE_DIRECTORY", root + Path.DirectorySeparatorChar);
            AppContext.SetData("JixModMaker.VideoRuntimeCache", Path.Combine(root, "cache"));
            string source = Path.Combine(root, "input.mp4");
            File.WriteAllBytes(source, new byte[] { 1 });
            string work = Path.Combine(root, "output");
            Directory.CreateDirectory(work);
            // An available executable models FFmpeg on PATH; preflight must reject the core package before launching it.
            var settings = new PortraitVideoSettings { Ffmpeg = Environment.ProcessPath! };
            try
            {
                PortraitVideoConverter.ConvertAsync(source, work, settings, new(), CancellationToken.None).GetAwaiter().GetResult();
                throw new Exception("A core package reached video conversion.");
            }
            catch (FileNotFoundException ex)
            {
                check(ex.Message.Contains("Python") && ex.Message.Contains("CriCodecs"),
                    "core package with available FFmpeg reports missing mux runtime before encoding");
                check(!Directory.EnumerateFileSystemEntries(work).Any(), "missing video runtime creates no conversion output");
            }

            string tools = Path.Combine(root, "Tools", "video");
            Directory.CreateDirectory(Path.Combine(tools, "python"));
            File.WriteAllBytes(Path.Combine(tools, "python", "python.exe"), new byte[] { 1 });
            File.WriteAllText(Path.Combine(tools, "mux.py"), "# fixture");
            try
            {
                PortraitVideoConverter.EnsureConversionAvailableAsync(settings, CancellationToken.None).GetAwaiter().GetResult();
                throw new Exception("Missing CriCodecs was accepted.");
            }
            catch (FileNotFoundException ex)
            {
                check(ex.Message.Contains("CriCodecs") && !ex.Message.Contains("Python"),
                    "partially extracted runtime is rejected before Python launch");
            }
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            try
            {
                PortraitVideoConverter.EnsureConversionAvailableAsync(settings, cancelled.Token).GetAwaiter().GetResult();
                throw new Exception("Cancelled preflight was accepted.");
            }
            catch (OperationCanceledException) { check(true, "video runtime preflight honors cancellation"); }
        }
        finally
        {
            AppContext.SetData("APP_CONTEXT_BASE_DIRECTORY", previousBase);
            AppContext.SetData("JixModMaker.VideoRuntimeCache", previousCache);
            Directory.Delete(root, true);
        }
    }
}
