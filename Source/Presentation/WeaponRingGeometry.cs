using System;
using System.Collections.Generic;
using UnityEngine;

namespace LihuatLib.Presentation
{
    public sealed class WeaponRingGeometry
    {
        private const int Resolution = WeaponRingSilhouette.Resolution;
        private readonly Dictionary<Mesh, Projection> meshes = new Dictionary<Mesh, Projection>();
        private readonly Dictionary<(Texture, Vector4), WeaponRingSilhouette> silhouettes = new Dictionary<(Texture, Vector4), WeaponRingSilhouette>();
        public int TextureSamples { get; private set; }

        private readonly struct Projection
        {
            internal readonly Vector3 Origin, U, V;
            internal readonly Vector2 Min, Span;
            internal Projection(Vector3 origin, Vector3 u, Vector3 v, Vector2 min, Vector2 span)
            { Origin = origin; U = u; V = v; Min = min; Span = span; }
            internal Vector3 Point(Vector2 uv) => Origin + U * uv.x + V * uv.y;
        }
        public WeaponRingPlacement Fit(Mesh mesh, Matrix4x4 matrix, Material material, in WeaponRingStyle style, WeaponRingSurface? surface = null, float along = .5f)
        {
            WeaponRingSurface effective = surface ?? WeaponRingSurface.FromMaterial(material);
            if (mesh == null || effective.Texture == null) throw new ArgumentException("A textured weapon mesh is required.");
            if (!meshes.TryGetValue(mesh, out Projection projection)) meshes.Add(mesh, projection = Project(mesh));
            Vector4 st = effective.TextureTransform;
            Vector2 scale = new Vector2(st.x, st.y), offset = new Vector2(st.z, st.w);
            Vector2 sampleOrigin = Vector2.Scale(projection.Min, scale) + offset;
            Vector2 sampleSpan = Vector2.Scale(projection.Span, scale);
            var key = (effective.Texture, new Vector4(sampleOrigin.x, sampleOrigin.y, sampleSpan.x, sampleSpan.y));
            if (!silhouettes.TryGetValue(key, out WeaponRingSilhouette shape))
            { shape = Sample(key.Item1, sampleOrigin, sampleSpan); silhouettes.Add(key, shape); TextureSamples++; }
            shape.Section(Mathf.Clamp01(along), out Vector2 anchor, out float sectionWidth);
            Vector2 uv = projection.Min + Vector2.Scale(anchor, projection.Span);
            Vector3 center = matrix.MultiplyPoint3x4(projection.Point(uv));
            Vector3 axis = matrix.MultiplyVector(projection.U * (shape.Axis.x * projection.Span.x)
                + projection.V * (shape.Axis.y * projection.Span.y));
            Vector3 cross = matrix.MultiplyVector(projection.U * (-shape.Axis.y * projection.Span.x)
                + projection.V * (shape.Axis.x * projection.Span.y));
            Vector3 transverse = cross - axis.normalized * Vector3.Dot(cross, axis.normalized);
            float width = Mathf.Max(sectionWidth, shape.Length * style.MinWidthRatio);
            float diameter = transverse.magnitude * width * 1.65f * style.Scale;
            return new WeaponRingPlacement(center, new Vector2(diameter, diameter * style.Aspect), Mathf.Atan2(axis.x, axis.z) * Mathf.Rad2Deg);
        }

        public Vector4 ClipPlane(Mesh mesh, Matrix4x4 matrix, in WeaponRingSurface surface)
        {
            if (surface.ClipUv == Vector3.zero) return Vector4.zero;
            if (!meshes.TryGetValue(mesh, out Projection projection)) meshes.Add(mesh, projection = Project(mesh));
            Vector3 u = matrix.MultiplyVector(projection.U), v = matrix.MultiplyVector(projection.V);
            Vector3 origin = matrix.MultiplyPoint3x4(projection.Origin);
            float determinant = u.x * v.z - u.z * v.x;
            if (Mathf.Abs(determinant) < 1e-7f) return Vector4.zero;
            Vector3 line = surface.ClipUv;
            Vector4 st = surface.TextureTransform;
            float a = line.x * st.x, b = line.y * st.y;
            float x = (a * v.z - b * u.z) / determinant, z = (b * u.x - a * v.x) / determinant;
            return new Vector4(x, 0f, z, line.z + line.x * st.z + line.y * st.w - x * origin.x - z * origin.z);
        }

        public void Invalidate(Texture texture)
        {
            var keys = new List<(Texture, Vector4)>();
            foreach (var key in silhouettes.Keys) if (key.Item1 == texture) keys.Add(key);
            foreach (var key in keys) silhouettes.Remove(key);
        }
        public void Clear() { silhouettes.Clear(); meshes.Clear(); TextureSamples = 0; }

        private static Projection Project(Mesh mesh)
        {
            Vector2[] uv = mesh.uv;
            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = new Vector2(float.MinValue, float.MinValue);
            foreach (Vector2 point in uv) { min = Vector2.Min(min, point); max = Vector2.Max(max, point); }
            for (int i = 0; i < triangles.Length; i += 3)
            {
                int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                Vector2 ab = uv[b] - uv[a], ac = uv[c] - uv[a];
                float determinant = ab.x * ac.y - ab.y * ac.x;
                if (Mathf.Abs(determinant) < 1e-7f) continue;
                Vector3 u = ((vertices[b] - vertices[a]) * ac.y - (vertices[c] - vertices[a]) * ab.y) / determinant;
                Vector3 v = ((vertices[c] - vertices[a]) * ab.x - (vertices[b] - vertices[a]) * ac.x) / determinant;
                return new Projection(vertices[a] - u * uv[a].x - v * uv[a].y, u, v, min, max - min);
            }
            throw new ArgumentException("Weapon ring placement requires a planar mesh with non-degenerate UVs.", nameof(mesh));
        }

        private static WeaponRingSilhouette Sample(Texture source, Vector2 offset, Vector2 scale)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture surface = RenderTexture.GetTemporary(Resolution, Resolution, 0, RenderTextureFormat.ARGB32);
            var readable = new Texture2D(Resolution, Resolution, TextureFormat.RGBA32, false);
            try
            {
                // Read a small GPU copy once; imported weapon textures need not enable Read/Write.
                Graphics.Blit(source, surface, scale, offset);
                RenderTexture.active = surface;
                readable.ReadPixels(new Rect(0, 0, Resolution, Resolution), 0, 0);
                return WeaponRingSilhouette.Analyze(readable.GetPixels32());
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(surface);
                UnityEngine.Object.Destroy(readable);
            }
        }

    }
}
