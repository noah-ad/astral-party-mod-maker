using System.Diagnostics;
using System.Text.Json;
using System.Globalization;
using System.Text.RegularExpressions;

namespace JixModMaker;

public sealed class PortraitVideoSettings
{
    public string Ffmpeg { get; set; } = "ffmpeg.exe";
    private static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JixModMaker", "video-tools.json");
    public static PortraitVideoSettings Load()
    {
        string bundled = VideoRuntime.FfmpegPath;
        if (File.Exists(bundled)) return new() { Ffmpeg = bundled };
        try { return JsonSerializer.Deserialize<PortraitVideoSettings>(File.ReadAllText(FilePath)) ?? new(); }
        catch { return new(); }
    }
    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this));
    }
}

public static class PortraitVideoConverter
{
    private static string PythonPath => VideoRuntime.PythonPath;
    private static string MuxPath => Path.Combine(VideoRuntime.ScriptRoot, "mux.py");

    public static async Task EnsureConversionAvailableAsync(PortraitVideoSettings tools, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var missing = new List<string>();
        if (!Available(tools.Ffmpeg)) missing.Add("FFmpeg");
        if (!File.Exists(PythonPath)) missing.Add("Python");
        if (!File.Exists(MuxPath)) missing.Add("USM 封装脚本");
        if (!File.Exists(Path.Combine(VideoRuntime.ScriptRoot, "inspect_movie.py"))) missing.Add("视频检查脚本");
        string codecs = Path.Combine(VideoRuntime.Root, "python", "cricodecs");
        if (!Directory.Exists(codecs) || !Directory.EnumerateFiles(codecs, "__init__*.pyd").Any()) missing.Add("CriCodecs");
        if (missing.Count > 0)
            throw new FileNotFoundException("视频转换组件缺失（" + string.Join("、", missing) + "）。请在“工具 / 维护”中启用视频组件，或拖入视频后点击“下载并启用”。");
        try
        {
            await RunAsync(PythonPath, new[] { "-I", "-c", "from cricodecs import usm, video" }, token);
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception)
        {
            throw new IOException("视频转换组件无法启动，请在“工具 / 维护”中检查或修复视频组件。" + ex.Message, ex);
        }
    }

    public sealed record Options(bool RemoveGreen = false, double Similarity = .25, double Softness = .10, VideoCrop Crop = null);
    public static string Filter(Options options, int size = 2048)
    {
        if (options.Similarity < .01 || options.Similarity > .8 || options.Softness < .01 || options.Softness > .5)
            throw new ArgumentOutOfRangeException(nameof(options));
        string basis = "scale=w='iw*sar':h=ih:flags=lanczos,setsar=1,format=rgba," + (options.Crop == null ? "" : options.Crop.Filter() + ",") +
            $"scale=w='min({size},iw)':h='min({size},ih)':force_original_aspect_ratio=decrease:flags=lanczos,setsar=1,format=rgba";
        // Normalized green dominance is insensitive to screen brightness. Preserve source alpha.
        string dominance = "(g(X,Y)-max(r(X,Y),b(X,Y)))/max(g(X,Y),1)";
        string key = options.RemoveGreen
            ? FormattableString.Invariant($",format=gbrap,geq=r='r(X,Y)':g='g(X,Y)':b='b(X,Y)':a='alpha(X,Y)*clip(({(1 - options.Similarity) * .35:0.000}+{options.Softness:0.000}-{dominance})/{options.Softness:0.000},0,1)',format=rgba,despill=type=green:mix=0.5") : "";
        // MPEG's 4:2:0 planes require even dimensions, not an eight-pixel padded canvas.
        return basis + key + ",scale=w='max(2,trunc(iw/2)*2)':h='max(2,trunc(ih/2)*2)':flags=lanczos,setsar=1,format=rgba";
    }

    public static async Task PreviewAsync(string input, string output, PortraitVideoSettings tools, Options options, CancellationToken token)
    {
        await RunAsync(tools.Ffmpeg, new[] { "-nostdin", "-v", "error", "-y", "-i", input, "-an", "-vf", Filter(options, 640), "-frames:v", "1", output }, token);
    }

    public static Task SourceFrameAsync(string input, string output, PortraitVideoSettings tools, CancellationToken token) =>
        RunAsync(tools.Ffmpeg, new[] { "-nostdin", "-v", "error", "-y", "-i", input, "-an", "-vf", "scale=iw*sar:ih,setsar=1", "-frames:v", "1", output }, token);

    public static async Task<double?> ProbeDurationAsync(string input, PortraitVideoSettings tools, CancellationToken token)
    {
        // The portable runtime has FFmpeg but not FFprobe. Read its input header without decoding the clip.
        string log = await RunCapturedAsync(tools.Ffmpeg, new[] { "-nostdin", "-hide_banner", "-v", "info", "-i", input,
            "-map", "0:v:0", "-c:v", "copy", "-frames:v", "0", "-an", "-sn", "-dn", "-f", "null", "-" }, token, true);
        var match = Regex.Match(log, @"Duration:\s*(\d+):(\d{2}):(\d{2}(?:\.\d+)?)", RegexOptions.CultureInvariant);
        if (!match.Success) return null;
        double seconds = double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) * 3600 +
            double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture) * 60 +
            double.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
        return double.IsFinite(seconds) && seconds > 0 ? seconds : null;
    }

    public static async Task PreviewAnimationAsync(string input, string output, PortraitVideoSettings tools, Options options, CancellationToken token,
        NativeMovieTiming target = null)
    {
        string filter = Filter(options, 960) + NativeTimingFilter(target) + ",fps=15,split[p][q];[p]palettegen=reserve_transparent=1:stats_mode=single[palette];[q][palette]paletteuse=new=1:alpha_threshold=128";
        await RunAsync(tools.Ffmpeg, new[] { "-nostdin", "-v", "error", "-y", "-t", "6", "-i", input, "-an", "-filter_complex", filter, "-loop", "0", output }, token);
    }

    private static string NativeTimingFilter(NativeMovieTiming target) => target == null ? "" : FormattableString.Invariant(
        $",fps={target.FramerateN}/{target.FramerateD},tpad=stop_mode=clone:stop_duration={target.Duration:0.000000},trim=end_frame={target.TotalFrames},setpts=PTS-STARTPTS");

    public static async Task<string> ConvertAsync(string input, string work, PortraitVideoSettings tools, Options options,
        CancellationToken token, IProgress<string> progress = null, NativeMovieTiming target = null)
    {
        if (Path.GetExtension(input).Equals(".usm", StringComparison.OrdinalIgnoreCase)) return input;
        await EnsureConversionAvailableAsync(tools, token);
        string color = Path.Combine(work, "color.m1v");
        string alpha = Path.Combine(work, "alpha.m1v");
        string usm = Path.Combine(work, "portrait.usm");
        progress?.Report("正在转换视频并处理透明通道…");
        // Copy coverage directly into Y, without gray-to-YUV studio-range remapping.
        // The range tag prevents the encoder from inserting another range conversion.
        // MPEG-1 cannot signal BT.709; convert RGB to its standard BT.601 color matrix explicitly.
        target?.Validate();
        string fps = target == null ? "30" : $"{target.FramerateN}/{target.FramerateD}";
        string native = target == null ? "" : FormattableString.Invariant(
            $",scale={target.Width}:{target.Height}:force_original_aspect_ratio=decrease:flags=lanczos,pad={target.Width}:{target.Height}:(ow-iw)/2:(oh-ih)/2:color=black@0,setsar=1");
        string timing = NativeTimingFilter(target);
        string filter = Filter(options, target == null ? 2048 : Math.Max(target.Width, target.Height)) + native + ",fps=" + fps + timing + ",format=rgba,split[rgb][mask];" +
            "[rgb]scale=out_color_matrix=bt601:out_range=limited:flags=lanczos,format=yuv420p[color];" +
            "[mask]alphaextract,split[y][uv];" +
            "[uv]scale=iw/2:ih/2:flags=neighbor,lut=y=128[uv128];" +
            "[y][uv128]mergeplanes=map0s=0:map0p=0:map1s=1:map1p=0:map2s=1:map2p=0:format=yuv420p,setrange=limited[a]";
        await RunAsync(tools.Ffmpeg, new[] { "-nostdin", "-v", "error", "-y", "-i", input, "-filter_complex", filter,
            "-map", "[color]", "-an", "-r", fps, "-frames:v", (target?.TotalFrames ?? int.MaxValue).ToString(), "-c:v", "mpeg1video", "-q:v", "1", "-qmin", "1", "-bf", "0", "-g", "15", "-pix_fmt", "yuv420p", "-fs", "134217728", color,
            "-map", "[a]", "-an", "-r", fps, "-frames:v", (target?.TotalFrames ?? int.MaxValue).ToString(), "-c:v", "mpeg1video", "-q:v", "1", "-qmin", "1", "-bf", "0", "-g", "15", "-pix_fmt", "yuv420p", "-fs", "134217728", alpha }, token);
        if (new FileInfo(color).Length >= 134_000_000 || new FileInfo(alpha).Length >= 134_000_000)
            throw new InvalidDataException("视频超过当前转换容量，请使用更短的视频。");
        progress?.Report("正在封装并校验透明动画…");
        await RunAsync(PythonPath, new[] { MuxPath, color, alpha, usm }, token);
        if (!File.Exists(usm)) throw new InvalidDataException("编码器未生成 USM 文件");
        return usm;
    }

    public static bool Available(string executable) => File.Exists(executable) ||
        (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
            .Any(p => File.Exists(Path.Combine(p.Trim('"'), executable)));

    public static async Task RunAsync(string executable, IEnumerable<string> arguments, CancellationToken token)
        => await RunCapturedAsync(executable, arguments, token);

    private static async Task<string> RunCapturedAsync(string executable, IEnumerable<string> arguments, CancellationToken token, bool keepHeader = false)
    {
        token.ThrowIfCancellationRequested();
        if (!Available(executable)) throw new FileNotFoundException("转换组件缺失，请在“工具 / 维护”中启用视频组件：" + executable);
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException("无法启动转换工具");
        var errors = DrainAsync(process.StandardError, keepHeader);
        var output = DrainAsync(process.StandardOutput);
        using var registration = token.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } });
        await process.WaitForExitAsync(CancellationToken.None);
        var error = await errors;
        await output;
        token.ThrowIfCancellationRequested();
        if (process.ExitCode != 0) throw new IOException("转换失败：" + error);
        return error;
    }

    private static async Task<string> DrainAsync(StreamReader reader, bool keepHeader = false)
    {
        string tail = "";
        char[] buffer = new char[4096];
        int count;
        while ((count = await reader.ReadAsync(buffer)) > 0)
        {
            tail += new string(buffer, 0, count);
            if (tail.Length > 6000) tail = keepHeader ? tail[..6000] : tail[^6000..];
        }
        return tail;
    }
}
