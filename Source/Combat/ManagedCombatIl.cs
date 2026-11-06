using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using LihuatLib.Mechanisms;

namespace LihuatLib.Combat
{
    internal sealed class ManagedCombatIl
    {
        internal const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private readonly LifeIlReader reader = new();
        private readonly Dictionary<MethodInfo, List<CodeInstruction>> code = new();
        internal byte[] ReadBody(MethodInfo method) => reader.ReadBody(method);
        internal List<CodeInstruction> Read(MethodInfo method)
        {
            if (code.TryGetValue(method, out var lines)) return lines;
            try { lines = reader.Read(method) ?? new(); }
            catch (Exception error) when (error is ArgumentException or MissingMemberException or TypeLoadException
                or FileNotFoundException or FileLoadException or BadImageFormatException)
            { lines = new(); }
            code[method] = lines;
            return lines;
        }
        internal static MethodInfo[] Methods(Type type) => type.GetMethods(Members).Where(m => !m.IsAbstract && !m.ContainsGenericParameters).ToArray();
        internal static T Only<T>(IEnumerable<T> values) where T : class
        {
            T[] candidates = values.Distinct().Take(2).ToArray();
            return candidates.Length == 1 ? candidates[0] : null;
        }
        internal static bool Signature(MethodInfo method, params Type[] args) => method.GetParameters().Select(p => p.ParameterType).SequenceEqual(args);
        internal static int? Integer(CodeInstruction line)
        {
            if (line.opcode == OpCodes.Ldc_I4 || line.opcode == OpCodes.Ldc_I4_S) return Convert.ToInt32(line.operand);
            if (line.opcode.Value >= OpCodes.Ldc_I4_0.Value && line.opcode.Value <= OpCodes.Ldc_I4_8.Value)
                return line.opcode.Value - OpCodes.Ldc_I4_0.Value;
            return null;
        }
        internal static int? StoredLocal(OpCode op, object operand)
        {
            if (op == OpCodes.Stloc || op == OpCodes.Stloc_S) return Convert.ToInt32(operand);
            if (op.Value >= OpCodes.Stloc_0.Value && op.Value <= OpCodes.Stloc_3.Value) return op.Value - OpCodes.Stloc_0.Value;
            return null;
        }
        internal static int? LoadedLocal(CodeInstruction line)
        {
            if (line.opcode == OpCodes.Ldloc || line.opcode == OpCodes.Ldloc_S) return Convert.ToInt32(line.operand);
            if (line.opcode.Value >= OpCodes.Ldloc_0.Value && line.opcode.Value <= OpCodes.Ldloc_3.Value) return line.opcode.Value - OpCodes.Ldloc_0.Value;
            return null;
        }
        internal static bool IsList(Type type, Type item) => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>)
            && type.GetGenericArguments()[0] == item;
        internal static bool Calls(CodeInstruction line, Type owner, string name) => line.operand is MethodInfo call
            && call.DeclaringType == owner && call.Name == name;
    }
}
