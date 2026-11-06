using UnityEngine;

namespace LihuatLib.Presentation
{
    internal sealed class WeaponRingSilhouette
    {
        internal const int Resolution = 96;
        internal readonly Vector2 Anchor, Axis;
        private readonly Vector2[] sections;
        private readonly float low, high;
        internal float Length => high - low;

        private WeaponRingSilhouette(Vector2 anchor, Vector2 axis, float low, float high, Vector2[] sections)
        { Anchor = anchor; Axis = axis; this.low = low; this.high = high; this.sections = sections; }

        internal void Section(float fraction, out Vector2 center, out float width)
        {
            float position = Mathf.Clamp01(fraction) * (sections.Length - 1);
            int first = Mathf.FloorToInt(position), second = Mathf.Min(first + 1, sections.Length - 1);
            Vector2 extent = Vector2.Lerp(sections[first], sections[second], position - first);
            Vector2 cross = new Vector2(-Axis.y, Axis.x);
            center = Anchor + Axis * Mathf.Lerp(low, high, fraction) + cross * ((extent.x + extent.y) * .5f);
            width = extent.y - extent.x;
        }

        internal static WeaponRingSilhouette Analyze(Color32[] pixels)
        {
            int count = 0;
            Vector2 sum = Vector2.zero;
            for (int i = 0; i < pixels.Length; i++)
                if (pixels[i].a >= 96) { sum += Pixel(i); count++; }
            if (count == 0)
                return new WeaponRingSilhouette(new Vector2(.5f, .5f), Vector2.up, 0f, 0f, new[] { Vector2.zero });
            Vector2 centroid = sum / count;
            float xx = 0f, yy = 0f, xy = 0f;
            for (int i = 0; i < pixels.Length; i++)
            {
                if (pixels[i].a < 96) continue;
                Vector2 delta = Pixel(i) - centroid;
                xx += delta.x * delta.x; yy += delta.y * delta.y; xy += delta.x * delta.y;
            }
            float angle = .5f * Mathf.Atan2(2f * xy, xx - yy);
            Vector2 axis = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)), cross = new Vector2(-axis.y, axis.x);
            float low = float.MaxValue, high = float.MinValue;
            for (int i = 0; i < pixels.Length; i++)
            {
                if (pixels[i].a < 96) continue;
                float along = Vector2.Dot(Pixel(i) - centroid, axis);
                low = Mathf.Min(low, along); high = Mathf.Max(high, along);
            }
            float pixelExtent = (Mathf.Abs(axis.x) + Mathf.Abs(axis.y)) * .5f / Resolution;
            low -= pixelExtent; high += pixelExtent;
            var sections = new Vector2[Resolution];
            for (int i = 0; i < sections.Length; i++) sections[i] = new Vector2(float.MaxValue, float.MinValue);
            for (int i = 0; i < pixels.Length; i++)
            {
                if (pixels[i].a < 96) continue;
                Vector2 delta = Pixel(i) - centroid;
                float along = Vector2.Dot(delta, axis), across = Vector2.Dot(delta, cross);
                int first = Mathf.Clamp(Mathf.FloorToInt((along - pixelExtent - low) / (high - low) * (Resolution - 1)), 0, Resolution - 1);
                int last = Mathf.Clamp(Mathf.CeilToInt((along + pixelExtent - low) / (high - low) * (Resolution - 1)), 0, Resolution - 1);
                for (int slice = first; slice <= last; slice++)
                    sections[slice] = new Vector2(Mathf.Min(sections[slice].x, across - pixelExtent),
                        Mathf.Max(sections[slice].y, across + pixelExtent));
            }
            var occupied = new bool[sections.Length];
            for (int i = 0; i < sections.Length; i++) occupied[i] = sections[i].x <= sections[i].y;
            // Empty slices between disconnected opaque pieces inherit the nearest visible cross-section.
            for (int i = 0; i < sections.Length; i++)
            {
                if (occupied[i]) continue;
                for (int distance = 1; distance < sections.Length; distance++)
                {
                    int before = i - distance, after = i + distance;
                    if (before >= 0 && occupied[before]) { sections[i] = sections[before]; break; }
                    if (after < sections.Length && occupied[after]) { sections[i] = sections[after]; break; }
                }
            }
            return new WeaponRingSilhouette(centroid, axis, low, high, sections);
        }

        private static Vector2 Pixel(int index) => new Vector2((index % Resolution + .5f) / Resolution,
            (index / Resolution + .5f) / Resolution);
    }
}
