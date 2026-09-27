using System.Reflection;

namespace JixModMaker;

public static class AppBuildInfo
{
    private static readonly Assembly Assembly = typeof(AppBuildInfo).Assembly;
    public static string Version { get; } = "v" + (Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
        .InformationalVersion.Split('+')[0] ?? Assembly.GetName().Version!.ToString());
    public static bool IncludesVideoRuntime { get; } = Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .Any(attribute => attribute.Key == "PackageEdition" && attribute.Value == "full");
    public static string Edition => IncludesVideoRuntime ? "完整视频版" : "标准版";
    public static string ReleaseLabel => $"{Version} · {Edition}";
    public static string WindowTitle => "吉星派对 Mod 助手 " + ReleaseLabel;
}
