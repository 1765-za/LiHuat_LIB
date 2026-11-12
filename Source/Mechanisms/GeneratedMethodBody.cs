using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace LihuatLib.Mechanisms
{
    internal static class GeneratedMethodBody
    {
        private sealed class Body
        {
            internal readonly DynamicMethod Method;
            internal Body(MethodInfo source)
            {
                bool previous = DynamicWriteCatalogue.Building;
                DynamicWriteCatalogue.Building = true;
                try { Method = Clone(source, source.Module.Assembly, new Dictionary<MethodInfo, DynamicMethod>()); }
                finally { DynamicWriteCatalogue.Building = previous; }
            }
        }
        private static readonly ConditionalWeakTable<MethodInfo, Body> bodies = new();
        internal static DynamicMethod For(MethodInfo source) => bodies.GetValue(MethodIdentity.Declaration(source), method => new Body(method)).Method;
        internal static void Invalidate() => bodies.Clear();

        private static DynamicMethod Clone(MethodInfo source, Assembly assembly, Dictionary<MethodInfo, DynamicMethod> methods)
        {
            source = MethodIdentity.Declaration(source);
            if (methods.TryGetValue(source, out var existing)) return existing;
            MethodBody body = NativeRuntime.SourceBody(source);
            if (body == null && UnsafeFieldMetadata.Resolve(source) is FieldInfo field)
            {
                DynamicMethod accessor = IndependentIl.New(source);
                ILGenerator il = accessor.GetILGenerator();
                if (!field.IsStatic) il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Call, GeneratedFieldWrites.Reference(field));
                il.Emit(OpCodes.Ret);
                methods.Add(source, accessor);
                return accessor;
            }
            if (body == null) throw new InvalidOperationException("Missing generated body: " + source);
            DynamicMethod result = IndependentIl.New(source);
            result.InitLocals = body.InitLocals;
            int offset = source.IsStatic ? 0 : 1;
            ParameterInfo[] parameters = source.GetParameters();
            for (int i = 0; i < parameters.Length; i++) result.DefineParameter(i + offset + 1, parameters[i].Attributes, parameters[i].Name);
            methods.Add(source, result);
            if (!PortableIlEmitter.TryEmit(source, body, CilRewrite.ReadSource(source, body), result.GetILGenerator(), out string reason,
                (called, virtualCall) => !virtualCall && called.Module.Assembly == assembly && !called.IsAbstract
                    && !called.ContainsGenericParameters && (called.GetMethodBody() != null || UnsafeFieldMetadata.Resolve(called) != null)
                        ? Clone(called, assembly, methods) : called,
                GeneratedFieldWrites.Rewrite)) throw new InvalidOperationException("Generated body protection failed: " + reason);
            return result;
        }
    }
}
