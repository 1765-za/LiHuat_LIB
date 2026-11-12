using System;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using HarmonyLib;
using LihuatLib.Mechanisms;

namespace LihuatLib.Integrations
{
    internal static class HarmonyCodeWriter
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private unsafe delegate int WriteCall(ulong authority, IntPtr target, byte* data, nuint length,
            byte* backup, nuint backupLength, int executable);
        private static readonly WriteCall write = NativeModule.Bind<WriteCall>("LihuatNative_PatchBytes");

        internal static void Install()
        {
            MethodInfo target = AccessTools.DeclaredMethod(typeof(Harmony).Assembly.GetType("MonoMod.Core.Platforms.Systems.WindowsSystem", true), "PatchData");
            NativeIlGuards.RegisterTransform(target, typeof(HarmonyCodeWriter).FullName, NativeIlGuards.BodyOrder, _ => Body(target));
        }

        private static DynamicMethod Body(MethodInfo original)
        {
            DynamicMethod method = IndependentIl.New(original);
            ILGenerator il = method.GetILGenerator();
            for (short i = 1; i <= 4; i++) il.Emit(OpCodes.Ldarg, i);
            il.Emit(OpCodes.Call, AccessTools.DeclaredMethod(typeof(HarmonyCodeWriter), nameof(Write)));
            il.Emit(OpCodes.Ret);
            return method;
        }

        private static unsafe void Write(int kind, IntPtr target, ReadOnlySpan<byte> data, Span<byte> backup)
        {
            // A neighboring protected method can share the same Windows page as this exact write range.
            fixed (byte* input = data)
            fixed (byte* output = backup)
            {
                int result = write(NativeModule.Authority, target, input, (nuint)data.Length, output, (nuint)backup.Length, kind == 0 ? 1 : 0);
                if (result == -12) throw new UnauthorizedAccessException("Cannot overwrite protected native memory.");
                if (result != 1) throw new InvalidOperationException("Native patch publication failed: " + result);
            }
        }
    }
}
