using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace LihuatLib.Presentation
{
    public sealed class WeaponRingBatch : IDisposable
    {
        private readonly LihuatLib.Presentation.IndexedInstanceDraw instanceDraw = new LihuatLib.Presentation.IndexedInstanceDraw();
        [StructLayout(LayoutKind.Sequential)]
        private struct Instance
        {
            internal Vector4 Position, Shape, Color, Highlight, Motion, Clip;
            internal bool Matches(in Instance other) => Position.Equals(other.Position) && Shape.Equals(other.Shape)
                && Color.Equals(other.Color) && Highlight.Equals(other.Highlight) && Motion.Equals(other.Motion) && Clip.Equals(other.Clip);
        }
        public const int InstanceBytes = 96;
        private static readonly int instancesId = Shader.PropertyToID("_Rings");
        private static readonly int timeId = Shader.PropertyToID("_EffectTime");
        private readonly Material material;
        private readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();
        private readonly WeaponRingGeometry geometry = new WeaponRingGeometry();
        private Instance[] instances = new Instance[16];
        private bool[] dirty = new bool[16];
        private ComputeBuffer buffer;
        private Bounds bounds;
        private int firstDirty, lastDirty, uploadedCount;
        private bool disposed;
        public static bool Supported => WeaponRingAssets.Supported;
        public int Count { get; private set; }
        public int UploadedBytes { get; private set; }
        public int TextureSamples => geometry.TextureSamples;

        public WeaponRingBatch()
        {
            material = new Material(WeaponRingAssets.Shader) { name = "LihuatWeaponRing", enableInstancing = true,
                mainTexture = WeaponRingAssets.Mask, hideFlags = HideFlags.HideAndDontSave };
            BeginFrame();
        }
        public void BeginFrame()
        {
            RequireLive(); Count = UploadedBytes = 0; firstDirty = int.MaxValue; lastDirty = -1;
        }
        public void AddWeapon(Mesh mesh, Matrix4x4 matrix, Material weaponMaterial, in WeaponRingStyle style,
            float opacity = 1f, float phase = 0f, int layers = 1, WeaponRingSurface? surface = null,
            WeaponRingSequence sequence = null, float timeSeconds = 0f)
        {
            RequireLive();
            if (!Supported || opacity <= .005f || style.Scale <= 0f || layers <= 0) return;
            WeaponRingSurface effective = surface ?? WeaponRingSurface.FromMaterial(weaponMaterial);
            if (mesh == null || !effective.HasTexture) return;
            Vector4 clip = geometry.ClipPlane(mesh, matrix, effective);
            for (int i = 0; i < layers; i++)
            {
                WeaponRingPlacement placement = geometry.Fit(mesh, matrix, weaponMaterial, style, effective, (i + .5f) / layers);
                AddLayer(placement.Center, placement.Size, style, opacity * effective.Opacity,
                    placement.Angle, phase + i * 2.399963f, clip, sequence, i, timeSeconds);
            }
        }
        public void InvalidateTexture(Texture texture) { RequireLive(); geometry.Invalidate(texture); }
        public void AddLayers(Vector3 center, Vector2 size, in WeaponRingStyle style, int layers,
            float opacity = 1f, float rotationDegrees = 0f, float phase = 0f, Vector4 clipPlane = default,
            WeaponRingSequence sequence = null, float timeSeconds = 0f)
        {
            RequireLive();
            if (!Supported || opacity <= .005f || size.x <= 0f || size.y <= 0f || style.Intensity <= 0f) return;
            float radians = rotationDegrees * Mathf.Deg2Rad;
            Vector3 axis = new Vector3(Mathf.Sin(radians), 0f, Mathf.Cos(radians));
            float spacing = size.x * Mathf.Min(.22f, 1.1f / Mathf.Max(1, layers - 1));
            for (int i = 0; i < layers; i++)
                AddLayer(center + axis * ((i - (layers - 1) * .5f) * spacing), size, style,
                    opacity, rotationDegrees, phase + i * 2.399963f, clipPlane, sequence, i, timeSeconds);
        }
        private void AddLayer(Vector3 center, Vector2 size, in WeaponRingStyle style, float opacity,
            float angle, float phase, Vector4 clip, WeaponRingSequence sequence, int layer, float now)
        {
            WeaponRingPulse pulse = sequence?.Sample(layer, now) ?? new WeaponRingPulse(1f, 1f, 0f);
            if (pulse.Opacity <= 0f) return;
            var animated = new WeaponRingStyle(Color.Lerp(style.Color, style.Highlight, pulse.Flash * .85f),
                style.Highlight, style.Speed, style.Intensity * (1f + pulse.Flash * 1.4f),
                style.Scale, style.Aspect, style.Depth, style.MinWidthRatio);
            Add(center, size * pulse.Scale, animated, opacity * pulse.Opacity, angle, phase, clip);
        }
        public void Add(Vector3 center, Vector2 size, in WeaponRingStyle style, float opacity = 1f,
            float rotationDegrees = 0f, float phase = 0f, Vector4 clipPlane = default)
        {
            RequireLive();
            if (!Supported || opacity <= .005f || size.x <= 0f || size.y <= 0f || style.Intensity <= 0f) return;
            var item = new Instance { Shape = new Vector4(size.x, size.y, rotationDegrees * Mathf.Deg2Rad, Mathf.Clamp01(opacity)),
                Color = style.Color, Highlight = style.Highlight, Motion = new Vector4(style.Speed, phase, style.Intensity, 0f), Clip = clipPlane };
            item.Position = new Vector4(center.x, center.y - style.Depth, center.z, 1f); Append(in item);
            item.Position = new Vector4(center.x, center.y + style.Depth, center.z, -1f); Append(in item);
        }
        private void Append(in Instance item)
        {
            if (Count == instances.Length)
            { Array.Resize(ref instances, instances.Length * 2); Array.Resize(ref dirty, instances.Length); }
            if (!instances[Count].Matches(in item)) { instances[Count] = item; dirty[Count] = true; }
            if (dirty[Count]) { firstDirty = Math.Min(firstDirty, Count); lastDirty = Count; }
            float diameter = Mathf.Sqrt(item.Shape.x * item.Shape.x + item.Shape.y * item.Shape.y);
            var itemBounds = new Bounds(item.Position, new Vector3(diameter, .1f, diameter));
            if (Count == 0) bounds = itemBounds; else bounds.Encapsulate(itemBounds);
            Count++;
        }
        public bool Draw(float timeSeconds, Camera camera)
        {
            RequireLive(); UploadedBytes = 0;
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
                Array.Clear(dirty, 0, uploaded); UploadedBytes = uploaded * InstanceBytes;
                firstDirty = int.MaxValue; lastDirty = -1;
            }
            properties.SetFloat(timeId, timeSeconds);
            instanceDraw.Draw(WeaponRingAssets.Quad, material, bounds, Count, properties, camera);
            return true;
        }
        private void RequireLive() { if (disposed) throw new ObjectDisposedException(nameof(WeaponRingBatch)); }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true; buffer?.Release(); buffer = null; instanceDraw.Dispose(); geometry.Clear();
            UnityEngine.Object.Destroy(material);
        }
    }
}
