using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;

namespace LihuatLib.Mechanisms
{
    internal static class DynamicIlCatalogue
    {
        private sealed class Body
        {
            internal readonly DynamicILInfo Info;
            internal readonly Dictionary<int, MemberInfo> Tokens = new();
            internal byte[] Analyzed;
            internal int TokenCount;
            internal Body(DynamicILInfo info) { Info = info; }
        }
        private static readonly ConditionalWeakTable<DynamicMethod, Body> bodies = new();
        private static readonly FieldInfo scope = AccessTools.Field(typeof(DynamicILInfo), "m_scope");
        internal static List<CodeInstruction> Read(DynamicMethod method)
        {
            if (!bodies.TryGetValue(method, out Body body)) return null;
            lock (body.Tokens) return LifeIlReader.Decode(Code(body.Info), (_, token) => body.Tokens.TryGetValue(token, out MemberInfo member)
                ? member : FieldValueReader.ResolveToken(scope.GetValue(body.Info), token));
        }
        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "m_code")]
        private static extern ref byte[] Code(DynamicILInfo info);

        internal static void Install()
        {
            NativeIlGuards.Scoped(AccessTools.Method(typeof(DynamicILInfo), nameof(DynamicILInfo.SetCode), new[] { typeof(byte[]), typeof(int) }),
                AccessTools.Method(typeof(DynamicIlCatalogue), nameof(BeforeCode)), new[] { 0, 1 },
                AccessTools.Method(typeof(DynamicIlCatalogue), nameof(AfterCode)), Array.Empty<int>());
            MethodInfo pointerCode = AccessTools.Method(typeof(DynamicILInfo), nameof(DynamicILInfo.SetCode),
                new[] { typeof(byte).MakePointerType(), typeof(int), typeof(int) });
            NativeIlGuards.RegisterTransform(pointerCode, typeof(DynamicIlCatalogue).FullName, NativeIlGuards.ResultOrder,
                body => PointerCode(pointerCode, body));
            NativeIlGuards.FilterArguments(AccessTools.Method(typeof(DynamicMethod), nameof(DynamicMethod.GetDynamicILInfo)),
                AccessTools.Method(typeof(DynamicIlCatalogue), nameof(Capture)), 0);
            Token(new[] { typeof(RuntimeFieldHandle) }, nameof(Field));
            Token(new[] { typeof(RuntimeFieldHandle), typeof(RuntimeTypeHandle) }, nameof(GenericField));
            Token(new[] { typeof(RuntimeMethodHandle) }, nameof(Method));
            Token(new[] { typeof(RuntimeMethodHandle), typeof(RuntimeTypeHandle) }, nameof(GenericMethod));
            Token(new[] { typeof(DynamicMethod) }, nameof(DynamicMethodToken));
        }
        private static bool BeforeCode(DynamicILInfo info, ref byte[] code, out bool state)
        {
            state = false;
            if (!bodies.TryGetValue(info.DynamicMethod, out Body body)) return true;
            code = Rewrite(info, body, code);
            return true;
        }
        private static byte[] Rewrite(DynamicILInfo info, Body body, byte[] code)
        {
            lock (body.Tokens)
            {
                code = DynamicRootMutation.Rewrite(code, token => body.Tokens.TryGetValue(token, out MemberInfo member) ? member : null,
                    setter => info.GetTokenFor(setter), DynamicWriteCatalogue.AddressResult(info.DynamicMethod.ReturnType));
                code = DynamicLifecycleCalls.Rewrite(code, token => body.Tokens.TryGetValue(token, out MemberInfo member) ? member : null,
                    guarded => info.GetTokenFor(guarded));
            }
            return code;
        }
        private static void AfterCode(bool state) { }
        private static DynamicMethod PointerCode(MethodInfo original, MethodInfo continuation)
        {
            DynamicMethod method = IndependentIl.New(original);
            ILGenerator il = method.GetILGenerator();
            for (short i = 0; i < 4; i++) il.Emit(OpCodes.Ldarg, i);
            il.Emit(OpCodes.Call, continuation);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, GeneratedIntegrity.Create(AccessTools.Method(typeof(DynamicIlCatalogue), nameof(PublishCode))));
            il.Emit(OpCodes.Ret);
            return method;
        }
        private static void PublishCode(DynamicILInfo info)
        {
            if (!DynamicWriteCatalogue.Ready || DynamicWriteCatalogue.Building || !bodies.TryGetValue(info.DynamicMethod, out Body body)) return;
            bool previous = DynamicWriteCatalogue.Building;
            DynamicWriteCatalogue.Building = true;
            try { Code(info) = Rewrite(info, body, Code(info)); }
            finally { DynamicWriteCatalogue.Building = previous; }
        }
        private static void Token(Type[] signature, string filter)
        {
            int[] arguments = signature.Length == 1 ? new[] { 0, 1 } : new[] { 0, 1, 2 };
            NativeIlGuards.FilterArguments(AccessTools.Method(typeof(DynamicILInfo), nameof(DynamicILInfo.GetTokenFor), signature),
                AccessTools.Method(typeof(DynamicIlCatalogue), filter), arguments);
        }
        private static DynamicILInfo Capture(DynamicMethod method, DynamicILInfo result)
        {
            if (DynamicWriteCatalogue.Ready && !DynamicWriteCatalogue.Building && !DynamicWriteCatalogue.Register(method).Trusted)
                bodies.GetValue(method, _ => new Body(result));
            return result;
        }
        private static int Remember(DynamicILInfo info, int token, Func<MemberInfo> resolve)
        {
            if (bodies.TryGetValue(info.DynamicMethod, out Body body))
                lock (body.Tokens) body.Tokens[token] = resolve();
            return token;
        }
        private static int Field(DynamicILInfo info, RuntimeFieldHandle field, int result) => Remember(info, result, () => FieldInfo.GetFieldFromHandle(field));
        private static int GenericField(DynamicILInfo info, RuntimeFieldHandle field, RuntimeTypeHandle owner, int result) => Remember(info, result, () => FieldInfo.GetFieldFromHandle(field, owner));
        private static int Method(DynamicILInfo info, RuntimeMethodHandle method, int result) => Remember(info, result, () => MethodBase.GetMethodFromHandle(method));
        private static int GenericMethod(DynamicILInfo info, RuntimeMethodHandle method, RuntimeTypeHandle owner, int result) => Remember(info, result, () => MethodBase.GetMethodFromHandle(method, owner));
        private static int DynamicMethodToken(DynamicILInfo info, DynamicMethod method, int result) => Remember(info, result, () => method);

        internal static void Analyze(DynamicMethod method)
        {
            if (!bodies.TryGetValue(method, out Body body) || !DynamicWriteCatalogue.TryGet(method, out var entry)) return;
            lock (body.Tokens)
            {
                byte[] code = Code(body.Info);
                if (body.Analyzed != null && code.AsSpan().SequenceEqual(body.Analyzed) && body.TokenCount == body.Tokens.Count) return;
                List<CodeInstruction> instructions = LifeIlReader.Decode(code, (_, token) => body.Tokens.TryGetValue(token, out MemberInfo member) ? member : null);
                if (instructions == null) throw new BadImageFormatException("Dynamic method body could not be decoded.");
                lock (entry.Fields)
                {
                    for (int i = 0; i < instructions.Count; i++)
                    {
                        CodeInstruction line = instructions[i];
                        if ((line.opcode == OpCodes.Newobj || line.opcode == OpCodes.Call || line.opcode == OpCodes.Callvirt)
                            && line.operand is MethodBase authority && RuntimeAuthorityCalls.Changes(authority)) entry.ChangesAuthority = true;
                        if ((line.opcode == OpCodes.Stfld || line.opcode == OpCodes.Stsfld
                            || method.ReturnType.IsByRef && (line.opcode == OpCodes.Ldflda || line.opcode == OpCodes.Ldsflda))
                            && line.operand is FieldInfo field && GeneratedMethodCatalogue.Sensitive(field)) entry.Fields.Add(field);
                        if ((line.opcode == OpCodes.Call || line.opcode == OpCodes.Callvirt) && line.operand is MethodInfo call)
                        {
                            MethodInfo source = RuntimePublicationCalls.SourceAt(instructions, i, call) ?? call;
                            entry.Destructive |= ReverseLifecycle.Destructive(source)
                                && DynamicCallReceivers.CanAffectPawn(method, instructions, i, source);
                            if (call is DynamicMethod dependency) entry.Calls.Add(dependency);
                        }
                    }
                    entry.Destructive |= IndirectFieldMutation.Writes(instructions, DynamicWriteCatalogue.Vital);
                }
                body.Analyzed = (byte[])code.Clone(); body.TokenCount = body.Tokens.Count;
            }
        }
    }
}
