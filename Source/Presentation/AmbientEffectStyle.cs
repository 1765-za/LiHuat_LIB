using UnityEngine;

namespace LihuatLib.Presentation
{
    public enum AmbientEffectKind { EntangledEcho, SpectralAura, OrbitLights }
    public enum AmbientEffectHalf { Whole, Back, Front }

    public readonly struct AmbientEffectStyle
    {
        public readonly AmbientEffectKind Kind;
        public readonly Color Primary, Secondary, Highlight;
        public readonly float Speed, Intensity, Density, Thickness;

        public AmbientEffectStyle(AmbientEffectKind kind, Color primary, Color secondary, Color highlight,
            float speed = 1f, float intensity = 1f, float density = 1f, float thickness = 1f)
        {
            Kind = kind; Primary = primary; Secondary = secondary; Highlight = highlight;
            Speed = speed; Intensity = Mathf.Max(0f, intensity);
            Density = Mathf.Clamp(density, .25f, 3f); Thickness = Mathf.Clamp(thickness, .3f, 3f);
        }

        public static AmbientEffectStyle EntangledEcho => new AmbientEffectStyle(AmbientEffectKind.EntangledEcho,
            new Color(.035f, .28f, 1f), new Color(1f, .025f, .16f), new Color(.65f, .85f, 1f));
        public static AmbientEffectStyle SpectralAura => new AmbientEffectStyle(AmbientEffectKind.SpectralAura,
            new Color(.33f, .055f, 1f), new Color(.08f, .8f, 1f), new Color(.83f, .7f, 1f));
        public static AmbientEffectStyle OrbitLights => new AmbientEffectStyle(AmbientEffectKind.OrbitLights,
            new Color(1f, .5f, .04f), new Color(1f, .15f, .01f), new Color(1f, .95f, .65f));
    }
}
