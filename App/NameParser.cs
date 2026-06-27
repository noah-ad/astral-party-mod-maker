using System.Text.RegularExpressions;

namespace JixModMaker;

/// <summary>一个资源名解析后的分类信息。</summary>
public class AssetCategory
{
    public string Series;    // Hero / HandCard / Other
    public string HeroId;    // 角色ID, 如 "101"; 手牌为其编号
    public string Kind;      // Bust / Card / Card2 / ProfilePhoto / RolePhoto / LevelUp / HandCard / ""
    public string Variant;   // 01 / 02 / Max / ""
    public bool Sfw;         // 是否 _sfw 和谐版
    public string Raw;       // 原始名

    public bool IsHero => Series == "Hero";
    public bool IsHandCard => Series == "HandCard";

    /// <summary>角色ID >= 1000 视为怪兽。</summary>
    public bool IsMonster => IsHero && int.TryParse(HeroId, out var n) && n >= 1000;

    /// <summary>用于分组的稳定键 (重命名时作为 key)。怪兽合为一组。</summary>
    public string GroupKey => Series switch
    {
        "Hero" => IsMonster ? "怪兽" : "角色 " + HeroId,
        "HandCard" => "手牌",
        _ => "其他"
    };

    /// <summary>皮肤分组名: 原皮 / 皮肤01 / 皮肤02 / Max 皮肤。手牌不分皮肤。</summary>
    public string Skin => IsHandCard ? "手牌"
        : Variant switch { "" => "原皮", "Max" => "Max 皮肤", _ => "皮肤" + Variant };

    public int SkinOrder => Skin == "原皮" ? 0 : Skin == "Max 皮肤" ? 99
        : int.TryParse(Variant, out var n) ? n : 50;

    public int KindOrder => Kind switch
    {
        "Bust" => 0, "Card" => 1, "Card2" => 2, "ThinCard" => 3,
        "ProfilePhoto" => 4, "RolePhoto" => 5, "LevelUp" => 6, "Story" => 7, _ => 9
    };

    /// <summary>卡片上显示的版本类型 (皮肤已在分组标题, 这里只显示类型)。</summary>
    public string SubLabel
    {
        get
        {
            string k = Kind switch
            {
                "Bust" => "半身", "Card" => "卡面", "Card2" => "卡面2", "ThinCard" => "细卡",
                "ProfilePhoto" => "头像", "RolePhoto" => "角色照", "LevelUp" => "升级",
                "Story" => "剧情", "HandCard" => "手牌", _ => Kind
            };
            return k + (Sfw ? " (和谐)" : "");
        }
    }
}

public static class NameParser
{
    private static readonly Regex HeroRe = new(
        @"^UT_Hero_(?<kind>Bust|Card2|Card|ThinCard|ProfilePhoto|RolePhoto|LevelUp|Story)_(?<id>\d+)(?<rest>.*)$",
        RegexOptions.Compiled);
    private static readonly Regex HandRe = new(
        @"^UT_HandCard_(?<id>\d+)(?<sfw>_sfw)?$", RegexOptions.Compiled);
    private static readonly Regex VarRe = new(@"_(?<v>01|02|03|04|05|Max)(?:_|$)", RegexOptions.Compiled);

    public static AssetCategory Parse(string name)
    {
        var c = new AssetCategory { Raw = name, Series = "Other" };
        if (string.IsNullOrEmpty(name)) return c;

        var h = HeroRe.Match(name);
        if (h.Success)
        {
            c.Series = "Hero";
            c.Kind = h.Groups["kind"].Value;
            c.HeroId = h.Groups["id"].Value;
            string rest = h.Groups["rest"].Value;
            c.Sfw = rest.Contains("_sfw");
            var vm = VarRe.Match(rest);
            if (vm.Success) c.Variant = vm.Groups["v"].Value;
            return c;
        }

        var hc = HandRe.Match(name);
        if (hc.Success)
        {
            c.Series = "HandCard";
            c.Kind = "HandCard";
            c.HeroId = hc.Groups["id"].Value;
            c.Sfw = hc.Groups["sfw"].Success;
            return c;
        }

        return c;
    }
}
