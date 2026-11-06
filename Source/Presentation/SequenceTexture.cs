using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

namespace LihuatLib.Presentation
{
    internal sealed class SequenceTexture : Texture
    {
        public override Waylith.Rendering.Texture2DArray CoreTexture { get; }

        internal SequenceTexture(AssetBundle bundle, string sequence, int frames = 128)
        {
            var layers = new List<Waylith.Rendering.Texture2D>(frames);
            for (int i = 0; i < frames; i++)
            {
                string address = sequence + "_" + i.ToString("D3");
                Texture2D texture = bundle.LoadAsset<Texture2D>(address);
                if (texture == null) throw new InvalidDataException("Missing animation frame: " + address);
                layers.Add(texture.CoreTexture);
            }
            CoreTexture = Waylith.Rendering.Texture2DArray.CreateStatic(layers, sequence);
            dimension = TextureDimension.Tex2DArray;
            name = sequence;
        }
    }
}
