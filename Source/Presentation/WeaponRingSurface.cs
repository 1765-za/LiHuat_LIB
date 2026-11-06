using UnityEngine;

namespace LihuatLib.Presentation
{
    public readonly struct WeaponRingSurface
    {
        public readonly Texture Texture;
        public readonly Vector4 TextureTransform;
        public readonly Vector3 ClipUv;
        public readonly float Opacity;
        public bool HasTexture => Texture != null;

        public WeaponRingSurface(Texture texture, Vector4 textureTransform, Vector3 clipUv, float opacity = 1f)
        { Texture = texture; TextureTransform = textureTransform; ClipUv = clipUv; Opacity = opacity; }

        public static WeaponRingSurface FromMaterial(Material material)
        {
            if (material == null) return default;
            Vector2 scale = material.mainTextureScale, offset = material.mainTextureOffset;
            return new WeaponRingSurface(material.mainTexture, new Vector4(scale.x, scale.y, offset.x, offset.y), Vector3.zero);
        }
    }
}
