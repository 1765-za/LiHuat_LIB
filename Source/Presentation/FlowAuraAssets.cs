using System.IO;
using System.Linq;
using UnityEngine;
using Verse;

namespace LihuatLib.Presentation
{
    [StaticConstructorOnStartup]
    internal static class FlowAuraAssets
    {
        internal static readonly Shader Shader;
        internal static readonly Texture2D Noise;
        internal static readonly Mesh Quad;
        internal static readonly bool Supported;

        static FlowAuraAssets()
        {
            AssetBundle bundle = LoadedModManager.GetMod<LibraryMod>().Content.assetBundles.loadedAssetBundles
                .SingleOrDefault(candidate => candidate.name == "lihuatflow");
            if (bundle == null) throw new InvalidDataException("Lihuat flow aura asset bundle is missing");
            Shader = DefDatabase<ShaderDef>.GetNamed("Lihuat_FlowAura").Shader;
            Noise = bundle.LoadAsset<Texture2D>("FlowNoise");
            if (Shader == null || Noise == null) throw new InvalidDataException("Incomplete Lihuat flow aura assets");
            Quad = new Mesh { name = "LihuatFlowAuraQuad", hideFlags = HideFlags.HideAndDontSave };
            Quad.vertices = new[] { new Vector3(-.5f, 0f, -.5f), new Vector3(-.5f, 0f, .5f),
                new Vector3(.5f, 0f, .5f), new Vector3(.5f, 0f, -.5f) };
            Quad.uv = new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right };
            Quad.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            Quad.RecalculateBounds(); Quad.UploadMeshData(false);
            Supported = SystemInfo.supportsInstancing && Shader.isSupported;
            if (!Supported) Log.Warning("[LihuatLib.Effects] Flow aura shader or instancing is unavailable on " + SystemInfo.graphicsDeviceName);
        }
    }
}
