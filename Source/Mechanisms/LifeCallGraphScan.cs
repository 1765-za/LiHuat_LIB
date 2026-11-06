using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;

namespace LihuatLib.Mechanisms
{
    internal sealed partial class LifeCallGraphReader
    {
        private static readonly OpCode[] scanOpcodes = ScanOpcodes();
        private readonly List<int> branches = new();
        private byte[] starts = Array.Empty<byte>();

        internal bool Scan(MethodInfo method, byte[] body, Dictionary<MethodInfo, LifeCallKind> targets, out int indirect)
        {
            targets.Clear(); branches.Clear(); indirect = 0;
            try
            {
                if (body == null) throw new BadImageFormatException("Method IL could not be decoded.");
                int maskBytes = body.Length / 8 + (body.Length % 8 == 0 ? 0 : 1);
                if (starts.Length < maskBytes) starts = new byte[Math.Max(maskBytes, starts.Length * 2)];
                starts.AsSpan(0, maskBytes).Clear();
                Type[] types = method.DeclaringType?.IsGenericType == true ? method.DeclaringType.GetGenericArguments() : null;
                Type[] arguments = method.IsGenericMethod ? method.GetGenericArguments() : null;
                int position = 0;
                while (position < body.Length)
                {
                    int offset = position;
                    starts[offset >> 3] |= (byte)(1 << (offset & 7));
                    int code = body[position++];
                    if (code == 0xfe) { Require(body, position, 1); code = 256 + body[position++]; }
                    OpCode opcode = scanOpcodes[code];
                    if (opcode.Size == 0) throw new BadImageFormatException("Unknown IL opcode.");
                    switch (opcode.OperandType)
                    {
                        case OperandType.InlineNone: break;
                        case OperandType.ShortInlineBrTarget:
                            Require(body, position, 1);
                            branches.Add(checked(position + 1 + unchecked((sbyte)body[position]))); position++; break;
                        case OperandType.InlineBrTarget:
                            branches.Add(checked(position + 4 + Integer(body, position))); position += 4; break;
                        case OperandType.InlineSwitch:
                            int count = Integer(body, position); position += 4;
                            if (count < 0 || count > (body.Length - position) / 4) throw new BadImageFormatException("Invalid IL switch table.");
                            int end = checked(position + count * 4);
                            for (int i = 0; i < count; i++, position += 4) branches.Add(checked(end + Integer(body, position)));
                            break;
                        case OperandType.InlineMethod:
                            int token = Integer(body, position); position += 4;
                            MethodBase resolved = Resolve(method.Module, token, types, arguments);
                            LifeCallKind kind = opcode == OpCodes.Call ? LifeCallKind.Direct
                                : opcode == OpCodes.Callvirt ? LifeCallKind.Virtual
                                : opcode == OpCodes.Ldftn || opcode == OpCodes.Ldvirtftn ? LifeCallKind.Delegate : 0;
                            if (kind != 0 && resolved is MethodInfo target)
                            {
                                target = NativeIlGuards.Definition(target);
                                targets.TryGetValue(target, out LifeCallKind previous);
                                targets[target] = previous | kind;
                            }
                            break;
                        case OperandType.ShortInlineI:
                        case OperandType.ShortInlineVar: Require(body, position, 1); position++; break;
                        case OperandType.InlineVar: Require(body, position, 2); position += 2; break;
                        case OperandType.InlineI:
                        case OperandType.ShortInlineR:
                        case OperandType.InlineString:
                        case OperandType.InlineField:
                        case OperandType.InlineType:
                        case OperandType.InlineTok:
                        case OperandType.InlineSig: Require(body, position, 4); position += 4; break;
                        case OperandType.InlineI8:
                        case OperandType.InlineR: Require(body, position, 8); position += 8; break;
                        default: throw new BadImageFormatException("Unsupported IL operand.");
                    }
                    if (opcode == OpCodes.Calli) indirect++;
                }
                foreach (int destination in branches)
                    if ((uint)destination >= (uint)body.Length || (starts[destination >> 3] & (1 << (destination & 7))) == 0)
                        throw new BadImageFormatException("IL branch does not target an instruction.");
                return true;
            }
            catch (Exception error) when (error is BadImageFormatException || error is EndOfStreamException || error is OverflowException)
            {
                Limited = true; Failure ??= error;
                targets.Clear(); indirect = 0;
                return false;
            }
        }

        private static int Integer(byte[] body, int offset)
        { Require(body, offset, 4); return BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(offset, 4)); }
        private static void Require(byte[] body, int offset, int count)
        { if (offset < 0 || offset > body.Length - count) throw new EndOfStreamException(); }
        private static OpCode[] ScanOpcodes()
        {
            var result = new OpCode[512];
            foreach (OpCode opcode in LifeIlReader.Opcodes)
            {
                ushort value = unchecked((ushort)opcode.Value);
                result[value < 256 ? value : 256 + (value & 255)] = opcode;
            }
            return result;
        }
    }
}
