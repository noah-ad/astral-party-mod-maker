using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;

namespace JixModMaker;

public sealed record VideoComponent(string Name, string Url, string Sha256, long Size, string Directory);

public static class VideoRuntime
{
    public const string RuntimeVersion = "ffmpeg-8.1.1-python-3.11.9-cricodecs-1.2.0";
    public static IReadOnlyList<VideoComponent> Components { get; } = Array.AsReadOnly(new[]
    {
        new VideoComponent("FFmpeg 8.1.1",
            "https://github.com/GyanD/codexffmpeg/releases/download/8.1.1/ffmpeg-8.1.1-essentials_build.zip",
            "6f58ce889f59c311410f7d2b18895b33c03456463486f3b1ebc93d97a0f54541", 109282242, "ffmpeg"),
        new VideoComponent("Python 3.11.9",
            "https://www.python.org/ftp/python/3.11.9/python-3.11.9-embed-amd64.zip",
            "009d6bf7e3b2ddca3d784fa09f90fe54336d5b60f0e0f305c37f400bf83cfd3b", 11249023, "python"),
        new VideoComponent("CriCodecs 1.2.0",
            "https://files.pythonhosted.org/packages/63/0a/e025145fb178acb1f840f435f9ee862a79f3e3b8d38bf3aadb2809b42107/cricodecs-1.2.0-cp311-cp311-win_amd64.whl",
            "ace3269a6d156fe8737b3e06ad3e80cd8c4899c3bc8df208df6314ceee4346de", 2121857, "python")
    });
    private static readonly SemaphoreSlim InstallGate = new(1, 1);
    public static string ScriptRoot => Path.Combine(AppContext.BaseDirectory, "Tools", "video");
    public static string CacheRoot => AppContext.GetData("JixModMaker.VideoRuntimeCache") as string ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JixModMaker", "video-runtime");
    public static string InstalledRoot => Path.Combine(CacheRoot, RuntimeVersion);
    public static string Root => HasFiles(ScriptRoot) ? ScriptRoot :
        HasFiles(InstalledRoot) && File.Exists(Path.Combine(InstalledRoot, "ready.txt")) ? InstalledRoot : ScriptRoot;
    public static string PythonPath => Path.Combine(Root, "python", "python.exe");
    public static string FfmpegPath => FindFfmpeg(Root);
    public static bool IsReady => HasFiles(Root) && ScriptsPresent;
    public static bool ScriptsPresent => File.Exists(Path.Combine(ScriptRoot, "mux.py")) &&
        File.Exists(Path.Combine(ScriptRoot, "inspect_movie.py"));

    public static string FindFfmpeg(string root) => File.Exists(Path.Combine(root, "ffmpeg.exe"))
        ? Path.Combine(root, "ffmpeg.exe")
        : Path.Combine(root, "ffmpeg", "ffmpeg-8.1.1-essentials_build", "bin", "ffmpeg.exe");

    public static bool HasFiles(string root) => File.Exists(FindFfmpeg(root)) &&
        File.Exists(Path.Combine(root, "python", "python.exe")) &&
        Directory.Exists(Path.Combine(root, "python", "cricodecs")) &&
        Directory.EnumerateFiles(Path.Combine(root, "python", "cricodecs"), "__init__*.pyd").Any();

    public static async Task ValidateAsync(string root, CancellationToken token)
    {
        if (!HasFiles(root)) throw new FileNotFoundException("视频组件不完整，请重新下载并启用。");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(45));
        try
        {
            await PortraitVideoConverter.RunAsync(FindFfmpeg(root), new[] { "-version" }, timeout.Token);
            await PortraitVideoConverter.RunAsync(Path.Combine(root, "python", "python.exe"),
                new[] { "-I", "-c", "from cricodecs import usm, video" }, timeout.Token);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new IOException("视频组件启动超时，请重试或检查安全软件的拦截记录。");
        }
    }

    public static async Task InstallAsync(IProgress<string> progress, CancellationToken token)
    {
        if (!ScriptsPresent) throw new FileNotFoundException("程序的 USM 脚本缺失，请重新解压程序及 data 文件夹。");
        await InstallGate.WaitAsync(token);
        string staging = null;
        try
        {
            Directory.CreateDirectory(CacheRoot);
            // The file lock also protects downloads and the directory swap across app instances.
            using var installationLock = new FileStream(Path.Combine(CacheRoot, "install.lock"), FileMode.OpenOrCreate,
                FileAccess.ReadWrite, FileShare.None);
            staging = Path.Combine(CacheRoot, ".staging-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);
            using var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("AstralPartyModMaker/2.3.0");
            foreach (var component in Components)
            {
                string archive = Path.Combine(CacheRoot, "downloads", component.Sha256 + ".zip");
                await DownloadAsync(client, component, archive, progress, token);
                progress?.Report("正在解压 " + component.Name + "...");
                await Task.Run(() => ExtractArchive(archive, Path.Combine(staging, component.Directory), token), token);
            }
            progress?.Report("正在验证视频编码和 USM 封装组件...");
            await ValidateAsync(staging, token);
            await File.WriteAllTextAsync(Path.Combine(staging, "ready.txt"), RuntimeVersion, token);
            token.ThrowIfCancellationRequested();
            await Task.Run(() => CommitInstall(staging, InstalledRoot));
            progress?.Report("视频组件已就绪");
        }
        finally
        {
            if (staging != null) TryDeleteDirectory(staging);
            InstallGate.Release();
        }
    }

    public static async Task DownloadAsync(HttpClient client, VideoComponent component, string destination,
        IProgress<string> progress, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (File.Exists(destination) && await MatchesAsync(destination, component, token))
        {
            progress?.Report(component.Name + " 已下载并校验");
            return;
        }
        var uri = new Uri(component.Url);
        if (uri.Scheme != Uri.UriSchemeHttps) throw new InvalidDataException("组件下载必须使用 HTTPS。");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        string part = destination + ".part";
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(90));
            using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            response.EnsureSuccessStatusCode();
            if (response.RequestMessage?.RequestUri?.Scheme != Uri.UriSchemeHttps)
                throw new InvalidDataException("组件下载被重定向到非 HTTPS 地址。");
            if (response.Content.Headers.ContentLength is long length && length != component.Size)
                throw new InvalidDataException(component.Name + " 下载大小不匹配，请稍后重试。");
            await using (var source = await response.Content.ReadAsStreamAsync(timeout.Token))
            await using (var output = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                byte[] buffer = new byte[81920];
                long total = 0;
                int previous = -1;
                while (true)
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(90));
                    int count = await source.ReadAsync(buffer, timeout.Token);
                    if (count == 0) break;
                    total += count;
                    if (total > component.Size) throw new InvalidDataException(component.Name + " 下载大小超出预期。");
                    hash.AppendData(buffer, 0, count);
                    await output.WriteAsync(buffer.AsMemory(0, count), timeout.Token);
                    int percent = (int)(total * 100 / component.Size);
                    if (percent != previous)
                    {
                        previous = percent;
                        progress?.Report($"正在下载 {component.Name} · {percent}% · {total / 1048576d:0.0} / {component.Size / 1048576d:0.0} MB");
                    }
                }
                if (total != component.Size || !Convert.ToHexString(hash.GetHashAndReset()).Equals(component.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException(component.Name + " SHA256 校验失败，未安装，请重试。");
            }
            token.ThrowIfCancellationRequested();
            File.Move(part, destination, true);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new IOException(component.Name + " 下载超时，请检查网络后重试。");
        }
        finally { if (File.Exists(part)) File.Delete(part); }
    }

    private static async Task<bool> MatchesAsync(string path, VideoComponent component, CancellationToken token)
    {
        if (new FileInfo(path).Length != component.Size) return false;
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, token)).Equals(component.Sha256, StringComparison.OrdinalIgnoreCase);
    }

    public static void ExtractArchive(string archive, string destination, CancellationToken token)
    {
        string root = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        Directory.CreateDirectory(root);
        using var zip = ZipFile.OpenRead(archive);
        long expanded = 0;
        foreach (var entry in zip.Entries)
        {
            token.ThrowIfCancellationRequested();
            string path = Path.GetFullPath(Path.Combine(root, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase) || entry.FullName.Contains(':') ||
                ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                throw new InvalidDataException("组件压缩包包含不安全的路径。");
            expanded = checked(expanded + entry.Length);
            if (expanded > 1_000_000_000) throw new InvalidDataException("组件解压大小超出限制。");
            if (string.IsNullOrEmpty(entry.Name)) { Directory.CreateDirectory(path); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var input = entry.Open();
            using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
            byte[] buffer = new byte[81920];
            int count;
            while ((count = input.Read(buffer, 0, buffer.Length)) != 0)
            {
                token.ThrowIfCancellationRequested();
                output.Write(buffer, 0, count);
            }
        }
    }

    public static void CommitInstall(string staging, string destination)
    {
        string previous = destination + ".previous-" + Guid.NewGuid().ToString("N");
        bool hadPrevious = Directory.Exists(destination);
        if (hadPrevious) MoveDirectory(destination, previous);
        try { MoveDirectory(staging, destination); }
        catch
        {
            if (hadPrevious) MoveDirectory(previous, destination);
            throw;
        }
        if (hadPrevious) TryDeleteDirectory(previous);
    }

    private static void MoveDirectory(string source, string destination)
    {
        // Windows scanners can briefly hold a just-exited executable or Python extension.
        for (int attempt = 0; ; attempt++)
        {
            try { Directory.Move(source, destination); return; }
            catch (IOException) when (attempt < 15 && Directory.Exists(source) && !Directory.Exists(destination)) { Thread.Sleep(200); }
            catch (UnauthorizedAccessException) when (attempt < 15 && Directory.Exists(source) && !Directory.Exists(destination)) { Thread.Sleep(200); }
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
