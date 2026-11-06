using LihuatLib.Presentation;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace LihuatLib.WorldArts
{
    [StaticConstructorOnStartup]
    internal static class WorldOpeningRenderer
    {
        private static readonly Material ink = new(DefDatabase<ShaderDef>.GetNamed("Lihuat_WorldOpening").Shader)
            { enableInstancing = true, renderQueue = ShaderDatabase.MetaOverlay.renderQueue + 1 };
        private static readonly Material light = new(DefDatabase<ShaderDef>.GetNamed("Lihuat_WorldOpeningLight").Shader)
            { enableInstancing = true, renderQueue = ShaderDatabase.MetaOverlay.renderQueue + 2 };
        private static readonly MaterialPropertyBlock properties = new();
        private static readonly IndexedInstanceDraw drawing = new();
        private static readonly Mesh mesh = Ribbon();
        private static readonly int originId = Shader.PropertyToID("_PlanetOrigin"), normalId = Shader.PropertyToID("_NormalRadius"),
            forwardId = Shader.PropertyToID("_ForwardLength"), rightId = Shader.PropertyToID("_RightWidth"),
            motionId = Shader.PropertyToID("_Motion"), tintId = Shader.PropertyToID("_Tint");

        static WorldOpeningRenderer() => Application.quitting += Release;

        internal static void Draw(WorldArtCast cast, float age)
        {
            var frame = cast.Frame;
            float length = frame.Unit * cast.Range.Length, width = frame.Unit * Mathf.Max(3f, cast.Range.Width * 1.35f);
            properties.SetVector(originId, new Vector4(frame.Origin.x, frame.Origin.y, frame.Origin.z, .08f));
            properties.SetVector(normalId, new Vector4(frame.Normal.x, frame.Normal.y, frame.Normal.z, frame.Radius));
            properties.SetVector(forwardId, new Vector4(frame.Forward.x, frame.Forward.y, frame.Forward.z, length));
            properties.SetVector(rightId, new Vector4(frame.Right.x, frame.Right.y, frame.Right.z, width));
            float seed = WorldArtEffectDraw.Noise(cast.StartedAt);
            var bounds = new Bounds(frame.Center, Vector3.one * Mathf.Max(length, width) * 1.5f);
            properties.SetVector(motionId, new Vector4(age, seed, 0f, 1f));
            properties.SetColor(tintId, new Color(.045f, .025f, .018f, .9f));
            drawing.Draw(mesh, ink, bounds, 1, properties, Find.WorldCamera, WorldCameraManager.WorldLayer);
            properties.SetVector(motionId, new Vector4(age, seed, 0f, 0f));
            properties.SetColor(tintId, new Color(1f, .8f, .42f, 1f));
            drawing.Draw(mesh, light, bounds, 1, properties, Find.WorldCamera, WorldCameraManager.WorldLayer);
        }

        private static Mesh Ribbon()
        {
            const int segments = 64;
            var vertices = new Vector3[(segments + 1) * 2];
            var uv = new Vector2[vertices.Length]; var triangles = new int[segments * 6];
            for (int i = 0; i <= segments; i++)
            {
                float along = i / (float)segments;
                vertices[i * 2] = new Vector3(along - .5f, 0f, -.5f);
                vertices[i * 2 + 1] = new Vector3(along - .5f, 0f, .5f);
                uv[i * 2] = new Vector2(along, 0f); uv[i * 2 + 1] = new Vector2(along, 1f);
                if (i == segments) continue;
                int v = i * 2, t = i * 6;
                triangles[t] = v; triangles[t + 1] = v + 1; triangles[t + 2] = v + 2;
                triangles[t + 3] = v + 1; triangles[t + 4] = v + 3; triangles[t + 5] = v + 2;
            }
            return new Mesh { name = "LihuatWorldOpening", vertices = vertices, uv = uv, triangles = triangles };
        }
        private static void Release()
        {
            drawing.Dispose(); Object.Destroy(mesh); Object.Destroy(ink); Object.Destroy(light);
        }
    }
}
