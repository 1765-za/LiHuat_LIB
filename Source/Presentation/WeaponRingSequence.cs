using System;
using UnityEngine;

namespace LihuatLib.Presentation
{
    public sealed class WeaponRingSequence
    {
        public const float Interval = .18f;
        public const float Duration = .28f;
        private float[] starts;
        public int Count { get; private set; }

        public WeaponRingSequence(int settledLayers = 0)
        {
            Count = Mathf.Max(0, settledLayers);
            starts = new float[Mathf.Max(8, Count)];
            for (int i = 0; i < Count; i++) starts[i] = float.NegativeInfinity;
        }

        public void SetLayers(int layers, float now)
        {
            layers = Mathf.Max(0, layers);
            if (layers > starts.Length) Array.Resize(ref starts, Mathf.NextPowerOfTwo(layers));
            float next = Count > 0 ? Mathf.Max(now, starts[Count - 1] + Interval) : now;
            for (int i = Count; i < layers; i++) { starts[i] = next; next += Interval; }
            Count = layers;
        }

        public WeaponRingPulse Sample(int layer, float now)
        {
            if (layer < 0 || layer >= Count) return default;
            float age = now - starts[layer];
            if (age < 0f) return default;
            if (age >= Duration) return new WeaponRingPulse(1f, 1f, 0f);
            const float peak = .055f;
            float rise = Mathf.Clamp01(age / peak);
            float settle = Mathf.Clamp01((age - peak) / (Duration - peak));
            float scale = age < peak ? Mathf.Lerp(.35f, 1.55f, 1f - (1f - rise) * (1f - rise))
                : Mathf.Lerp(1.55f, 1f, settle * (2f - settle));
            float flash = (1f - Mathf.Clamp01(age / Duration));
            return new WeaponRingPulse(scale, Mathf.Lerp(.65f, 1f, rise), flash * flash);
        }
    }

    public readonly struct WeaponRingPulse
    {
        public readonly float Scale, Opacity, Flash;
        public WeaponRingPulse(float scale, float opacity, float flash)
        { Scale = scale; Opacity = opacity; Flash = flash; }
    }
}
