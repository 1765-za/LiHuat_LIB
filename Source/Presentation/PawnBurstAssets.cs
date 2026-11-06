using System.IO;
using System.Linq;
using UnityEngine;
using Verse;

namespace LihuatLib.Presentation
{
    internal static class PawnBurstAssets
    {
        internal static readonly Shader Shader;
        private static readonly string[] names = { "GoldenSurge", "UmbralSurge", "GoldenFlow" };
        private static readonly Texture[] textures = new Texture[names.Length];
        internal static bool Supported => SystemInfo.supportsInstancing && SystemInfo.supports2DArrayTextures && Shader.isSupported;

        static PawnBurstAssets()
        {
            AssetBundle bundle = LoadedModManager.GetMod<LibraryMod>().Content.assetBundles.loadedAssetBundles
                .SingleOrDefault(candidate => candidate.name == "lihuatburst");
            if (bundle == null) throw new InvalidDataException("Lihuat burst effect asset bundle is missing");
            Shader = DefDatabase<ShaderDef>.GetNamed("Lihuat_PawnBurst").Shader;
            if (Shader == null) throw new InvalidDataException("Incomplete Lihuat burst effect assets");
            for (int i = 0; i < names.Length; i++)
            {
                textures[i] = new SequenceTexture(bundle, names[i]);
                if (textures[i] == null) throw new InvalidDataException("Missing Lihuat burst art: " + names[i]);
            }
        }

        internal static void Bind(Material material)
        {
            for (int i = 0; i < names.Length; i++) material.SetTexture("_" + names[i], textures[i]);
        }
    }
}
