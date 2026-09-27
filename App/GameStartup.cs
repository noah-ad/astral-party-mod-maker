using System.Diagnostics;

namespace JixModMaker;

public static class GameStartup
{
    public static ProcessStartInfo SteamStart() => new($"steam://rungameid/{GameLocator.AppId}")
    {
        UseShellExecute = true
    };
}
