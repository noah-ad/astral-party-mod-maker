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

namespace UnityEngine
{
    public struct Rect
    {
        public float x { get; set; }
        public float y { get; set; }
        public float width { get; set; }
        public float height { get; set; }
        public Rect(float x, float y, float width, float height) { this.x = x; this.y = y; this.width = width; this.height = height; }
    }
    public struct Color32 { public byte r, g, b, a; }
    public class Object
    {
        public string name { get; set; }
        public bool Destroyed { get; private set; }
        public static void Destroy(Object value) => value.Destroyed = true;
    }
    public class ScriptableObject : Object
    {
        public static ScriptableObject CreateInstance(Type type) => (ScriptableObject)Activator.CreateInstance(type);
    }
}
namespace FairyGUI
{
    public interface IMeshFactory { void OnPopulateMesh(VertexBuffer buffer); }
    public sealed class RectMesh : IMeshFactory
    {
        public void OnPopulateMesh(VertexBuffer buffer) { buffer.AddQuad(buffer.contentRect, buffer.vertexColor, buffer.uvRect); buffer.AddTriangles(0); }
    }
    public sealed class VertexBuffer
    {
        public UnityEngine.Rect contentRect, uvRect, LastQuad, LastUv;
        public UnityEngine.Color32 vertexColor;
        public int Triangles;
        public void AddQuad(UnityEngine.Rect rect, UnityEngine.Color32 color, UnityEngine.Rect uv) { LastQuad = rect; LastUv = uv; }
        public void AddTriangles(int start) => Triangles++;
    }
    public sealed class NGraphics
    {
        public IMeshFactory meshFactory { get; set; } = new RectMesh();
        public int DirtyCount;
        public void SetMeshDirty() => DirtyCount++;
    }
    public class DisplayObject { public NGraphics graphics { get; } = new(); }
    public sealed class Shape : DisplayObject { }
    public sealed class GGraph { public Shape shape { get; } = new(); }
}
namespace Cysharp.Threading.Tasks
{
    public readonly struct UniTask<T>
    {
        public readonly T Result;
        public UniTask(T result) => Result = result;
    }
}
namespace CriWare.Assets
{
    public interface ICriAssetImpl
    {
        void OnEnable();
        void OnDisable();
    }
    public sealed class CriSerializedBytesAssetImpl : ICriAssetImpl
    {
        public byte[] Data { get; }
        public bool Enabled { get; private set; }
        public CriSerializedBytesAssetImpl(byte[] data) => Data = data;
        public void OnEnable() => Enabled = true;
        public void OnDisable() => Enabled = false;
    }
    public class CriAssetBase : UnityEngine.ScriptableObject
    {
        internal ICriAssetImpl implementation;
        public ICriAssetImpl Implementation => implementation;
    }
    public sealed class CriManaUsmAsset : CriAssetBase
    {
        public enum CodecType { Unknown, SofdecPrime }
        public class ManaMovieInfo
        {
            public uint width, height, dispWidth, dispHeight, framerateN, framerateD, totalFrames, numAlphaStreams;
            public CodecType codecType, alphaCodecType;
        }
        public class MovieAssetInfo { public bool loop; }
        internal ManaMovieInfo movieInfo = null;
        internal MovieAssetInfo assetInfo = null;
        public ManaMovieInfo MovieInfo => movieInfo;
        public MovieAssetInfo AssetInfo => assetInfo;
    }
}
namespace UnityEngine.AddressableAssets
{
    public static class Addressables
    {
        public static int Released;
        public static void Release<T>(T asset) => Released++;
    }
}
namespace UI
{
    using CriWare.Assets;
    using Cysharp.Threading.Tasks;
    using UnityEngine.AddressableAssets;
    public sealed class CriMovieManager
    {
        private readonly Dictionary<string, CriManaUsmAsset> _loadedAssets = new();
        public int OriginalLoads;
        public void Play(string key, FairyGUI.GGraph graph) { }
        public void PlaAutoReleaseVideo(string key, FairyGUI.GGraph graph) { }
        public void StopAndDestroy(FairyGUI.GGraph graph) { }
        public UniTask<CriManaUsmAsset> Load(string key)
        {
            OriginalLoads++;
            if (string.IsNullOrEmpty(key)) return new(null);
            var asset = new CriManaUsmAsset { name = key, implementation = new CriSerializedBytesAssetImpl(new byte[] { 9 }) };
            _loadedAssets[key] = asset;
            return new(asset);
        }
        public void ClearOne(string key)
        {
            if (!_loadedAssets.TryGetValue(key, out var asset)) return;
            asset.Implementation.OnDisable();
            _loadedAssets.Remove(key);
            Addressables.Release(asset);
        }
        public void Clear()
        {
            foreach (var asset in _loadedAssets.Values)
            {
                asset.Implementation.OnDisable();
                Addressables.Release(asset);
            }
            _loadedAssets.Clear();
        }
    }
}
