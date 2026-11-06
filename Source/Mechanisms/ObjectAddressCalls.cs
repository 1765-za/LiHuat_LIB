using System;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;

namespace LihuatLib.Mechanisms
{
    internal static class ObjectAddressCalls
    {
        internal static MethodInfo Replacement(MethodInfo method)
        {
            if (method?.DeclaringType != typeof(Unsafe) || !method.IsGenericMethod) return null;
            Type[] types = method.GetGenericArguments();
            ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length != 1) return null;
            string name = method.Name switch
            {
                nameof(Unsafe.As) when types.Length == 1 && parameters[0].ParameterType == typeof(object)
                    => nameof(ObjectAddressBoundary.Cast),
                nameof(Unsafe.As) when types.Length == 2 && parameters[0].ParameterType.IsByRef && !types[0].IsValueType
                    => nameof(ObjectAddressBoundary.Reinterpret),
                nameof(Unsafe.AsPointer) when types.Length == 1 && parameters[0].ParameterType.IsByRef && !types[0].IsValueType
                    => nameof(ObjectAddressBoundary.Address),
                nameof(Unsafe.Unbox) when types.Length == 1 && parameters[0].ParameterType == typeof(object)
                    => nameof(ObjectAddressBoundary.Unbox),
                _ => null
            };
            return name == null ? null : typeof(ObjectAddressBoundary).GetMethod(name,
                BindingFlags.Static | BindingFlags.NonPublic).MakeGenericMethod(types);
        }

        internal static byte[] Rewrite(byte[] code, Func<int, MemberInfo> resolve, Func<MethodInfo, int> tokenFor)
        {
            var instructions = LifeIlReader.Decode(code, (_, token) => resolve(token));
            if (instructions == null) return code;
            byte[] rewritten = null;
            int offset = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Call && instruction.operand is MethodInfo called
                    && Replacement(called) is MethodInfo guarded)
                {
                    rewritten ??= (byte[])code.Clone();
                    Array.Copy(BitConverter.GetBytes(tokenFor(guarded)), 0, rewritten, offset + 1, 4);
                }
                offset += instruction.opcode.Size + DynamicRootMutation.OperandSize(instruction);
            }
            return rewritten ?? code;
        }
    }
}
