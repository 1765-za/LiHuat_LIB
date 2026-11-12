using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using LihuatLib.Configuration;

namespace LihuatLib.Mechanisms
{
    internal static class NativeHighMechanisms
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int ConfigureCall(ulong authority, uint disabled);
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void Configure(HighMechanism disabled)
        {
            if (!InfrastructureAssemblies.IsCaller(Assembly.GetCallingAssembly(), typeof(NativeHighMechanisms).Assembly))
                throw new InvalidOperationException("Native mechanism selection requires library startup authority.");
            var configure = NativeModule.Bind<ConfigureCall>("LihuatNative_ConfigureHighMechanisms");
            if (configure(NativeModule.Authority, (uint)disabled) != 1)
                throw new InvalidOperationException("High mechanism selection must precede native initialization.");
        }
    }
}
