using UnityEngine;

namespace LihuatLib.Presentation
{
    public enum PawnBurstKind { GoldenSurge, UmbralSurge, GoldenFlow }
    public enum PawnBurstHalf { Whole, Back, Front }

    public readonly struct PawnBurstStyle
    {
        public readonly PawnBurstKind Kind;
        public readonly Color Primary, Secondary, Accent;
        public readonly float Speed, Intensity, Density;

        public PawnBurstStyle(PawnBurstKind kind, Color primary, Color secondary, Color accent,
            float speed = 1f, float intensity = 1f, float density = 1f)
        {
            Kind = kind; Primary = primary; Secondary = secondary; Accent = accent;
            Speed = speed; Intensity = Mathf.Max(0f, intensity); Density = Mathf.Clamp(density, .1f, 3f);
        }

        public static PawnBurstStyle GoldenSurge => new PawnBurstStyle(PawnBurstKind.GoldenSurge,
            new Color(1f, .79f, .3f), new Color(1f, .95f, .69f), new Color(1f, .48f, .06f));
        public static PawnBurstStyle UmbralSurge => new PawnBurstStyle(PawnBurstKind.UmbralSurge,
            Color.black, Color.black, Color.black);
        public static PawnBurstStyle GoldenFlow => new PawnBurstStyle(PawnBurstKind.GoldenFlow,
            new Color(1f, .64f, .09f), new Color(1f, .88f, .42f), new Color(1f, .97f, .8f));
    }
}
