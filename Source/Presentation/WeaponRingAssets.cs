using System.IO;
using System.Linq;
using UnityEngine;
using Verse;

namespace LihuatLib.Presentation
{
    [StaticConstructorOnStartup]
    internal static class WeaponRingAssets
    {
        internal static readonly Shader Shader;
        internal static readonly Texture2D Mask;
        internal static readonly Mesh Quad;
        internal static readonly bool Supported;
        static WeaponRingAssets()
        {
            AssetBundle bundle = LoadedModManager.GetMod<LibraryMod>().Content.assetBundles.loadedAssetBundles
                .SingleOrDefault(candidate => candidate.name == "lihuatrings");
            if (bundle == null) throw new InvalidDataException("Lihuat weapon ring asset bundle is missing");
            Shader = DefDatabase<ShaderDef>.GetNamed("Lihuat_WeaponRing").Shader;
            Mask = bundle.LoadAsset<Texture2D>("WeaponRingMask");
            if (Shader == null || Mask == null) throw new InvalidDataException("Incomplete Lihuat weapon ring assets");
            Quad = new Mesh { name = "LihuatWeaponRingQuad", hideFlags = HideFlags.HideAndDontSave };
            Quad.vertices = new[] { new Vector3(-.5f, 0f, -.5f), new Vector3(-.5f, 0f, .5f),
                new Vector3(.5f, 0f, .5f), new Vector3(.5f, 0f, -.5f) };
            Quad.uv = new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right };
            Quad.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            Quad.RecalculateBounds(); Quad.UploadMeshData(false);
            Supported = SystemInfo.supportsInstancing && Shader.isSupported;
        }
    }
}
