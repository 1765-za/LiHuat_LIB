using UnityEngine;

namespace LihuatLib.Presentation
{
    public readonly struct WeaponRingStyle
    {
        public readonly Color Color, Highlight;
        public readonly float Speed, Intensity, Scale, Aspect, Depth, MinWidthRatio;

        public WeaponRingStyle(Color color, Color highlight, float speed = 2.4f, float intensity = 1.35f,
            float scale = 1f, float aspect = .27f, float depth = .012f, float minWidthRatio = 0f)
        {
            Color = color; Highlight = highlight; Speed = speed; Intensity = Mathf.Max(0f, intensity);
            Scale = Mathf.Max(0f, scale); Aspect = Mathf.Max(.01f, aspect); Depth = Mathf.Max(.001f, depth);
            MinWidthRatio = Mathf.Max(0f, minWidthRatio);
        }
    }

    public readonly struct WeaponRingPlacement
    {
        public readonly Vector3 Center;
        public readonly Vector2 Size;
        public readonly float Angle;
        public WeaponRingPlacement(Vector3 center, Vector2 size, float angle)
        { Center = center; Size = size; Angle = angle; }
    }
}
