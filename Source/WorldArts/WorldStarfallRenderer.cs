using LihuatLib.Presentation;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace LihuatLib.WorldArts
{
    [StaticConstructorOnStartup]
    internal static class WorldStarfallRenderer
    {
        private static readonly Material body = Material("Lihuat_WorldStarfallBody", 1);
        private static readonly Material light = Material("Lihuat_WorldStarfallLight", 2);
        private static readonly Material smoke = Material("Lihuat_WorldStarfallSmoke", 3);
        private static readonly MaterialPropertyBlock properties = new();
        private static readonly IndexedInstanceDraw bodyDraw = new(), lightDraw = new(), smokeDraw = new();
        private static readonly Mesh mesh = Grid();
        private static readonly int originId = Shader.PropertyToID("_PlanetOrigin"), normalId = Shader.PropertyToID("_NormalRadius"),
            forwardId = Shader.PropertyToID("_ForwardUnit"), rightId = Shader.PropertyToID("_RightSize"),
            meteorId = Shader.PropertyToID("_MeteorProgress"), trailId = Shader.PropertyToID("_TrailAge"),
            breachId = Shader.PropertyToID("_BreachTime"), cameraRightId = Shader.PropertyToID("_CameraRightRange"),
            cameraUpId = Shader.PropertyToID("_CameraUpPass");
        internal static int LastDraws { get; private set; }
        internal static int LastInstances { get; private set; }

        static WorldStarfallRenderer() => Application.quitting += Release;
        private static Material Material(string def, int order) => new(DefDatabase<ShaderDef>.GetNamed(def).Shader)
            { enableInstancing = true, renderQueue = ShaderDatabase.MetaOverlay.renderQueue + order };
        private static Vector4 Pack(Vector3 value, float w) => new(value.x, value.y, value.z, w);

        internal static void Draw(WorldArtCast cast, int now)
        {
            var frame = cast.Frame;
            float progress = Mathf.Clamp01((now - cast.StartedAt) / (float)(cast.ImpactAt - cast.StartedAt));
            float after = (now - cast.ImpactAt) / 60f;
            float size = frame.Unit * (1.7f + Mathf.Sqrt(cast.Range.Radius + 1f) * .55f);
            Vector3 meteor = WorldStarfallVisuals.MeteorPosition(cast, progress);
            Vector3 trail = (WorldStarfallVisuals.MeteorPosition(cast, Mathf.Max(0f, progress - .025f))
                - WorldStarfallVisuals.MeteorPosition(cast, Mathf.Min(1f, progress + .025f))).normalized;
            Camera camera = Find.WorldCamera;
            properties.SetVector(originId, Pack(frame.Origin, WorldArtEffectDraw.Noise(cast.StartedAt)));
            properties.SetVector(normalId, Pack(frame.Normal, frame.Radius));
            properties.SetVector(forwardId, Pack(frame.Forward, frame.Unit));
            properties.SetVector(rightId, Pack(frame.Right, size));
            properties.SetVector(meteorId, Pack(meteor, progress));
            properties.SetVector(trailId, Pack(trail, after));
            properties.SetVector(breachId, Pack(WorldStarfallVisuals.MeteorPosition(cast, .48f), (now - cast.StartedAt) / 60f));
            properties.SetVector(cameraRightId, Pack(camera.transform.right, cast.Range.Radius + 1f));
            var bounds = new Bounds(frame.Center, Vector3.one * frame.Unit * (100f + cast.Range.Radius * 10f));
            properties.SetVector(cameraUpId, Pack(camera.transform.up, 0f));
            bodyDraw.Draw(mesh, body, bounds, 1, properties, camera, WorldCameraManager.WorldLayer);
            properties.SetVector(cameraUpId, Pack(camera.transform.up, 1f));
            int count = after < 0f ? 40 : 64;
            lightDraw.Draw(mesh, light, bounds, count, properties, camera, WorldCameraManager.WorldLayer);
            LastDraws = 2; LastInstances = count + 1;
            if (after < 0f) return;
            properties.SetVector(cameraUpId, Pack(camera.transform.up, 2f));
            smokeDraw.Draw(mesh, smoke, bounds, 48, properties, camera, WorldCameraManager.WorldLayer);
            LastDraws++; LastInstances += 48;
        }

        private static Mesh Grid()
        {
            const int segments = 8, stride = segments + 1;
            var vertices = new Vector3[stride * stride]; var uv = new Vector2[vertices.Length];
            var triangles = new int[segments * segments * 6];
            for (int y = 0; y <= segments; y++)
                for (int x = 0; x <= segments; x++)
                {
                    int v = y * stride + x;
                    uv[v] = new Vector2(x / (float)segments, y / (float)segments);
                    vertices[v] = new Vector3(uv[v].x - .5f, 0f, uv[v].y - .5f);
                    if (x == segments || y == segments) continue;
                    int t = (y * segments + x) * 6;
                    triangles[t] = v; triangles[t + 1] = v + stride; triangles[t + 2] = v + 1;
                    triangles[t + 3] = v + 1; triangles[t + 4] = v + stride; triangles[t + 5] = v + stride + 1;
                }
            return new Mesh { name = "LihuatWorldStarfall", vertices = vertices, uv = uv, triangles = triangles };
        }
        private static void Release()
        {
            bodyDraw.Dispose(); lightDraw.Dispose(); smokeDraw.Dispose();
            Object.Destroy(body); Object.Destroy(light); Object.Destroy(smoke); Object.Destroy(mesh);
        }
    }
}
