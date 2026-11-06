using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace LihuatLib.WorldArts
{
    [StaticConstructorOnStartup]
    internal static class WorldArtEffectDraw
    {
        internal static readonly Material Ring = MaterialPool.MatFrom("Lihuat/AstralArts/Effects/Ring", ShaderDatabase.Transparent);
        internal static readonly Material Solid = MaterialPool.MatFrom(BaseContent.WhiteTex, ShaderDatabase.Transparent, Color.white);
        private static readonly MaterialPropertyBlock properties = new();
        private static readonly int colorId = Shader.PropertyToID("_Color");

        internal static void Quad(Vector3 position, Vector3 normal, Vector2 size, Material material, Color color, float angle = 0f)
            => Mesh(MeshPool.plane10, Matrix4x4.TRS(position,
                Quaternion.FromToRotation(Vector3.up, normal) * Quaternion.AngleAxis(angle, Vector3.up),
                new Vector3(size.x, 1f, size.y)), material, color);
        internal static void Mesh(Mesh mesh, Matrix4x4 matrix, Material material, Color color, int submesh = 0)
        {
            properties.SetColor(colorId, color);
            Graphics.DrawMesh(mesh, matrix, material, WorldCameraManager.WorldLayer, null, submesh, properties);
        }
        internal static float Noise(int index) => Mathf.Repeat(Mathf.Sin(index * 127.1f + 311.7f) * 43758.5453f, 1f);

    }
}
