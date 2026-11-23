using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace JixModMaker;

public static class FileIntegrity
{
    public sealed record Entry(string Path, long Size, string Sha256);

    public static string Hash(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    public static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    public static string Relative(string path)
    {
        path = path.Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(path) || path.StartsWith('/') || path.Contains(':') || path.Contains('\0')
            || path.Split('/').Any(part => part is "" or "." or ".." || part.Any(char.IsControl)))
            throw new InvalidDataException("不安全的资源相对路径：" + path);
        return path;
    }

    public static void CheckBundleLength(Stream stream, string name)
    {
        long start = stream.Position;
        try
        {
            Span<byte> signature = stackalloc byte[8];
            if (stream.Read(signature) != 8 || !signature.SequenceEqual("UnityFS\0"u8)) return;
            Span<byte> version = stackalloc byte[4];
            stream.ReadExactly(version);
            int format = BinaryPrimitives.ReadInt32BigEndian(version);
            if (format < 6 || format > 9) throw new InvalidDataException("不支持的 UnityFS 版本：" + name);
            ReadString();
            ReadString();
            Span<byte> size = stackalloc byte[8];
            stream.ReadExactly(size);
            long expected = BinaryPrimitives.ReadInt64BigEndian(size);
            if (expected != stream.Length - start)
                throw new InvalidDataException($"资源被截断或长度不符：{name}（应为 {expected}，实际 {stream.Length - start} 字节）");
        }
        finally { stream.Position = start; }

        void ReadString()
        {
            for (int i = 0; i < 256; i++)
            {
                int value = stream.ReadByte();
                if (value == 0) return;
                if (value < 0) throw new InvalidDataException("UnityFS 文件头不完整：" + name);
            }
            throw new InvalidDataException("UnityFS 文件头无效：" + name);
        }
    }

    public static IReadOnlyList<Entry> WriteZip(string root, IEnumerable<string> relativePaths, string destination,
        Action<int, int> progress = null)
    {
        root = Path.GetFullPath(root);
        string[] paths = relativePaths.Select(Relative).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).ToArray();
        var entries = new List<Entry>();
        using (var archive = ZipFile.Open(destination, ZipArchiveMode.Create))
        {
            foreach (string relative in paths)
            {
                using var source = new FileStream(Path.Combine(root, relative), FileMode.Open, FileAccess.Read, FileShare.Read);
                CheckBundleLength(source, relative);
                long length = source.Length;
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                using var target = archive.CreateEntry(relative, CompressionLevel.Optimal).Open();
                byte[] buffer = new byte[128 * 1024];
                int read;
                long copied = 0;
                while ((read = source.Read(buffer)) > 0)
                {
                    target.Write(buffer, 0, read);
                    hash.AppendData(buffer, 0, read);
                    copied += read;
                }
                if (copied != length || source.Length != length) throw new IOException("打包时资源发生变化：" + relative);
                entries.Add(new(relative, length, Convert.ToHexString(hash.GetHashAndReset())));
                progress?.Invoke(entries.Count, paths.Length * 2);
            }
        }
        VerifyZip(destination, entries, (done, _) => progress?.Invoke(paths.Length + done, paths.Length * 2));
        return entries;
    }

    public static void VerifyZip(string path, IReadOnlyList<Entry> expected, Action<int, int> progress = null)
    {
        using var archive = ZipFile.OpenRead(path);
        var files = archive.Entries.Where(entry => entry.Name.Length != 0).ToArray();
        if (files.Length != expected.Count || files.Select(entry => Relative(entry.FullName)).Distinct(StringComparer.OrdinalIgnoreCase).Count() != files.Length)
            throw new InvalidDataException("压缩包文件清单不一致或存在重复路径。");
        int done = 0;
        foreach (var item in expected)
        {
            var entry = archive.GetEntry(Relative(item.Path)) ?? throw new InvalidDataException("压缩包缺少文件：" + item.Path);
            using var stream = entry.Open();
            if (entry.Length != item.Size || Convert.ToHexString(SHA256.HashData(stream)) != item.Sha256)
                throw new InvalidDataException("压缩包回读校验失败：" + item.Path);
            progress?.Invoke(++done, expected.Count);
        }
    }
}
