using System;
using System.Reflection;

namespace LihuatLib.Mechanisms
{
    internal sealed class NativeMethodEntry
    {
        private readonly MethodBase original;
        private readonly byte[] canonical;
        private bool registered;
        internal bool Intact => registered && NativeRuntime.Status(original).Intact;

        internal NativeMethodEntry(MethodBase original)
        {
            NativeRuntime.Validate(original);
            this.original = original;
            canonical = original.GetMethodBody()?.GetILAsByteArray() ?? throw new InvalidOperationException("Missing metadata IL: " + original);
        }

        internal void Publish(MethodInfo replacement)
        {
            if (registered && NativeRuntime.Status(original).State == NativeIlState.Missing) registered = false;
            bool first = !registered;
            if (first) { NativeRuntime.Register(original, canonical, canonical, replacement); registered = true; }
            try { NativeRuntime.Publish(original, replacement); }
            catch { if (first) Rollback(); throw; }
        }

        internal void Rollback()
        {
            if (!registered) return;
            if (NativeRuntime.Status(original).State == NativeIlState.Missing) { registered = false; return; }
            if (!NativeRuntime.Remove(original)) throw new InvalidOperationException("Native IL rollback failed: " + original);
            registered = false;
        }
    }
}
