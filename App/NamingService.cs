using System.Text.Encodings.Web;
using System.Text.Json;

namespace JixModMaker;

/// <summary>
/// 自定义分组名 (右键重命名)。key = NameParser.GroupKey (如 "角色 101" / "怪兽" / "手牌"),
/// value = 用户起的名字。全局持久化到 LocalAppData。
/// </summary>
public class NamingService
{
    private readonly string _path;
    private Dictionary<string, string> _map = new();

    private static readonly JsonSerializerOptions J = new()
    {
        WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public NamingService()
    {
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JixModMaker");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "names.json");
        Load();
    }

    private void Load()
    {
        if (File.Exists(_path))
            try { _map = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_path)) ?? new(); }
            catch { _map = new(); }
    }

    private void Save() => File.WriteAllText(_path, JsonSerializer.Serialize(_map, J));

    /// <summary>有自定义名则 "原键 · 自定义名", 否则原键。</summary>
    public string Display(string groupKey)
        => _map.TryGetValue(groupKey, out var v) && !string.IsNullOrWhiteSpace(v)
           ? $"{groupKey} · {v}" : groupKey;

    public string Custom(string groupKey) => _map.GetValueOrDefault(groupKey, "");

    public void Set(string groupKey, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) _map.Remove(groupKey);
        else _map[groupKey] = name.Trim();
        Save();
    }
}
