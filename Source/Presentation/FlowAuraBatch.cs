using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace LihuatLib.Presentation
{
    public sealed class FlowAuraBatch : IDisposable
    {
        private readonly LihuatLib.Presentation.IndexedInstanceDraw instanceDraw = new LihuatLib.Presentation.IndexedInstanceDraw();
        [StructLayout(LayoutKind.Sequential)]
        private struct Instance
        {
            internal Vector4 Position, Shape, Edge, Main, Highlight, Motion;
            internal bool Matches(in Instance other) => Position.Equals(other.Position) && Shape.Equals(other.Shape)
                && Edge.Equals(other.Edge) && Main.Equals(other.Main) && Highlight.Equals(other.Highlight) && Motion.Equals(other.Motion);
        }
        public const int InstanceBytes = 96;
        private static readonly int instancesId = Shader.PropertyToID("_Auras");
        private static readonly int timeId = Shader.PropertyToID("_EffectTime");
        private readonly Material material;
        private readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();
        private Instance[] instances = new Instance[16];
        private bool[] dirty = new bool[16];
        private ComputeBuffer buffer;
        private Bounds bounds;
        private int firstDirty, lastDirty, uploadedCount;
        private bool disposed;
        public static bool Supported => FlowAuraAssets.Supported;
        public int Count { get; private set; }
        public int UploadedBytes { get; private set; }

        public FlowAuraBatch()
        {
            material = new Material(FlowAuraAssets.Shader) { name = "LihuatFlowAura", enableInstancing = true,
                hideFlags = HideFlags.HideAndDontSave };
            material.SetTexture("_FlowNoise", FlowAuraAssets.Noise);
            BeginFrame();
        }
        public void BeginFrame()
        {
            if (disposed) throw new ObjectDisposedException(nameof(FlowAuraBatch));
            Count = UploadedBytes = 0; firstDirty = int.MaxValue; lastDirty = -1;
        }
        // The caller owns placement, visibility, animation time and the batch lifetime.
        public void Add(Vector3 center, Vector2 size, in FlowAuraStyle style, float opacity = 1f,
            float rotationDegrees = 0f, float phase = 0f)
        {
            if (disposed) throw new ObjectDisposedException(nameof(FlowAuraBatch));
            if (!Supported || opacity <= .005f || style.Density <= 0f || size.x <= 0f || size.y <= 0f) return;
            if (Count == instances.Length)
            { Array.Resize(ref instances, instances.Length * 2); Array.Resize(ref dirty, instances.Length); }
            var item = new Instance { Position = new Vector4(center.x, center.y, center.z, 0f),
                Shape = new Vector4(size.x, size.y, rotationDegrees * Mathf.Deg2Rad, Mathf.Clamp01(opacity)),
                Edge = style.Edge, Main = style.Main, Highlight = style.Highlight,
                Motion = new Vector4((float)style.Direction, style.Speed, phase, style.Density) };
            if (!instances[Count].Matches(in item)) { instances[Count] = item; dirty[Count] = true; }
            if (dirty[Count]) { firstDirty = Math.Min(firstDirty, Count); lastDirty = Count; }
            float extent = size.magnitude * .5f;
            var itemBounds = new Bounds(center, new Vector3(extent * 2f, .1f, extent * 2f));
            if (Count == 0) bounds = itemBounds; else bounds.Encapsulate(itemBounds);
            Count++;
        }
        public bool Draw(float timeSeconds, Camera camera)
        {
            if (disposed) throw new ObjectDisposedException(nameof(FlowAuraBatch));
            UploadedBytes = 0;
            if (!Supported || Count == 0 || camera == null) return false;
            if (buffer == null || buffer.count < Count)
            {
                buffer?.Release(); buffer = new ComputeBuffer(instances.Length, InstanceBytes, ComputeBufferType.Structured);
                properties.SetBuffer(instancesId, buffer); firstDirty = 0; lastDirty = Count - 1;
            }
            if (lastDirty >= firstDirty || Count > uploadedCount)
            {
                int uploaded = Count;
                buffer.SetData(instances, 0, 0, uploaded); uploadedCount = uploaded;
                Array.Clear(dirty, 0, uploaded);
                UploadedBytes = uploaded * InstanceBytes; firstDirty = int.MaxValue; lastDirty = -1;
            }
            properties.SetFloat(timeId, timeSeconds);
            instanceDraw.Draw(FlowAuraAssets.Quad, material, bounds, Count, properties, camera);
            return true;
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true; buffer?.Release(); buffer = null; instanceDraw.Dispose();
            UnityEngine.Object.Destroy(material);
        }
    }
}
