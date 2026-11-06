using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace LihuatLib.Presentation
{
    public enum PawnClipKind { VioletCharge, FacetShield }

    internal static class PawnClipAssets
    {
        private static readonly AssetBundle bundle=LoadedModManager.GetMod<LibraryMod>().Content.assetBundles.loadedAssetBundles
            .Single(candidate=>candidate.name=="lihuatpawnclips");
        private static readonly Dictionary<string, Texture> sequences = new Dictionary<string, Texture>();
        internal static Texture Frames(string name)
        {
            if (!sequences.TryGetValue(name, out Texture texture)) sequences.Add(name, texture = new SequenceTexture(bundle, name));
            return texture;
        }
        internal static T Load<T>(string name) where T:Object
        {
            T asset=bundle.LoadAsset<T>(name);
            if(asset==null)throw new InvalidDataException("Missing pawn clip asset: "+name);
            return asset;
        }
    }
}
