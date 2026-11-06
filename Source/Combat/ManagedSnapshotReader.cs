using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using LihuatLib.Mechanisms;

namespace LihuatLib.Combat
{
    internal sealed class ManagedSnapshotReader
    {
        private readonly ManagedCombatIl il;
        private readonly HashSet<FieldInfo> delegates = new();
        internal readonly HashSet<MethodInfo> Methods = new();
        internal readonly HashSet<FieldInfo> Fields = new();
        internal readonly ManagedNativeReadPlan Native = new();
        private int readiness;
        internal ManagedSnapshotReader(ManagedCombatIl il) { this.il = il; }

        internal bool Ready
        {
            get
            {
                if (readiness != 0) return readiness > 0 && Native.Ready;
                var inspected = new HashSet<FieldInfo>();
                while (inspected.Count < delegates.Count)
                {
                    if (delegates.Count > 8) { readiness = -1; return false; }
                    FieldInfo source = delegates.First(slot => !inspected.Contains(slot));
                    if (!ReadDelegate(source, out Delegate function)) return false;
                    if (!PureDelegate(function)) { readiness = -1; return false; }
                    inspected.Add(source);
                }
                readiness = 1; return Native.Ready;
            }
        }

        internal string Failure { get; private set; }
        internal bool Inspect(MethodInfo method)
        {
            var analysis = new ManagedReadAnalysis(il, Fields, source => delegates.Add(source), Native);
            bool valid = analysis.Inspect(method);
            if (valid) Methods.UnionWith(analysis.Methods);
            Failure = valid ? null : analysis.Failure;
            return valid;
        }
        internal static bool SnapshotConstructor(ConstructorInfo ctor)
        {
            Type type = ctor.DeclaringType;
            if (!type.IsValueType || type.IsPrimitive || type.GetFields(ManagedCombatIl.Members).Any(f => !f.IsInitOnly || !f.FieldType.IsPrimitive && !f.FieldType.IsEnum)) return false;
            var body = LifeIlReader.Decode(ctor.GetMethodBody()?.GetILAsByteArray(), (kind, token) => ctor.Module.ResolveMember(token));
            if (body == null || body.Count > 64) return false;
            for (int i = 0; i < body.Count; i++)
            {
                var line = body[i];
                if (line.opcode == OpCodes.Stfld && line.operand is FieldInfo field && field.DeclaringType == type && field.IsInitOnly)
                {
                    int at = i - 1;
                    _ = ManagedValueExpression.Before(body, ref at);
                    if (ManagedValueExpression.Before(body, ref at)?.IsThis != true) return false;
                }
                else if (!Arithmetic(line.opcode)) return false;
            }
            return true;
        }

        private bool ReadDelegate(FieldInfo field, out Delegate function)
        {
            function = null;
            if (!field.IsStatic || !field.IsInitOnly) return false;
            Type type = field.FieldType;
            bool lazy = type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Lazy<>) && typeof(Delegate).IsAssignableFrom(type.GenericTypeArguments[0]);
            if (!lazy && !typeof(Delegate).IsAssignableFrom(type)) return false;
            object value = field.GetValue(null);
            if (lazy)
            {
                if (value == null) return false;
                if (!(bool)type.GetProperty(nameof(Lazy<object>.IsValueCreated)).GetValue(value))
                {
                    var factory = type.GetField("_factory", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(value) as Delegate;
                    if (factory?.Target != null || factory == null || !new ManagedReadAnalysis(il, new HashSet<FieldInfo>(), _ => { }, true).Inspect(factory.Method)) return false;
                }
                value = type.GetProperty(nameof(Lazy<object>.Value)).GetValue(value);
            }
            function = value as Delegate;
            return function != null && function.Target == null && function.GetInvocationList().Length == 1;
        }

        private bool PureDelegate(Delegate function)
        {
            MethodInfo method = function.Method;
            if (!method.ReturnType.IsPrimitive || method.GetParameters().Any(p => !p.ParameterType.IsPrimitive)) return false;
            if (method is not DynamicMethod dynamic) return method.IsStatic
                && new ManagedReadAnalysis(il, Fields, source => delegates.Add(source), false, false).Inspect(method);
            // A completed DynamicMethod has immutable code in its resolver; token-free arithmetic cannot touch game state.
            object resolver = typeof(DynamicMethod).GetField("_resolver", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(dynamic);
            byte[] bytes = resolver == null ? null : (byte[])resolver.GetType().GetField("m_code", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(resolver);
            if (bytes == null || bytes.Length > 2048) return false;
            var body = LifeIlReader.Decode(bytes, (_, _) => null);
            return body?.Count > 0 && body.Count <= 256 && body.All(line => Arithmetic(line.opcode));
        }

        private static bool Arithmetic(OpCode op) => op == OpCodes.Nop || op == OpCodes.Ret || op == OpCodes.Pop || op == OpCodes.Dup
            || op.Name.StartsWith("ldarg", StringComparison.Ordinal) && !op.Name.StartsWith("ldarga", StringComparison.Ordinal)
            || op.Name.StartsWith("ldloc", StringComparison.Ordinal) && !op.Name.StartsWith("ldloca", StringComparison.Ordinal)
            || op.Name.StartsWith("stloc", StringComparison.Ordinal) || op.Name.StartsWith("ldc.", StringComparison.Ordinal)
            || op.Name.StartsWith("conv.", StringComparison.Ordinal)
            || op == OpCodes.Add || op == OpCodes.Sub || op == OpCodes.Mul || op == OpCodes.Div || op == OpCodes.Div_Un
            || op == OpCodes.Rem || op == OpCodes.Rem_Un || op == OpCodes.And || op == OpCodes.Or || op == OpCodes.Xor
            || op == OpCodes.Shl || op == OpCodes.Shr || op == OpCodes.Shr_Un || op == OpCodes.Neg || op == OpCodes.Not
            || op == OpCodes.Ceq || op == OpCodes.Cgt || op == OpCodes.Cgt_Un || op == OpCodes.Clt || op == OpCodes.Clt_Un;
    }
}
