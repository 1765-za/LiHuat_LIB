using System;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace LihuatLib.Configuration
{
    [Flags]
    public enum HighMechanism
    {
        None = 0,
        Native = 1,
        Scanning = 2,
        AstralArts = 4,
        Maintenance = 8,
        All = Native | Scanning | AstralArts | Maintenance
    }

    public static class HighMechanismPolicy
    {
        private static bool initialized;
        public static HighMechanism Disabled { get; private set; }
        public static bool Scanning => (Disabled & HighMechanism.Scanning) == 0;
        public static bool AstralArts => (Disabled & HighMechanism.AstralArts) == 0;
        public static bool Maintenance => (Disabled & HighMechanism.Maintenance) == 0;
        public static bool NativeProtection => (Disabled & (HighMechanism.Native | HighMechanism.Maintenance)) == 0;
        public static bool ScannedProtection => (Disabled & (HighMechanism.Scanning | HighMechanism.Maintenance)) == 0;
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void Initialize(HighMechanism disabled)
        {
            if (initialized || Assembly.GetCallingAssembly() != typeof(HighMechanismPolicy).Assembly)
                throw new InvalidOperationException("High mechanisms are fixed by library startup.");
            HighMechanism selected = disabled & HighMechanism.All;
            Mechanisms.NativeHighMechanisms.Configure(selected);
            Disabled = selected;
            initialized = true;
        }
    }
}
