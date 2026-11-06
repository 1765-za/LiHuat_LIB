using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using LihuatLib.Core;

namespace LihuatLib.Experimental.VirtualEntities
{
    public sealed class VmExperimentSession : IDisposable
    {
        public const int MaximumTargets = 256, MaximumRoutes = 32, MaximumObservations = 8, MaximumObservedBytes = 1024 * 1024;
        private readonly ConcurrentDictionary<object, VmTarget> references = new(ReferenceEqualityComparer.Instance);
        private readonly ConcurrentDictionary<IntPtr, VmTarget> addresses = new();
        private readonly List<VmMethodRoute> routes = new();
        private readonly List<VmMemoryObservation> observations = new();
        private VmNativeMemory memory;
        private int observedBytes;
        private bool disposed;
        public string Owner { get; }
        public VmExperimentFeature Features { get; }
        public int TargetCount => references.Count + addresses.Count;
        public int RouteCount => routes.Count;
        public int ObservedBytes => observedBytes;

        internal VmExperimentSession(string owner, VmExperimentFeature features) { Owner = owner; Features = features; }
        internal void RequireActive()
        { LibraryCapabilities.RequirePublicationThread(); ObjectDisposedException.ThrowIf(disposed, this); }
        internal void Require(VmExperimentFeature feature)
        { RequireActive(); if ((Features & feature) != feature) throw new InvalidOperationException("VM experiment feature is disabled: " + feature); }

        public VmTarget Track(object instance, ulong generation)
        {
            RequireActive();
            if (instance == null || instance.GetType().IsValueType) throw new ArgumentException("A reference identity is required.", nameof(instance));
            CheckCapacity();
            var target = new VmTarget(this, instance, IntPtr.Zero, generation);
            if (!references.TryAdd(instance, target)) throw new InvalidOperationException("The entity identity is already tracked.");
            return target;
        }

        public VmTarget TrackAddress(IntPtr address, ulong generation)
        {
            RequireActive();
            if (address == IntPtr.Zero) throw new ArgumentException("A native identity is required.", nameof(address));
            CheckCapacity();
            var target = new VmTarget(this, null, address, generation);
            if (!addresses.TryAdd(address, target)) throw new InvalidOperationException("Release the previous address generation first.");
            return target;
        }

        private void CheckCapacity()
        { if (TargetCount >= MaximumTargets) throw new InvalidOperationException("VM target capacity reached."); }
        internal bool Blocks(object reference, VmRouteKind kind) => reference != null && references.TryGetValue(reference, out var target) && target.Blocks(kind);
        internal bool Blocks(IntPtr address, VmRouteKind kind) => addresses.TryGetValue(address, out var target) && target.Blocks(kind);
        internal void Release(VmTarget target)
        {
            if (target.Reference != null) references.TryRemove(target.Reference, out _);
            else addresses.TryRemove(target.Address, out _);
            foreach (VmMemoryObservation observation in observations.ToArray())
                if (observation.Target == target) observation.Dispose();
        }

        public VmMethodRoute Bind(MethodInfo method, int recipientArgument, VmRouteKind kind)
        {
            Require(kind == VmRouteKind.Projection ? VmExperimentFeature.Render : VmExperimentFeature.Clr);
            if (kind != VmRouteKind.Projection && kind != VmRouteKind.Execution) throw new ArgumentOutOfRangeException(nameof(kind));
            if (routes.Count >= MaximumRoutes) throw new InvalidOperationException("VM route capacity reached.");
            var route = VmMethodRoute.Install(this, method, recipientArgument, kind);
            routes.Add(route); return route;
        }

        public VmMemoryObservation Observe(VmTarget target, IntPtr address, int length)
        {
            Require(VmExperimentFeature.Native);
            if (target == null || target.Session != this || target.IsDisposed) throw new ArgumentException("The target lease is not active.", nameof(target));
            if (observations.Count >= MaximumObservations) throw new InvalidOperationException("VM observation capacity reached.");
            if (length <= 0 || length > MaximumObservedBytes - observedBytes) throw new ArgumentOutOfRangeException(nameof(length));
            if (address.ToInt64() <= 0 || address.ToInt64() > long.MaxValue - length) throw new ArgumentOutOfRangeException(nameof(address));
            memory ??= new VmNativeMemory();
            var observation = new VmMemoryObservation(this, memory, target, address, length);
            observations.Add(observation); observedBytes += length;
            return observation;
        }

        internal void Release(VmMethodRoute route) => routes.Remove(route);
        internal void Release(VmMemoryObservation observation)
        { if (observations.Remove(observation)) observedBytes -= observation.Length; }

        public void Dispose()
        {
            if (disposed) return;
            RequireActive();
            foreach (VmTarget target in references.Values) target.Release();
            foreach (VmTarget target in addresses.Values) target.Release();
            for (int i = routes.Count - 1; i >= 0; i--) routes[i].Dispose();
            for (int i = observations.Count - 1; i >= 0; i--) observations[i].Dispose();
            references.Clear(); addresses.Clear(); memory?.Dispose(); memory = null;
            disposed = true;
        }
    }
}
