using System.IO;
using System.Linq;
using UnityEngine;
using Verse;

namespace LihuatLib.Presentation
{
    internal static class AmbientEffectAssets
    {
        internal static readonly Shader Shader;
        private static readonly string[] names = { "WindShape", "AuraShape", "FlareShape", "GlowShape", "WindNoise", "DissolveNoise", "EchoNoise" };
        private static readonly Texture2D[] textures = new Texture2D[names.Length];
        internal static bool Supported => SystemInfo.supportsInstancing && Shader.isSupported;

        static AmbientEffectAssets()
        {
            AssetBundle bundle = LoadedModManager.GetMod<LibraryMod>().Content.assetBundles.loadedAssetBundles
                .SingleOrDefault(candidate => candidate.name == "lihuatambient");
            if (bundle == null) throw new InvalidDataException("Lihuat ambient effect asset bundle is missing");
            Shader = DefDatabase<ShaderDef>.GetNamed("Lihuat_AmbientEffects").Shader;
            if (Shader == null) throw new InvalidDataException("Incomplete Lihuat ambient effect assets");
            for (int i = 0; i < names.Length; i++)
            {
                textures[i] = bundle.LoadAsset<Texture2D>(names[i]);
                if (textures[i] == null) throw new InvalidDataException("Missing Lihuat ambient art: " + names[i]);
            }
        }

        internal static void Bind(Material material)
        {
            for (int i = 0; i < names.Length; i++) material.SetTexture("_" + names[i], textures[i]);
        }
    }
}
