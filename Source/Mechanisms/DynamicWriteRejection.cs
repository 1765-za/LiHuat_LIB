using System;
using System.Reflection;
using System.Reflection.Emit;

namespace LihuatLib.Mechanisms
{
    internal static class DynamicWriteRejection
    {
        internal static void Emit(ILGenerator il, Type result, ParameterInfo[] parameters, int firstArgument)
        {
            il.Emit(OpCodes.Call, typeof(DynamicWritePolicy).GetMethod(nameof(DynamicWritePolicy.Denied),
                BindingFlags.Static | BindingFlags.NonPublic));
            if (result == typeof(void) || result == typeof(bool))
            {
                for (int i = 0; i < parameters.Length; i++)
                    if (parameters[i].IsOut)
                    {
                        il.Emit(OpCodes.Ldarg, (short)(i + firstArgument));
                        il.Emit(OpCodes.Initobj, parameters[i].ParameterType.GetElementType());
                    }
                if (result == typeof(bool)) il.Emit(OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Ret);
                return;
            }
            il.Emit(OpCodes.Ldstr, "Generated mutation of protected state was rejected.");
            il.Emit(OpCodes.Newobj, typeof(UnauthorizedAccessException).GetConstructor(new[] { typeof(string) }));
            il.Emit(OpCodes.Throw);
        }
    }
}
