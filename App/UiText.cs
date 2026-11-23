using System.Globalization;
using System.Text.Json;

namespace JixModMaker;

internal static class UiText
{
    private static readonly Dictionary<string, string> EnglishText = LoadCatalog();
    private static string PreferenceFile => Path.Combine(
        AppContext.GetData("JixModMaker.SettingsDirectory") as string ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JixModMaker"),
        "ui-language.txt");

    public static bool English { get; private set; } = ReadPreference();
    public static string T(string text) => English && text != null && EnglishText.TryGetValue(text, out var value) ? value : text;
    public static string F(FormattableString text) =>
        string.Format(English ? CultureInfo.GetCultureInfo("en") : CultureInfo.CurrentCulture, T(text.Format), text.GetArguments());

    public static bool SetEnglish(bool english)
    {
        English = english;
        var path = PreferenceFile;
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(temp, english ? "en" : "zh-CN");
            File.Move(temp, path, true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
        finally { try { File.Delete(temp); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }

    private static bool ReadPreference()
    {
        try { return File.ReadAllText(PreferenceFile).Trim() == "en"; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    private static Dictionary<string, string> LoadCatalog()
    {
        using var stream = typeof(UiText).Assembly.GetManifestResourceStream("JixModMaker.English.json");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream!);
    }

    // Translate generated group captions only. User-defined names and resource identifiers remain literal.
    public static string Group(string key, string display)
    {
        if (!English || key == null || display == null || !display.StartsWith(key, StringComparison.Ordinal)) return display;
        string caption = key.StartsWith("角色 ", StringComparison.Ordinal) ? "Hero " + key[3..] : T(key);
        return caption + display[key.Length..];
    }

    public static string Skin(string skin) => English && skin.StartsWith("皮肤", StringComparison.Ordinal)
        ? "Skin " + skin[2..] : T(skin);

    public static string Source(string source) => string.IsNullOrEmpty(source) ? "-" :
        English ? string.Join("/", source.Split('/').Select(T)) : source;

    public static string AssetCaption(AssetCategory asset)
    {
        string label = asset.Kind switch
        {
            "Bust" => "Bust", "Card" => "Card art", "Card2" => "Card art 2", "ThinCard" => "Slim card",
            "ProfilePhoto" => "Avatar", "RolePhoto" => "Portrait", "LevelUp" => "Level up", "Story" => "Story",
            "SkillMovie" => "Skill cut-in", "SkillAnimation" => "Chibi motion · " + asset.Raw, _ => asset.Kind
        };
        return (asset.IsMonster ? asset.HeroId + " · " : "") + label + (asset.Sfw ? " (SFW)" : "");
    }
}
