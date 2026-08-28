namespace JixModMaker;

public sealed class ResourceCategoryDef
{
    public string Id { get; init; }
    public string Label { get; init; }
    public string Description { get; init; }
    public string[] Prefixes { get; init; } = Array.Empty<string>();
    public bool Advanced { get; init; }
}

public static class ResourceKinds
{
    public const string Texture = "Texture2D";
    public const string Audio = "AudioClip";
    public const string Text = "TextAsset";
    public const string Mesh = "Mesh";
    public const string Animation = "AnimationClip";

    public static readonly (string Id, string Label)[] All =
    {
        (Texture, "贴图"),
        (Audio, "音频"),
        (Text, "文本"),
        (Mesh, "3D模型"),
        (Animation, "动画")
    };

    public static string Label(string kind) => All.FirstOrDefault(x => x.Id == kind).Label ?? kind;
}

public static class ResourceCategories
{
    public const string AllId = "all";
    public const string CharacterId = "character";

    private static readonly ResourceCategoryDef All = new()
    {
        Id = AllId,
        Label = "全部",
        Description = "当前类型下的全部资源"
    };

    private static readonly ResourceCategoryDef[] Texture =
    {
        new()
        {
            Id = CharacterId,
            Label = "角色 / 皮肤 / 怪物",
            Description = "角色卡面、细卡、半身、头像、角色照、升级图、怪物立绘",
            Prefixes = new[] { "UT_Hero_", "UT_Item_PlayerPhoto", "UT_Item_StandingPainting" }
        },
        new() { Id = "hand_card", Label = "手牌 / 技能卡", Description = "对局卡面 UT_HandCard", Prefixes = new[] { "UT_HandCard" } },
        new() { Id = "background", Label = "背景 / 横幅", Description = "主页背景、账号背景、商店背景", Prefixes = new[] { "UT_Item_AccountBackground", "UT_AccountBackground", "UT_Item_KV", "UT_StoreBG", "UT_MapScene_", "UT_MapPreview_", "UT_MapScene" } },
        new() { Id = "cardback", Label = "卡背", Description = "手牌背面", Prefixes = new[] { "UT_Item_CardBack", "UT_CardBack" } },
        new() { Id = "event", Label = "事件卡 / 事件图", Description = "事件、地图事件", Prefixes = new[] { "UT_Event_", "UT_MapEvent_", "PlatformEvent", "LandEvent" } },
        new() { Id = "buff", Label = "Buff / 状态图标", Description = "增益减益图标", Prefixes = new[] { "UT_Buff_" } },
        new() { Id = "chip", Label = "筹码", Description = "局内筹码与 Relic", Prefixes = new[] { "UT_Relic_", "UT_SGRelic", "UT_Platform_Relic", "LandRelic", "BattleRelic" } },
        new() { Id = "currency", Label = "货币", Description = "星币、货币图标", Prefixes = new[] { "UT_Item_Currency", "UT_BattlePass_Currencys" } },
        new() { Id = "map_tile", Label = "地图格子 / 平台", Description = "棋盘格子和特殊平台", Prefixes = new[] { "UT_Land_", "UT_LandLottery", "UT_Platform_" } },
        new() { Id = "emoji", Label = "表情", Description = "聊天表情", Prefixes = new[] { "UT_Item_Emoji" } },
        new() { Id = "dice", Label = "骰子外观", Description = "骰子皮肤", Prefixes = new[] { "UT_Item_Dice" } },
        new() { Id = "achieve", Label = "成就 / 战令 / 活动", Description = "成就、战令、活动主题", Prefixes = new[] { "UT_Achieve", "UT_BattlePass_", "UT_Activity_", "UT_Item_Activity", "UT_GachaTheme_", "UT_SkinGroup" } },
        new() { Id = "item", Label = "道具 / 礼物 / 宝箱", Description = "道具、礼物、材料、宝箱", Prefixes = new[] { "UT_Item_Chest", "UT_Item_Gift", "UT_Item_Material", "UT_Item_Hero", "UT_Item_Exchange", "UT_Item_" } },
        new() { Id = "ui_icon", Label = "UI 小图标", Description = "界面图标", Prefixes = new[] { "icon_blj", "UI_blj", "T_UI_" } },
        new() { Id = "sprite_anim", Label = "角色动作帧", Description = "Fight/Walk/Idle/Hit 等序列帧贴图", Prefixes = new[] { "Fight", "Walk", "Idle", "Hit", "Show", "Die", "Talent", "Eat", "Cry", "Cheer", "Hospitalized", "Commentary", "Electricshock" } },
        new() { Id = "fx", Label = "特效", Description = "粒子、光效、遮罩", Prefixes = new[] { "lizi_blj", "Glow_blj", "baozha_blj", "yuanhuan_blj", "Mask_blj", "Smoke_blj", "tiaodai_blj" }, Advanced = true },
        new() { Id = "lightmap", Label = "光照 / 场景贴图", Description = "光照、线条、纹理贴图", Prefixes = new[] { "Lightmap", "T_Light", "T_Wenli", "T_Line" }, Advanced = true },
        new() { Id = "other", Label = "其它贴图", Description = "未匹配命名规则的贴图", Advanced = true }
    };

    private static readonly ResourceCategoryDef[] Audio =
    {
        new() { Id = "music", Label = "音乐 / BGM", Description = "背景音乐、主题音乐", Prefixes = new[] { "BGM", "Music", "bgm", "music" } },
        new() { Id = "voice", Label = "语音", Description = "角色语音、旁白", Prefixes = new[] { "Voice", "VO", "voice", "vo_" } },
        new() { Id = "sfx", Label = "音效", Description = "技能、UI、环境音效", Prefixes = new[] { "SFX", "SE_", "UI_", "Audio", "sfx", "se_" } },
        new() { Id = "other", Label = "其它音频", Description = "未匹配命名规则的音频" }
    };

    private static readonly ResourceCategoryDef[] Generic =
    {
        new() { Id = "all", Label = "全部", Description = "当前类型下的全部资源" }
    };

    public static IReadOnlyList<ResourceCategoryDef> ForKind(string kind)
    {
        if (kind == ResourceKinds.Texture) return new[] { All }.Concat(Texture).ToArray();
        if (kind == ResourceKinds.Audio) return new[] { All }.Concat(Audio).ToArray();
        return Generic;
    }

    public static string Categorize(string kind, string name, int width = 0, int height = 0)
    {
        if (kind == ResourceKinds.Texture)
        {
            var parsed = NameParser.Parse(name);
            if (parsed.IsHero)
                return CharacterId;
            if ((name ?? "").StartsWith("UT_Item_StandingPainting", StringComparison.OrdinalIgnoreCase))
                return CharacterId;

            var byName = Match(Texture, name);
            if (byName != null) return byName.Id;
            if (Near(width, height, 404, 400, 8)) return "hand_card";
            if (Near(width, height, 760, 180, 12)) return "background";
            if (Near(width, height, 220, 220, 8)) return CharacterId;
            return "other";
        }

        if (kind == ResourceKinds.Audio)
            return Match(Audio, name)?.Id ?? "other";

        return AllId;
    }

    public static string Label(string kind, string categoryId)
    {
        return ForKind(kind).FirstOrDefault(c => c.Id == categoryId)?.Label ?? categoryId;
    }

    private static ResourceCategoryDef Match(IEnumerable<ResourceCategoryDef> defs, string name)
    {
        var n = name ?? "";
        foreach (var def in defs)
        {
            if (def.Id == "other") continue;
            if (def.Prefixes.Any(p => n.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                return def;
        }
        return null;
    }

    private static bool Near(int w, int h, int tw, int th, int tolerance)
        => w > 0 && h > 0 && Math.Abs(w - tw) <= tolerance && Math.Abs(h - th) <= tolerance;
}
