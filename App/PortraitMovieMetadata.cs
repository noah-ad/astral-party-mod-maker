using System.Security.Cryptography;
using System.Text.Json;

namespace JixModMaker;

public sealed record PortraitMovieMetadata(uint Width, uint Height, uint FramerateN, uint FramerateD, uint TotalFrames, string Sha256)
{
    public static PortraitMovieMetadata Read(string video) => JsonSerializer.Deserialize<PortraitMovieMetadata>(File.ReadAllText(video + ".json"))
        ?? throw new InvalidDataException("缺少视频尺寸信息，请重新转换原始视频。");

    public void Validate(byte[] video)
    {
        if (Width is < 8 or > 4096 || Height is < 8 or > 4096 || FramerateN == 0 || FramerateD == 0 ||
            FramerateN / (double)FramerateD > 60 || TotalFrames == 0 || TotalFrames > int.MaxValue ||
            !string.Equals(Sha256, Convert.ToHexString(SHA256.HashData(video)), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("视频尺寸、帧数或内容校验失败，请重新转换。");
    }
}
