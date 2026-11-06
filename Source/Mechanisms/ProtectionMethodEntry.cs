using System;
using System.Reflection;

namespace LihuatLib.Mechanisms
{
    public enum ProtectionRoute { Native, Managed }

    internal sealed class ProtectionMethodEntry
    {
        private readonly NativeMethodEntry native;
        internal ProtectionRoute Route { get; private set; }
        internal string Failure { get; private set; }
        internal bool NativeIntact => Route == ProtectionRoute.Native && native.Intact;
        internal bool Intact => native.Intact;

        internal ProtectionMethodEntry(MethodBase original)
        { native = new NativeMethodEntry(original); }

        internal void Publish(MethodInfo method)
        {
            using (new ExecutableWriteProtection.Scope(true))
            {
                if (!NativeRuntime.TryInitialize()) throw new InvalidOperationException(NativeRuntime.Failure);
                try
                {
                    native.Publish(method);
                    if (!native.Intact) throw new InvalidOperationException("Native entry publication did not remain intact.");
                    Route = ProtectionRoute.Native;
                    Failure = null;
                }
                catch (Exception error) { Failure = error.GetType().Name + ": " + error.Message; throw; }
            }
        }

        internal void Rollback()
        {
            using (new ExecutableWriteProtection.Scope(true))
            {
                native.Rollback();
            }
        }
    }
}
