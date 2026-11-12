using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;

namespace LihuatLib.Mechanisms
{
    public sealed class LifeIlReader
    {
        private static readonly Dictionary<short, OpCode> opcodes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(OpCode)).Select(f => (OpCode)f.GetValue(null)).ToDictionary(op => op.Value);
        private static readonly ManagedIlImageCache sharedImages = new ManagedIlImageCache();
        internal static IEnumerable<OpCode> Opcodes => opcodes.Values;
        private readonly Dictionary<Module, ManagedIlImage> images = new Dictionary<Module, ManagedIlImage>();
        private readonly bool observeManaged;
        private readonly Func<MethodInfo, byte[]> snapshot;
        private readonly Func<MethodInfo, Type[]> localTypes;
        private readonly Func<MethodInfo, List<CodeInstruction>> sharedCode;
        private readonly Dictionary<MethodInfo, List<CodeInstruction>> decoded;
        internal readonly Dictionary<MethodInfo, string> Unavailable = new Dictionary<MethodInfo, string>();
        private HashSet<MethodInfo> trace;

        public LifeIlReader() { }
        internal LifeIlReader(bool observeManaged) { this.observeManaged = observeManaged; }
        internal LifeIlReader(Func<MethodInfo, byte[]> snapshot, Func<MethodInfo, Type[]> localTypes)
        {
            this.snapshot = snapshot; this.localTypes = localTypes;
            decoded = new Dictionary<MethodInfo, List<CodeInstruction>>();
        }
        internal LifeIlReader(Func<MethodInfo, byte[]> snapshot, Func<MethodInfo, Type[]> localTypes,
            Func<MethodInfo, List<CodeInstruction>> sharedCode)
        { this.snapshot = snapshot; this.localTypes = localTypes; this.sharedCode = sharedCode; }

        internal static bool HasMetadataBody(MethodInfo method)
        {
            if (method == null || method.IsAbstract
                || method.DeclaringType?.IsArray == true || method.Module.Assembly.IsDynamic
                || (method.Attributes & MethodAttributes.PinvokeImpl) != 0) return false;
            int token = method.MetadataToken;
            MethodImplAttributes implementation = method.GetMethodImplementationFlags();
            return (token & unchecked((int)0xff000000)) == 0x06000000 && (token & 0x00ffffff) != 0
                && (implementation & (MethodImplAttributes.CodeTypeMask | MethodImplAttributes.InternalCall)) == MethodImplAttributes.IL;
        }

        internal Type[] Locals(MethodInfo method) => !HasMetadataBody(method) ? Type.EmptyTypes
            : localTypes != null ? localTypes(method)
            : NativeRuntime.SourceBody(method)?.LocalVariables.Select(v => v.LocalType).ToArray() ?? Type.EmptyTypes;

        internal IDisposable Trace(HashSet<MethodInfo> methods) => new ReadTrace(this, methods);
        private sealed class ReadTrace : IDisposable
        {
            private readonly LifeIlReader reader;
            private readonly HashSet<MethodInfo> previous;
            public ReadTrace(LifeIlReader reader, HashSet<MethodInfo> methods)
            { this.reader = reader; previous = reader.trace; reader.trace = methods; }
            public void Dispose() => reader.trace = previous;
        }

        public List<CodeInstruction> Read(MethodInfo method)
        {
            method = MethodIdentity.Declaration(method);
            trace?.Add(method);
            if (!HasMetadataBody(method)) return null;
            if (decoded != null && decoded.TryGetValue(method, out List<CodeInstruction> cached)) return cached;
            List<CodeInstruction> result;
            try
            {
                result = sharedCode != null ? sharedCode(method) : Decode(method, snapshot != null ? snapshot(method) : ReadBody(method));
            }
            catch (Exception error) when (snapshot != null && (LifeDiscoveryMetadata.ResolutionFailure(error)
                || error is EndOfStreamException))
            {
                Unavailable[method] = error.GetType().Name + ": " + error.Message;
                result = null;
            }
            // Snapshot readers belong to one immutable analysis round; live readers must always read again.
            if (decoded != null) decoded.Add(method, result);
            return result;
        }
        internal byte[] ReadBody(MethodInfo method)
        {
            method = MethodIdentity.Declaration(method);
            if (!HasMetadataBody(method)) return null;
            if (observeManaged)
            {
                byte[] managed = LifeMethodRevisions.Read(method, null);
                if (managed != null) return managed;
            }
            Module module = method.Module;
            if (!images.TryGetValue(module, out ManagedIlImage image))
            {
                images[module] = null;
                string path = module.FullyQualifiedName;
                if (File.Exists(path))
                {
                    image = sharedImages.Get(module, path);
                    if (image.ModuleId != module.ModuleVersionId) return null;
                    images[module] = image;
                }
            }
            byte[] body = image != null ? image.Body(method.MetadataToken) : NativeRuntime.SourceBody(method)?.GetILAsByteArray();
            return body;
        }
        internal static List<CodeInstruction> Decode(MethodBase method, byte[] body)
        {
            Module module = method.Module;
            Type[] typeArgs = method.DeclaringType?.IsGenericType == true ? method.DeclaringType.GetGenericArguments() : null;
            Type[] methodArgs = method.IsGenericMethod ? method.GetGenericArguments() : null;
            return Decode(body, (kind, token) => Resolve(module, kind, token, typeArgs, methodArgs));
        }
        private static object Resolve(Module module, OperandType kind, int token, Type[] typeArgs, Type[] methodArgs)
        {
            try { return kind switch
            {
                OperandType.InlineString => module.ResolveString(token),
                OperandType.InlineField => module.ResolveField(token, typeArgs, methodArgs),
                OperandType.InlineMethod => module.ResolveMethod(token, typeArgs, methodArgs),
                OperandType.InlineType => module.ResolveType(token, typeArgs, methodArgs),
                OperandType.InlineSig => module.ResolveSignature(token),
                _ => module.ResolveMember(token, typeArgs, methodArgs)
            }; }
            catch (ArgumentException error)
            {
                throw new BadImageFormatException("Cannot resolve " + kind + " token 0x" + token.ToString("X8")
                    + " in " + module.Name + ".", error);
            }
        }
        internal static List<CodeInstruction> Decode(byte[] body, Func<OperandType, int, object> resolve)
        {
            if (body == null) return null;
            var lines = new List<CodeInstruction>();
            var offsets = new Dictionary<int, CodeInstruction>();
            int cursor = 0;
            using (var stream = new MemoryStream(body, false))
            using (var reader = new BinaryReader(stream))
            {
                while (cursor < body.Length)
                {
                    int offset = cursor;
                    byte first = reader.ReadByte();
                    short code = first == 0xfe ? unchecked((short)(0xfe00 | reader.ReadByte())) : first;
                    if (!opcodes.TryGetValue(code, out OpCode op)) return null;
                    object operand = null;
                    switch (op.OperandType)
                    {
                        case OperandType.InlineNone: break;
                        case OperandType.ShortInlineI: operand = reader.ReadSByte(); break;
                        case OperandType.InlineI: operand = reader.ReadInt32(); break;
                        case OperandType.InlineI8: operand = reader.ReadInt64(); break;
                        case OperandType.ShortInlineR: operand = reader.ReadSingle(); break;
                        case OperandType.InlineR: operand = reader.ReadDouble(); break;
                        case OperandType.ShortInlineVar: operand = (int)reader.ReadByte(); break;
                        case OperandType.InlineVar: operand = (int)reader.ReadUInt16(); break;
                        case OperandType.ShortInlineBrTarget: { int delta = reader.ReadSByte(); operand = checked((int)stream.Position + delta); break; }
                        case OperandType.InlineBrTarget: { int delta = reader.ReadInt32(); operand = checked((int)stream.Position + delta); break; }
                        case OperandType.InlineSwitch:
                            int count = reader.ReadInt32();
                            if (count < 0 || count > (stream.Length - stream.Position) / 4) return null;
                            int end = checked((int)stream.Position + count * 4);
                            var branches = new int[count];
                            for (int i = 0; i < count; i++) branches[i] = checked(end + reader.ReadInt32());
                            operand = branches; break;
                        case OperandType.InlineString:
                        case OperandType.InlineField:
                        case OperandType.InlineMethod:
                        case OperandType.InlineType:
                        case OperandType.InlineSig:
                        case OperandType.InlineTok: operand = resolve(op.OperandType, reader.ReadInt32()); break;
                        default: return null;
                    }
                    var line = new CodeInstruction(op, operand);
                    lines.Add(line); offsets.Add(offset, line); cursor = checked((int)stream.Position);
                }
            }
            Dictionary<int, Label> byOffset = null;
            foreach (CodeInstruction line in lines)
            {
                bool branch = line.opcode.OperandType == OperandType.InlineBrTarget || line.opcode.OperandType == OperandType.ShortInlineBrTarget;
                if (!branch && !(line.operand is int[])) continue;
                byOffset ??= new Dictionary<int, Label>();
                if (branch)
                    line.operand = LabelFor((int)line.operand, offsets, byOffset);
                else if (line.operand is int[] targets)
                {
                    var branches = new Label[targets.Length];
                    for (int i = 0; i < targets.Length; i++) branches[i] = LabelFor(targets[i], offsets, byOffset);
                    line.operand = branches;
                }
            }
            return lines;
        }
        private static Label LabelFor(int offset, Dictionary<int, CodeInstruction> offsets, Dictionary<int, Label> labels)
        {
            if (!offsets.TryGetValue(offset, out CodeInstruction line)) throw new BadImageFormatException();
            // These labels identify destinations within one analysis graph and are never emitted.
            if (!labels.TryGetValue(offset, out Label label))
            { labels[offset] = label = Unsafe.BitCast<int, Label>(labels.Count); line.labels.Add(label); }
            return label;
        }
    }
}
