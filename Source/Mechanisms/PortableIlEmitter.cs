using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace LihuatLib.Mechanisms
{
    /// <summary>
    /// Re-emits metadata IL with runtime-owned local and exception signatures.
    /// </summary>
    public static partial class PortableIlEmitter
    {
        private static readonly OpCode[] SingleByteOpCodes = new OpCode[0x100];
        private static readonly OpCode[] MultiByteOpCodes = new OpCode[0x100];

        static PortableIlEmitter()
        {
            FieldInfo[] fields = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static);
            for (int i = 0; i < fields.Length; i++)
            {
                if (fields[i].FieldType != typeof(OpCode))
                {
                    continue;
                }

                OpCode opcode = (OpCode)fields[i].GetValue(null);
                ushort value = unchecked((ushort)opcode.Value);
                if (value < 0x100)
                {
                    SingleByteOpCodes[value] = opcode;
                }
                else if ((value & 0xff00) == 0xfe00)
                {
                    MultiByteOpCodes[value & 0xff] = opcode;
                }
            }
        }

        public static bool TryEmit(
            MethodInfo original,
            MethodBody body,
            byte[] sourceIl,
            ILGenerator generator,
            out string reason,
            Func<MethodInfo, bool, MethodInfo> remap = null,
            Func<ILGenerator, OpCode, object, bool> rewrite = null)
            => TryEmit((MethodBase)original, body, sourceIl, generator, out reason, remap, rewrite);

        internal static bool TryEmit(MethodBase original, MethodBody body, byte[] sourceIl, ILGenerator generator,
            out string reason, Func<MethodInfo, bool, MethodInfo> remap = null,
            Func<ILGenerator, OpCode, object, bool> rewrite = null)
        {
            reason = null;
            if (original == null || body == null || sourceIl == null || generator == null)
            {
                reason = "portable IL emitter input is incomplete";
                return false;
            }

            List<int> callTargets = null;
            try
            {
                IList<LocalVariableInfo> locals = body.LocalVariables;
                if (locals != null)
                {
                    for (int i = 0; i < locals.Count; i++)
                    {
                        generator.DeclareLocal(locals[i].LocalType, locals[i].IsPinned);
                    }
                }

                List<Instruction> instructions = Decode(original, sourceIl);
                Dictionary<int, Label> labels = BuildLabels(generator, instructions, sourceIl.Length);
                Dictionary<int, List<ExceptionEvent>> exceptionEvents = BuildExceptionEvents(
                    body,
                    instructions,
                    sourceIl.Length);
                Stack<ExceptionRegion> activeRegions = new Stack<ExceptionRegion>();
                var incoming = new HashSet<int>();
                foreach (Instruction line in instructions)
                {
                    if (line.OpCode.OperandType == OperandType.InlineBrTarget || line.OpCode.OperandType == OperandType.ShortInlineBrTarget)
                        incoming.Add((int)line.Operand);
                    else if (line.Operand is int[] targets) incoming.UnionWith(targets);
                }
                Type delegateConstruction = null;
                for (int i = 0; i < instructions.Count; i++)
                {
                    Instruction instruction = instructions[i];
                    EmitExceptionEvents(generator, instruction.Offset, exceptionEvents, activeRegions);
                    generator.MarkLabel(labels[instruction.Offset]);
                    if (delegateConstruction != null)
                    {
                        generator.Emit(OpCodes.Call, typeof(PortableCallTargets).GetMethod(nameof(PortableCallTargets.Bind), BindingFlags.Static | BindingFlags.NonPublic));
                        generator.Emit(OpCodes.Castclass, delegateConstruction);
                        delegateConstruction = null;
                        continue;
                    }
                    if (rewrite != null && rewrite(generator, instruction.OpCode, instruction.Operand)) continue;
                    if (remap != null && instruction.Operand is MethodInfo pointer
                        && (instruction.OpCode == OpCodes.Ldftn || instruction.OpCode == OpCodes.Ldvirtftn))
                    {
                        Instruction next = i + 1 < instructions.Count ? instructions[i + 1] : null;
                        Type delegateType = next != null && next.OpCode == OpCodes.Newobj && next.Operand is ConstructorInfo constructor
                            && typeof(MulticastDelegate).IsAssignableFrom(constructor.DeclaringType) ? constructor.DeclaringType : null;
                        bool constructorEntry = delegateType != null && (incoming.Contains(next.Offset) || exceptionEvents.ContainsKey(next.Offset));
                        if (!constructorEntry && (delegateType != null || pointer.IsStatic))
                        {
                            MethodInfo replacement = remap(pointer, instruction.OpCode == OpCodes.Ldvirtftn);
                            if (replacement != pointer)
                            {
                                int id = PortableCallTargets.Register(pointer, replacement, delegateType);
                                (callTargets ??= new List<int>()).Add(id);
                                if (instruction.OpCode == OpCodes.Ldvirtftn) PortableCallTargets.LookupReceiver(generator);
                                generator.Emit(OpCodes.Ldc_I4, id);
                                if (delegateType != null) delegateConstruction = delegateType;
                                else generator.Emit(OpCodes.Call, typeof(PortableCallTargets).GetMethod(nameof(PortableCallTargets.Address), BindingFlags.Static | BindingFlags.NonPublic));
                                continue;
                            }
                        }
                    }
                    if (remap != null && instruction.Operand is MethodInfo called &&
                        (instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt))
                    {
                        MethodInfo replacement = remap(called, instruction.OpCode == OpCodes.Callvirt);
                        if (replacement != called)
                        {
                            generator.Emit(OpCodes.Call, replacement);
                            continue;
                        }
                    }
                    Emit(generator, instruction, labels);
                }

                EmitExceptionEvents(generator, sourceIl.Length, exceptionEvents, activeRegions);
                generator.MarkLabel(labels[sourceIl.Length]);
                if (activeRegions.Count != 0)
                {
                    throw new BadImageFormatException("independent IL clone ended with open exception regions");
                }

                return true;
            }
            catch (Exception ex)
            {
                if (callTargets != null) foreach (int id in callTargets) PortableCallTargets.Remove(id);
                reason = ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        private static Dictionary<int, List<ExceptionEvent>> BuildExceptionEvents(
            MethodBody body,
            List<Instruction> instructions,
            int endOffset)
        {
            Dictionary<int, List<ExceptionEvent>> events =
                new Dictionary<int, List<ExceptionEvent>>();
            IList<ExceptionHandlingClause> clauses = body.ExceptionHandlingClauses;
            if (clauses == null || clauses.Count == 0)
            {
                return events;
            }

            HashSet<int> validOffsets = new HashSet<int>();
            for (int i = 0; i < instructions.Count; i++)
            {
                validOffsets.Add(instructions[i].Offset);
            }

            validOffsets.Add(endOffset);
            List<ExceptionRegion> regions = new List<ExceptionRegion>();
            for (int i = 0; i < clauses.Count; i++)
            {
                ExceptionHandlingClause clause = clauses[i];
                int tryEnd = checked(clause.TryOffset + clause.TryLength);
                int handlerEnd = checked(clause.HandlerOffset + clause.HandlerLength);
                RequireValidTarget(validOffsets, clause.TryOffset);
                RequireValidTarget(validOffsets, tryEnd);
                RequireValidTarget(validOffsets, clause.HandlerOffset);
                RequireValidTarget(validOffsets, handlerEnd);
                if (clause.Flags == ExceptionHandlingClauseOptions.Filter)
                {
                    RequireValidTarget(validOffsets, clause.FilterOffset);
                }

                ExceptionRegion region = null;
                for (int j = 0; j < regions.Count; j++)
                {
                    if (regions[j].TryStart == clause.TryOffset
                        && regions[j].TryEnd == tryEnd)
                    {
                        region = regions[j];
                        break;
                    }
                }

                if (region == null)
                {
                    region = new ExceptionRegion(clause.TryOffset, tryEnd);
                    regions.Add(region);
                }

                region.Add(clause, handlerEnd);
            }

            for (int i = 0; i < regions.Count; i++)
            {
                ExceptionRegion region = regions[i];
                region.SortHandlers();
                AddExceptionEvent(events, region.TryStart, new ExceptionEvent(
                    ExceptionEventKind.BeginRegion,
                    region,
                    null));
                for (int j = 0; j < region.Handlers.Count; j++)
                {
                    ExceptionHandlingClause clause = region.Handlers[j];
                    if (clause.Flags == ExceptionHandlingClauseOptions.Filter)
                    {
                        AddExceptionEvent(events, clause.FilterOffset, new ExceptionEvent(
                            ExceptionEventKind.BeginFilter,
                            region,
                            clause));
                    }

                    AddExceptionEvent(events, clause.HandlerOffset, new ExceptionEvent(
                        ExceptionEventKind.BeginHandler,
                        region,
                        clause));
                }

                AddExceptionEvent(events, region.EndOffset, new ExceptionEvent(
                    ExceptionEventKind.EndRegion,
                    region,
                    null));
            }

            foreach (List<ExceptionEvent> atOffset in events.Values)
            {
                atOffset.Sort(CompareExceptionEvents);
            }

            return events;
        }

        private static void AddExceptionEvent(
            Dictionary<int, List<ExceptionEvent>> events,
            int offset,
            ExceptionEvent exceptionEvent)
        {
            if (!events.TryGetValue(offset, out List<ExceptionEvent> atOffset))
            {
                atOffset = new List<ExceptionEvent>();
                events.Add(offset, atOffset);
            }

            atOffset.Add(exceptionEvent);
        }

        private static int CompareExceptionEvents(ExceptionEvent left, ExceptionEvent right)
        {
            int kind = left.Kind.CompareTo(right.Kind);
            if (kind != 0)
            {
                return kind;
            }

            switch (left.Kind)
            {
                case ExceptionEventKind.EndRegion:
                    return right.Region.TryStart.CompareTo(left.Region.TryStart);
                case ExceptionEventKind.BeginRegion:
                    return right.Region.EndOffset.CompareTo(left.Region.EndOffset);
                default:
                    return right.Region.TryStart.CompareTo(left.Region.TryStart);
            }
        }

        private static void EmitExceptionEvents(
            ILGenerator generator,
            int offset,
            Dictionary<int, List<ExceptionEvent>> events,
            Stack<ExceptionRegion> activeRegions)
        {
            if (!events.TryGetValue(offset, out List<ExceptionEvent> atOffset))
            {
                return;
            }

            for (int i = 0; i < atOffset.Count; i++)
            {
                ExceptionEvent exceptionEvent = atOffset[i];
                ExceptionRegion region = exceptionEvent.Region;
                switch (exceptionEvent.Kind)
                {
                    case ExceptionEventKind.EndRegion:
                        RequireActiveRegion(activeRegions, region, offset);
                        generator.EndExceptionBlock();
                        activeRegions.Pop();
                        break;
                    case ExceptionEventKind.BeginFilter:
                        RequireActiveRegion(activeRegions, region, offset);
                        generator.BeginExceptFilterBlock();
                        break;
                    case ExceptionEventKind.BeginHandler:
                        RequireActiveRegion(activeRegions, region, offset);
                        BeginHandler(generator, exceptionEvent.Clause);
                        break;
                    case ExceptionEventKind.BeginRegion:
                        generator.BeginExceptionBlock();
                        activeRegions.Push(region);
                        break;
                    default:
                        throw new BadImageFormatException("unknown exception event at IL_" + offset.ToString("X4"));
                }
            }
        }

        private static void BeginHandler(ILGenerator generator, ExceptionHandlingClause clause)
        {
            switch (clause.Flags)
            {
                case ExceptionHandlingClauseOptions.Clause:
                    generator.BeginCatchBlock(clause.CatchType);
                    return;
                case ExceptionHandlingClauseOptions.Filter:
                    generator.BeginCatchBlock(null);
                    return;
                case ExceptionHandlingClauseOptions.Finally:
                    generator.BeginFinallyBlock();
                    return;
                case ExceptionHandlingClauseOptions.Fault:
                    generator.BeginFaultBlock();
                    return;
                default:
                    throw new NotSupportedException("unsupported exception clause: " + clause.Flags);
            }
        }

        private static void RequireActiveRegion(
            Stack<ExceptionRegion> activeRegions,
            ExceptionRegion expected,
            int offset)
        {
            if (activeRegions.Count == 0 || !ReferenceEquals(activeRegions.Peek(), expected))
            {
                throw new BadImageFormatException(
                    "non-nested exception region transition at IL_" + offset.ToString("X4"));
            }
        }

        private static List<Instruction> Decode(MethodBase original, byte[] il)
        {
            List<Instruction> instructions = new List<Instruction>();
            Module module = original.Module;
            Type[] typeArguments = original.DeclaringType?.GetGenericArguments() ?? Type.EmptyTypes;
            Type[] methodArguments = original.IsGenericMethod ? original.GetGenericArguments() : Type.EmptyTypes;
            int offset = 0;
            while (offset < il.Length)
            {
                int instructionOffset = offset;
                OpCode opcode = ReadOpCode(il, ref offset);
                object operand;
                switch (opcode.OperandType)
                {
                    case OperandType.InlineNone:
                        operand = null;
                        break;
                    case OperandType.ShortInlineBrTarget:
                        EnsureRemaining(il, offset, 1);
                        sbyte shortDelta = unchecked((sbyte)il[offset]);
                        offset += 1;
                        operand = offset + shortDelta;
                        break;
                    case OperandType.InlineBrTarget:
                        EnsureRemaining(il, offset, 4);
                        int delta = ReadInt32(il, offset);
                        offset += 4;
                        operand = offset + delta;
                        break;
                    case OperandType.ShortInlineI:
                        EnsureRemaining(il, offset, 1);
                        operand = unchecked((sbyte)il[offset]);
                        offset += 1;
                        break;
                    case OperandType.InlineI:
                        EnsureRemaining(il, offset, 4);
                        operand = ReadInt32(il, offset);
                        offset += 4;
                        break;
                    case OperandType.InlineI8:
                        EnsureRemaining(il, offset, 8);
                        operand = BitConverter.ToInt64(il, offset);
                        offset += 8;
                        break;
                    case OperandType.ShortInlineR:
                        EnsureRemaining(il, offset, 4);
                        operand = BitConverter.ToSingle(il, offset);
                        offset += 4;
                        break;
                    case OperandType.InlineR:
                        EnsureRemaining(il, offset, 8);
                        operand = BitConverter.ToDouble(il, offset);
                        offset += 8;
                        break;
                    case OperandType.ShortInlineVar:
                        EnsureRemaining(il, offset, 1);
                        operand = il[offset];
                        offset += 1;
                        break;
                    case OperandType.InlineVar:
                        EnsureRemaining(il, offset, 2);
                        operand = ReadUInt16(il, offset);
                        offset += 2;
                        break;
                    case OperandType.InlineString:
                        operand = module.ResolveString(ReadToken(il, ref offset));
                        break;
                    case OperandType.InlineType:
                        operand = module.ResolveType(ReadToken(il, ref offset), typeArguments, methodArguments);
                        break;
                    case OperandType.InlineField:
                        operand = module.ResolveField(ReadToken(il, ref offset), typeArguments, methodArguments);
                        break;
                    case OperandType.InlineMethod:
                        operand = module.ResolveMethod(ReadToken(il, ref offset), typeArguments, methodArguments);
                        break;
                    case OperandType.InlineTok:
                        operand = module.ResolveMember(ReadToken(il, ref offset), typeArguments, methodArguments);
                        break;
                    case OperandType.InlineSwitch:
                        EnsureRemaining(il, offset, 4);
                        int count = ReadInt32(il, offset);
                        offset += 4;
                        if (count < 0 || count > (il.Length - offset) / 4)
                        {
                            throw new BadImageFormatException("invalid switch table in original IL");
                        }

                        int tableEnd = offset + (count * 4);
                        int[] targets = new int[count];
                        for (int i = 0; i < count; i++)
                        {
                            targets[i] = tableEnd + ReadInt32(il, offset);
                            offset += 4;
                        }

                        operand = targets;
                        break;
                    case OperandType.InlineSig:
                        operand = CallSiteSignature.Parse(
                            module,
                            module.ResolveSignature(ReadToken(il, ref offset)),
                            typeArguments,
                            methodArguments);
                        break;
                    default:
                        throw new NotSupportedException("unsupported IL operand: " + opcode.OperandType);
                }

                instructions.Add(new Instruction(instructionOffset, opcode, operand));
            }

            return instructions;
        }

        private static Dictionary<int, Label> BuildLabels(
            ILGenerator generator,
            List<Instruction> instructions,
            int endOffset)
        {
            HashSet<int> validOffsets = new HashSet<int>();
            for (int i = 0; i < instructions.Count; i++)
            {
                validOffsets.Add(instructions[i].Offset);
            }

            validOffsets.Add(endOffset);
            for (int i = 0; i < instructions.Count; i++)
            {
                Instruction instruction = instructions[i];
                if (instruction.Operand is int branchTarget
                    && (instruction.OpCode.OperandType == OperandType.ShortInlineBrTarget
                        || instruction.OpCode.OperandType == OperandType.InlineBrTarget))
                {
                    RequireValidTarget(validOffsets, branchTarget);
                }
                else if (instruction.Operand is int[] switchTargets)
                {
                    for (int j = 0; j < switchTargets.Length; j++)
                    {
                        RequireValidTarget(validOffsets, switchTargets[j]);
                    }
                }
            }

            Dictionary<int, Label> labels = new Dictionary<int, Label>();
            foreach (int validOffset in validOffsets)
            {
                labels.Add(validOffset, generator.DefineLabel());
            }

            return labels;
        }

        private static void Emit(ILGenerator generator, Instruction instruction, Dictionary<int, Label> labels)
        {
            OpCode opcode = instruction.OpCode;
            // Structured ILGenerator transitions emit these terminators when a
            // filter handler begins or an exception block ends. Re-emitting the
            // metadata terminator would produce a second endfilter/endfinally;
            // the duplicate endfilter in particular reaches the JIT with an empty
            // evaluation stack and turns an otherwise valid clone into InvalidProgram.
            if (opcode == OpCodes.Endfilter || opcode == OpCodes.Endfinally)
            {
                return;
            }

            switch (opcode.OperandType)
            {
                case OperandType.InlineNone:
                    generator.Emit(opcode);
                    return;
                case OperandType.ShortInlineBrTarget:
                    generator.Emit(ExpandShortBranch(opcode), labels[(int)instruction.Operand]);
                    return;
                case OperandType.InlineBrTarget:
                    generator.Emit(opcode, labels[(int)instruction.Operand]);
                    return;
                case OperandType.ShortInlineI:
                    generator.Emit(opcode, (sbyte)instruction.Operand);
                    return;
                case OperandType.InlineI:
                    generator.Emit(opcode, (int)instruction.Operand);
                    return;
                case OperandType.InlineI8:
                    generator.Emit(opcode, (long)instruction.Operand);
                    return;
                case OperandType.ShortInlineR:
                    generator.Emit(opcode, (float)instruction.Operand);
                    return;
                case OperandType.InlineR:
                    generator.Emit(opcode, (double)instruction.Operand);
                    return;
                case OperandType.ShortInlineVar:
                    generator.Emit(opcode, (byte)instruction.Operand);
                    return;
                case OperandType.InlineVar:
                    generator.Emit(opcode, unchecked((short)(ushort)instruction.Operand));
                    return;
                case OperandType.InlineString:
                    generator.Emit(opcode, (string)instruction.Operand);
                    return;
                case OperandType.InlineType:
                    generator.Emit(opcode, (Type)instruction.Operand);
                    return;
                case OperandType.InlineField:
                    generator.Emit(opcode, (FieldInfo)instruction.Operand);
                    return;
                case OperandType.InlineMethod:
                    EmitMethod(generator, opcode, (MethodBase)instruction.Operand);
                    return;
                case OperandType.InlineTok:
                    EmitMember(generator, opcode, (MemberInfo)instruction.Operand);
                    return;
                case OperandType.InlineSwitch:
                    int[] targetOffsets = (int[])instruction.Operand;
                    Label[] targetLabels = new Label[targetOffsets.Length];
                    for (int i = 0; i < targetOffsets.Length; i++)
                    {
                        targetLabels[i] = labels[targetOffsets[i]];
                    }

                    generator.Emit(opcode, targetLabels);
                    return;
                case OperandType.InlineSig:
                    ((CallSiteSignature)instruction.Operand).Emit(generator, opcode);
                    return;
                default:
                    throw new NotSupportedException("unsupported emitted IL operand: " + opcode.OperandType);
            }
        }

        private static OpCode ExpandShortBranch(OpCode opcode)
        {
            if (opcode == OpCodes.Br_S) return OpCodes.Br;
            if (opcode == OpCodes.Brfalse_S) return OpCodes.Brfalse;
            if (opcode == OpCodes.Brtrue_S) return OpCodes.Brtrue;
            if (opcode == OpCodes.Beq_S) return OpCodes.Beq;
            if (opcode == OpCodes.Bge_S) return OpCodes.Bge;
            if (opcode == OpCodes.Bgt_S) return OpCodes.Bgt;
            if (opcode == OpCodes.Ble_S) return OpCodes.Ble;
            if (opcode == OpCodes.Blt_S) return OpCodes.Blt;
            if (opcode == OpCodes.Bne_Un_S) return OpCodes.Bne_Un;
            if (opcode == OpCodes.Bge_Un_S) return OpCodes.Bge_Un;
            if (opcode == OpCodes.Bgt_Un_S) return OpCodes.Bgt_Un;
            if (opcode == OpCodes.Ble_Un_S) return OpCodes.Ble_Un;
            if (opcode == OpCodes.Blt_Un_S) return OpCodes.Blt_Un;
            if (opcode == OpCodes.Leave_S) return OpCodes.Leave;
            throw new NotSupportedException("unknown short branch opcode: " + opcode.Name);
        }

        private static void EmitMethod(ILGenerator generator, OpCode opcode, MethodBase method)
        {
            if (method is MethodInfo methodInfo)
            {
                generator.Emit(opcode, methodInfo);
                return;
            }

            if (method is ConstructorInfo constructor)
            {
                generator.Emit(opcode, constructor);
                return;
            }

            throw new NotSupportedException("resolved method operand is not callable");
        }

        private static void EmitMember(ILGenerator generator, OpCode opcode, MemberInfo member)
        {
            if (member is Type type)
            {
                generator.Emit(opcode, type);
                return;
            }

            if (member is FieldInfo field)
            {
                generator.Emit(opcode, field);
                return;
            }

            EmitMethod(generator, opcode, member as MethodBase);
        }

        private static OpCode ReadOpCode(byte[] il, ref int offset)
        {
            EnsureRemaining(il, offset, 1);
            byte first = il[offset++];
            OpCode opcode;
            if (first == 0xfe)
            {
                EnsureRemaining(il, offset, 1);
                opcode = MultiByteOpCodes[il[offset++]];
            }
            else
            {
                opcode = SingleByteOpCodes[first];
            }

            if (opcode.Size == 0)
            {
                throw new BadImageFormatException("unknown opcode in original IL");
            }

            return opcode;
        }

        private static int ReadToken(byte[] data, ref int offset)
        {
            EnsureRemaining(data, offset, 4);
            int token = ReadInt32(data, offset);
            offset += 4;
            return token;
        }

        private static ushort ReadUInt16(byte[] data, int offset)
        {
            return (ushort)(data[offset] | (data[offset + 1] << 8));
        }

        private static int ReadInt32(byte[] data, int offset)
        {
            return data[offset]
                | (data[offset + 1] << 8)
                | (data[offset + 2] << 16)
                | (data[offset + 3] << 24);
        }

        private static void EnsureRemaining(byte[] il, int offset, int length)
        {
            if (offset < 0 || length < 0 || offset > il.Length - length)
            {
                throw new BadImageFormatException("truncated original IL body");
            }
        }

        private static void RequireValidTarget(HashSet<int> validOffsets, int target)
        {
            if (!validOffsets.Contains(target))
            {
                throw new BadImageFormatException("branch target does not land on an IL instruction: " + target);
            }
        }

        private sealed class Instruction
        {
            public readonly int Offset;
            public readonly OpCode OpCode;
            public readonly object Operand;

            public Instruction(int offset, OpCode opCode, object operand)
            {
                Offset = offset;
                OpCode = opCode;
                Operand = operand;
            }
        }

        private enum ExceptionEventKind
        {
            EndRegion = 0,
            BeginFilter = 1,
            BeginHandler = 2,
            BeginRegion = 3
        }

        private sealed class ExceptionEvent
        {
            public readonly ExceptionEventKind Kind;
            public readonly ExceptionRegion Region;
            public readonly ExceptionHandlingClause Clause;

            public ExceptionEvent(
                ExceptionEventKind kind,
                ExceptionRegion region,
                ExceptionHandlingClause clause)
            {
                Kind = kind;
                Region = region;
                Clause = clause;
            }
        }

        private sealed class ExceptionRegion
        {
            public readonly int TryStart;
            public readonly int TryEnd;
            public readonly List<ExceptionHandlingClause> Handlers =
                new List<ExceptionHandlingClause>();
            public int EndOffset;

            public ExceptionRegion(int tryStart, int tryEnd)
            {
                TryStart = tryStart;
                TryEnd = tryEnd;
                EndOffset = tryEnd;
            }

            public void Add(ExceptionHandlingClause clause, int handlerEnd)
            {
                Handlers.Add(clause);
                if (handlerEnd > EndOffset)
                {
                    EndOffset = handlerEnd;
                }
            }

            public void SortHandlers()
            {
                Handlers.Sort((left, right) => HandlerStart(left).CompareTo(HandlerStart(right)));
            }

            private static int HandlerStart(ExceptionHandlingClause clause)
            {
                return clause.Flags == ExceptionHandlingClauseOptions.Filter
                    ? clause.FilterOffset
                    : clause.HandlerOffset;
            }
        }
    }
}
