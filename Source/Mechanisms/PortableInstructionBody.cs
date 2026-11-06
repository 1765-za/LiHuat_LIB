using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace LihuatLib.Mechanisms
{
    public static partial class PortableIlEmitter
    {
        internal static List<CodeInstruction> ReadInstructions(MethodBase original, ILGenerator generator)
        {
            MethodBody body = NativeRuntime.SourceBody(original)
                ?? throw new InvalidOperationException("Missing source body: " + original);
            byte[] bytes = CilRewrite.ReadSource(original, body);
            LocalBuilder[] locals = body.LocalVariables.Select(v => generator.DeclareLocal(EmittedLocalType(v.LocalType), v.IsPinned)).ToArray();
            List<Instruction> decoded = Decode(original, bytes);
            Dictionary<int, Label> labels = BuildLabels(generator, decoded, bytes.Length);
            var destinations = new HashSet<int>();
            foreach (Instruction line in decoded)
                if (line.OpCode.OperandType == OperandType.InlineBrTarget || line.OpCode.OperandType == OperandType.ShortInlineBrTarget)
                    destinations.Add((int)line.Operand);
                else if (line.Operand is int[] targets) destinations.UnionWith(targets);
            Dictionary<int, List<ExceptionEvent>> events = BuildExceptionEvents(body, decoded, bytes.Length);
            var result = new List<CodeInstruction>(decoded.Count + 1);
            foreach (Instruction line in decoded)
            {
                object operand = line.Operand;
                if (line.OpCode.OperandType == OperandType.InlineBrTarget || line.OpCode.OperandType == OperandType.ShortInlineBrTarget)
                    operand = labels[(int)operand];
                else if (operand is int[] targets) operand = targets.Select(t => labels[t]).ToArray();
                else if ((line.OpCode.OperandType == OperandType.InlineVar || line.OpCode.OperandType == OperandType.ShortInlineVar)
                    && line.OpCode.Name.Contains("loc")) operand = locals[Convert.ToInt32(operand)];
                var instruction = new CodeInstruction(line.OpCode, operand);
                if (destinations.Contains(line.Offset)) instruction.labels.Add(labels[line.Offset]);
                AddBlocks(result, instruction, events, line.Offset);
                result.Add(instruction);
            }
            var end = new CodeInstruction(OpCodes.Nop);
            AddBlocks(result, end, events, bytes.Length);
            return result;
        }

        // Reflection.Emit cannot import runtime function-pointer Type tokens; calli retains the complete ABI.
        private static Type EmittedLocalType(Type type) => type.IsFunctionPointer ? typeof(IntPtr)
            : type.IsByRef && type.GetElementType().IsFunctionPointer ? typeof(IntPtr).MakeByRefType() : type;

        private static void AddBlocks(List<CodeInstruction> previous, CodeInstruction current,
            Dictionary<int, List<ExceptionEvent>> events, int offset)
        {
            if (!events.TryGetValue(offset, out List<ExceptionEvent> transitions)) return;
            foreach (ExceptionEvent transition in transitions)
            {
                if (transition.Kind == ExceptionEventKind.EndRegion)
                { previous[previous.Count - 1].blocks.Add(new ExceptionBlock(ExceptionBlockType.EndExceptionBlock)); continue; }
                ExceptionBlockType type = transition.Kind == ExceptionEventKind.BeginRegion ? ExceptionBlockType.BeginExceptionBlock
                    : transition.Kind == ExceptionEventKind.BeginFilter ? ExceptionBlockType.BeginExceptFilterBlock
                    : transition.Clause.Flags == ExceptionHandlingClauseOptions.Finally ? ExceptionBlockType.BeginFinallyBlock
                    : transition.Clause.Flags == ExceptionHandlingClauseOptions.Fault ? ExceptionBlockType.BeginFaultBlock
                    : ExceptionBlockType.BeginCatchBlock;
                current.blocks.Add(new ExceptionBlock(type, transition.Clause?.Flags == ExceptionHandlingClauseOptions.Clause
                    ? transition.Clause.CatchType : null));
            }
        }

        internal static void EmitInstructions(ILGenerator il, IEnumerable<CodeInstruction> instructions,
            Func<OpCode, MethodBase, bool> runtimeCall = null)
        {
            foreach (CodeInstruction line in instructions)
            {
                foreach (ExceptionBlock block in line.blocks)
                    switch (block.blockType)
                    {
                        case ExceptionBlockType.BeginExceptionBlock: il.BeginExceptionBlock(); break;
                        case ExceptionBlockType.BeginCatchBlock: il.BeginCatchBlock(block.catchType); break;
                        case ExceptionBlockType.BeginExceptFilterBlock: il.BeginExceptFilterBlock(); break;
                        case ExceptionBlockType.BeginFinallyBlock: il.BeginFinallyBlock(); break;
                        case ExceptionBlockType.BeginFaultBlock: il.BeginFaultBlock(); break;
                    }
                foreach (Label label in line.labels) il.MarkLabel(label);
                if (!(line.operand is MethodBase called && runtimeCall?.Invoke(line.opcode, called) == true)) EmitInstruction(il, line);
                foreach (ExceptionBlock block in line.blocks)
                    if (block.blockType == ExceptionBlockType.EndExceptionBlock) il.EndExceptionBlock();
            }
        }

        private static void EmitInstruction(ILGenerator il, CodeInstruction line)
        {
            OpCode code = line.opcode;
            if (code == OpCodes.Endfinally || code == OpCodes.Endfilter) return;
            if (code.OperandType == OperandType.ShortInlineBrTarget) code = ExpandShortBranch(code);
            switch (line.operand)
            {
                case null: il.Emit(code); break;
                case Label label: il.Emit(code, label); break;
                case Label[] labels: il.Emit(code, labels); break;
                case LocalBuilder local: il.Emit(code, local); break;
                case FieldInfo field: il.Emit(code, field); break;
                case MethodBase method: EmitMethod(il, code, method); break;
                case Type type: il.Emit(code, type); break;
                case string text: il.Emit(code, text); break;
                case float value: il.Emit(code, value); break;
                case double value: il.Emit(code, value); break;
                case long value: il.Emit(code, value); break;
                case CallSiteSignature signature: signature.Emit(il, code); break;
                default:
                    if (code.OperandType == OperandType.InlineVar) il.Emit(code, unchecked((short)Convert.ToInt32(line.operand)));
                    else if (code.OperandType == OperandType.ShortInlineVar) il.Emit(code, Convert.ToByte(line.operand));
                    else if (code.OperandType == OperandType.ShortInlineI) il.Emit(code, unchecked((sbyte)Convert.ToInt32(line.operand)));
                    else if (code.OperandType == OperandType.InlineI) il.Emit(code, Convert.ToInt32(line.operand));
                    else throw new NotSupportedException("Unsupported instruction operand: " + line);
                    break;
            }
        }
    }
}
