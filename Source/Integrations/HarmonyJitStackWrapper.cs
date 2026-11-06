using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using LihuatLib.Mechanisms;

namespace LihuatLib.Integrations
{
    internal static class HarmonyJitStackWrapper
    {
        // ThreadLocal inside a JIT callback can recursively compile its first contended-lock P/Invoke stub.
        internal static DynamicMethod Create(MethodInfo method)
        {
            MethodBody body = method.GetMethodBody();
            byte[] code = body.GetILAsByteArray();
            List<CodeInstruction> lines = LifeIlReader.Decode(method, code);
            var prepared = new HashSet<MethodBase>();
            foreach (CodeInstruction line in lines)
            {
                if (line.operand is not MethodBase called || called.Module.Assembly != method.Module.Assembly
                    || called.ContainsGenericParameters || called.IsGenericMethod || called.IsAbstract
                    || called.GetMethodBody() == null || !prepared.Add(called)) continue;
                // The dynamic callback needs callable helper entries even when metadata callers inline them.
                RuntimeHelpers.PrepareMethod(called.MethodHandle);
            }
            int[] accesses = Enumerable.Range(0, lines.Count).Where(i => lines[i].opcode == OpCodes.Ldfld
                && lines[i].operand is FieldInfo field && field.Name == "iCorJitInfoWrapper").ToArray();
            if (accesses.Length == 0) return null;
            if (accesses.Length != 3 || !body.InitLocals) throw new InvalidOperationException("Unexpected JIT wrapper lifetime.");
            int[] pointers = body.LocalVariables.Where(v => (v.LocalType.IsPointer || v.LocalType.IsByRef)
                && v.LocalType.GetElementType().Name == "ICorJitInfoWrapper").Select(v => v.LocalIndex).ToArray();
            if (pointers.Length != 2) throw new InvalidOperationException("Unexpected JIT wrapper locals.");
            int firstStore = lines.FindIndex(accesses[0], line => Stores(line, pointers[0]));
            int lastStore = lines.FindIndex(accesses[2], line => Stores(line, pointers[1]));
            Type wrapperType = body.LocalVariables[pointers[0]].LocalType.GetElementType();
            if (firstStore <= accesses[1] || lastStore <= accesses[2])
                throw new InvalidOperationException("Unexpected JIT wrapper allocation.");
            int[] offsets = new int[lines.Count + 1];
            for (int i = 0; i < lines.Count; i++) offsets[i + 1] = offsets[i] + Size(lines[i]);
            if (offsets[lines.Count] != code.Length) throw new InvalidOperationException("JIT wrapper IL length differs.");
            foreach (int index in new[] { accesses[0], accesses[2] })
                if (lines[index - 1].opcode != OpCodes.Ldarg_0) throw new InvalidOperationException("Unexpected JIT wrapper receiver.");
            Array.Clear(code, offsets[accesses[0] - 1], offsets[firstStore + 1] - offsets[accesses[0] - 1]);
            int reload = offsets[accesses[2] - 1];
            Array.Clear(code, reload, offsets[lastStore + 1] - reload);
            using (var stream = new MemoryStream(code, true))
            using (var writer = new BinaryWriter(stream))
            {
                stream.Position = reload;
                writer.Write((byte)0xfe); writer.Write((byte)0x0c); writer.Write((ushort)pointers[0]);
                writer.Write((byte)0xfe); writer.Write((byte)0x0e); writer.Write((ushort)pointers[1]);
            }
            var result = new DynamicMethod("LihuatJitCompile", method.ReturnType, IndependentIl.Parameters(method), method.Module, true);
            ILGenerator il = result.GetILGenerator();
            il.Emit(OpCodes.Sizeof, wrapperType); il.Emit(OpCodes.Conv_U); il.Emit(OpCodes.Localloc);
            il.Emit(OpCodes.Stloc, (short)pointers[0]);
            if (!PortableIlEmitter.TryEmit(method, body, code, il, out string reason))
                throw new InvalidOperationException("JIT stack wrapper emission failed: " + reason);
            return result;
        }

        private static bool Stores(CodeInstruction line, int local) =>
            (line.opcode == OpCodes.Stloc || line.opcode == OpCodes.Stloc_S) && (int)line.operand == local;

        private static int Size(CodeInstruction line) => line.opcode.Size + (line.opcode.OperandType switch
        {
            OperandType.InlineNone => 0,
            OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
            OperandType.InlineVar => 2,
            OperandType.InlineI8 or OperandType.InlineR => 8,
            OperandType.InlineSwitch => 4 + 4 * ((Label[])line.operand).Length,
            _ => 4
        });
    }
}
