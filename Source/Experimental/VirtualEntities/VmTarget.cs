using System;
using System.Threading;

namespace LihuatLib.Experimental.VirtualEntities
{
    public sealed class VmTarget : IDisposable
    {
        internal readonly VmExperimentSession Session;
        internal readonly object Reference;
        internal readonly IntPtr Address;
        private int intervention, disposed;
        public ulong Generation { get; }
        public bool IsDisposed => Volatile.Read(ref disposed) != 0;
        public VmIntervention Intervention => (VmIntervention)Volatile.Read(ref intervention);

        internal VmTarget(VmExperimentSession session, object reference, IntPtr address, ulong generation)
        { Session = session; Reference = reference; Address = address; Generation = generation; }

        public void SetIntervention(VmIntervention value)
        {
            Session.RequireActive();
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            if ((value & ~(VmIntervention.Projection | VmIntervention.Execution)) != 0) throw new ArgumentOutOfRangeException(nameof(value));
            if ((value & VmIntervention.Projection) != 0) Session.Require(VmExperimentFeature.Render);
            if ((value & VmIntervention.Execution) != 0) Session.Require(VmExperimentFeature.Clr);
            Volatile.Write(ref intervention, (int)value);
        }

        internal bool Blocks(VmRouteKind kind) => !IsDisposed && (Intervention & (kind == VmRouteKind.Projection
            ? VmIntervention.Projection : VmIntervention.Execution)) != 0;
        internal void Release() { Volatile.Write(ref intervention, 0); Volatile.Write(ref disposed, 1); }
        public void Dispose()
        {
            if (IsDisposed) return;
            Session.RequireActive();
            Release(); Session.Release(this);
        }
    }
}
