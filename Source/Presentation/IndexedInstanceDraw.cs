using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace LihuatLib.Presentation
{
    public sealed class IndexedInstanceDraw : IDisposable
    {
        private readonly uint[] arguments = new uint[5];
        private ComputeBuffer buffer;

        public void Draw(Mesh mesh, Material material, Bounds bounds, int count, MaterialPropertyBlock properties, Camera camera) =>
            Draw(mesh, material, bounds, count, properties, camera, 0);

        public void Draw(Mesh mesh, Material material, Bounds bounds, int count, MaterialPropertyBlock properties, Camera camera, int layer)
        {
            uint indexCount = mesh.GetIndexCount(0), start = mesh.GetIndexStart(0), vertex = mesh.GetBaseVertex(0);
            if (buffer == null || arguments[0] != indexCount || arguments[1] != count || arguments[2] != start || arguments[3] != vertex)
            {
                buffer ??= new ComputeBuffer(1, 5 * sizeof(uint), ComputeBufferType.IndirectArguments);
                arguments[0] = indexCount; arguments[1] = (uint)count; arguments[2] = start; arguments[3] = vertex;
                buffer.SetData(arguments);
            }
            Graphics.DrawMeshInstancedIndirect(mesh, 0, material, bounds, buffer, 0, properties,
                ShadowCastingMode.Off, false, layer, camera, LightProbeUsage.Off);
        }

        public void Dispose() { buffer?.Release(); buffer = null; }
    }
}
