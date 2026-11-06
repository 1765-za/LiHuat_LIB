using UnityEngine;

namespace LihuatLib.Presentation
{
    public enum FlowAuraDirection { Downward = -1, Counterflow = 0, Upward = 1 }

    public readonly struct FlowAuraStyle
    {
        public readonly Color Edge, Main, Highlight;
        public readonly FlowAuraDirection Direction;
        public readonly float Speed, Density;

        public FlowAuraStyle(Color edge, Color main, Color highlight,
            FlowAuraDirection direction = FlowAuraDirection.Upward, float speed = 1f, float density = 1f)
        {
            Edge = edge; Main = main; Highlight = highlight;
            Direction = direction; Speed = Mathf.Max(0f, speed); Density = Mathf.Max(0f, density);
        }
    }
}
