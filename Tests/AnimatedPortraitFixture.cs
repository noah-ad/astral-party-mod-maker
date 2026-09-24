public sealed class SkinStandingPaintingConfigureItem
{
    public string Character { get; set; } = "UT_Hero_Card_101";
    public string SfwCharacter { get; set; } = "UT_Hero_Card_101_sfw";
    public string InGameCharacter { get; set; } = "";
    public bool SafeMode { get; set; }

    public (string, bool) GetCharacter()
    {
        if (SafeMode && !string.IsNullOrEmpty(SfwCharacter)) return (SfwCharacter, false);
        return (Character, false);
    }

    public (string, bool) GetCharacterInGame()
    {
        if (string.IsNullOrEmpty(InGameCharacter)) return GetCharacter();
        return (InGameCharacter, false);
    }
}
