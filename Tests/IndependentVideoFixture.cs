namespace UnityEngine
{
    public static class Application
    {
        public static string persistentDataPath { get; set; }
    }
}

namespace Cysharp.Threading.Tasks
{
    public readonly struct UniTask<T>
    {
        public bool IsCompleted => true;
    }
}

namespace CriWare
{
    public sealed class CriFsBinder { }
    public class CriManaMovieMaterialBase
    {
        public CriMana.Player player { get; private set; }
        public void PlayerManualInitialize() => player = new();
    }
}

namespace CriWare.CriMana
{
    public sealed class Player
    {
        public enum SetMode { New, Append }
        public string File;
        public bool IsLooping;
        public bool additiveMode { get; set; }
        public bool SetFile(CriFsBinder binder, string path, SetMode mode) { File = path; return true; }
        public void Loop(bool enabled) => IsLooping = enabled;
    }
}

namespace CriWare.Assets
{
    public sealed class CriManaUsmAsset { }
    public static class CriManaPlayerExtentionForAsset
    {
        public static int OfficialSets;
        public static bool SetAsset(CriMana.Player player, CriManaUsmAsset asset, CriMana.Player.SetMode mode)
        {
            OfficialSets++;
            return true;
        }
    }
}

namespace UI
{
    public sealed class CriMovieManager
    {
        public int OfficialLoads;
        public Cysharp.Threading.Tasks.UniTask<CriWare.Assets.CriManaUsmAsset> Load(string key)
        {
            OfficialLoads++;
            return default;
        }
    }
    public sealed class CriManaMovieControllerForGGraph : CriWare.CriManaMovieMaterialBase
    {
        public string CurrentVideoKey;
        public void SetAsset(string key, CriWare.Assets.CriManaUsmAsset asset)
        {
            CurrentVideoKey = key;
            if (player == null) PlayerManualInitialize();
            CriWare.Assets.CriManaPlayerExtentionForAsset.SetAsset(player, asset, 0);
        }
    }
    public sealed class UICom_Card
    {
        public FairyGUI.GGraph video_FrontCard = new();
        public FairyGUI.GGraph video_FullCard = new();
        public FairyGUI.Controller frontState = new();
        public CardView Rendered;
        public int Layer;
    }
    public static class CommonUIManager
    {
        public static void RendererCardContent(UICom_Card view, CardView card)
        {
            view.Rendered = card;
            if (card.CardType == 1) view.Layer = 1;
            if (card.CardType == 2) view.Layer = 2;
        }
    }
    public sealed class CriManaMovieBridge
    {
        public string _videoKey;
        public void AddVideoGraph(FairyGUI.GGraph graph) => graph.VideoKey = _videoKey;
    }
}

public sealed class CardView
{
    public string Key;
    public bool IsVideo;
    public int CardType;
    public string Name;
}

namespace FairyGUI
{
    public sealed class Controller
    {
        public string GetPageId(int index) => "page-" + index;
    }
    public sealed class GearDisplay2
    {
        public string[] pages { get; set; } = new[] { "original" };
    }
    public class GObject
    {
        private GearDisplay2 gear = new();
        public bool visible { get; set; }
        public object GetGear(int index) => gear;
        public void HandleControllerChanged(Controller controller) { }
    }
    public sealed class GGraph : GObject
    {
        public string VideoKey;
    }
}
