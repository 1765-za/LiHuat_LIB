using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;

namespace LihuatLib.Mechanisms
{
    internal static class IndependentIl
    {
        internal static Type ResultType(MethodBase method) => method is MethodInfo info ? info.ReturnType : typeof(void);
        internal static Type[] Parameters(MethodBase method) => (method.IsStatic ? Type.EmptyTypes
            : new[] { method.DeclaringType.IsValueType ? method.DeclaringType.MakeByRefType() : method.DeclaringType })
            .Concat(method.GetParameters().Select(p => p.ParameterType)).ToArray();

        internal static DynamicMethod New(MethodBase method) => new DynamicMethod(Guid.NewGuid().ToString("N"),
            ResultType(method), Parameters(method), method.Module, true);

        internal static DynamicMethod Clone(MethodBase method, Func<MethodInfo, bool, MethodInfo> remap = null,
            Func<ILGenerator, OpCode, object, bool> rewrite = null)
        {
            MethodBody body = NativeRuntime.SourceBody(method) ?? throw new InvalidOperationException("Missing metadata IL: " + method);
            DynamicMethod result = New(method);
            result.InitLocals = body.InitLocals;
            if (!PortableIlEmitter.TryEmit(method, body, CilRewrite.ReadSource(method, body), result.GetILGenerator(), out string reason, remap, rewrite))
                throw new InvalidOperationException("Independent IL emission failed: " + method + ": " + reason);
            return result;
        }

        internal static DynamicMethod Guard(MethodInfo original, MethodInfo continuation, MethodInfo gate, int[] arguments, int? tag = null,
            int? deniedArgument = null, MethodInfo deniedResult = null)
        {
            if (!gate.IsStatic || gate.ReturnType != typeof(void) && gate.ReturnType != typeof(bool))
                throw new ArgumentException("An entry gate must be static and return void or bool.", nameof(gate));
            Type[] signature = Parameters(original);
            if (deniedArgument.HasValue && (deniedArgument.Value < 0 || deniedArgument.Value >= signature.Length
                || original.ReturnType == typeof(void) || !original.ReturnType.IsAssignableFrom(signature[deniedArgument.Value])))
                throw new ArgumentException("Denied return argument does not match the entry return type.");
            if (deniedResult != null && (!tag.HasValue || deniedArgument.HasValue || !deniedResult.IsStatic
                || deniedResult.ReturnType != original.ReturnType
                || !deniedResult.GetParameters().Select(p => p.ParameterType).SequenceEqual(new[] { typeof(int) })))
                throw new ArgumentException("Denied result requires a tagged static return provider.");
            ParameterInfo[] inputs = gate.GetParameters();
            int offset = tag.HasValue ? 1 : 0;
            if (inputs.Length != arguments.Length + offset || offset != 0 && inputs[0].ParameterType != typeof(int))
                throw new ArgumentException("Gate argument count or tag type differs.");
            for (int i = 0; i < arguments.Length; i++)
                if (arguments[i] < 0 || arguments[i] >= signature.Length
                    || !inputs[i + offset].ParameterType.IsAssignableFrom(signature[arguments[i]])
                        && !Dereferences(signature[arguments[i]], inputs[i + offset].ParameterType))
                    throw new ArgumentException("Gate argument type differs at " + i);
            DynamicMethod wrapper = New(original);
            ILGenerator il = wrapper.GetILGenerator();
            if (tag.HasValue) il.Emit(OpCodes.Ldc_I4, tag.Value);
            for (int i = 0; i < arguments.Length; i++)
            {
                il.Emit(OpCodes.Ldarg, (short)arguments[i]);
                if (Dereferences(signature[arguments[i]], inputs[i + offset].ParameterType)) il.Emit(OpCodes.Ldind_Ref);
            }
            il.Emit(OpCodes.Call, GeneratedIntegrity.Create(gate));
            if (gate.ReturnType == typeof(bool))
            {
                Label allowed = il.DefineLabel();
                il.Emit(OpCodes.Brtrue, allowed);
                if (!deniedArgument.HasValue && (original.ReturnType.IsByRef || original.ReturnType.IsPointer || original.ReturnType.IsFunctionPointer))
                {
                    il.Emit(OpCodes.Ldstr, "Entry protection rejected access to a managed reference or native pointer.");
                    il.Emit(OpCodes.Newobj, typeof(UnauthorizedAccessException).GetConstructor(new[] { typeof(string) }));
                    il.Emit(OpCodes.Throw);
                }
                else
                {
                    ParameterInfo[] parameters = original.GetParameters();
                    for (int i = 0; i < parameters.Length; i++)
                        if (parameters[i].IsOut)
                        {
                            il.Emit(OpCodes.Ldarg, (short)(i + (original.IsStatic ? 0 : 1)));
                            il.Emit(OpCodes.Initobj, parameters[i].ParameterType.GetElementType());
                        }
                    if (original.ReturnType != typeof(void))
                    {
                        if (deniedArgument.HasValue) il.Emit(OpCodes.Ldarg, (short)deniedArgument.Value);
                        else if (deniedResult != null)
                        {
                            il.Emit(OpCodes.Ldc_I4, tag.Value);
                            il.Emit(OpCodes.Call, GeneratedIntegrity.Create(deniedResult));
                        }
                        else
                        {
                            LocalBuilder empty = il.DeclareLocal(original.ReturnType);
                            il.Emit(OpCodes.Ldloca, empty); il.Emit(OpCodes.Initobj, original.ReturnType); il.Emit(OpCodes.Ldloc, empty);
                        }
                    }
                    il.Emit(OpCodes.Ret);
                }
                il.MarkLabel(allowed);
            }
            for (int i = 0; i < signature.Length; i++) il.Emit(OpCodes.Ldarg, (short)i);
            il.Emit(OpCodes.Call, continuation);
            il.Emit(OpCodes.Ret);
            return wrapper;
        }

        private static bool Dereferences(Type source, Type destination) => source.IsByRef
            && !source.GetElementType().IsValueType && destination.IsAssignableFrom(source.GetElementType());

        internal static Delegate Compile(DynamicMethod method) => method.CreateDelegate(
            Expression.GetDelegateType(Parameters(method).Concat(new[] { method.ReturnType }).ToArray()));

        internal static DynamicMethod Filter(MethodInfo original, MethodInfo continuation, MethodInfo filter)
        {
            Type[] signature = Parameters(original);
            if (!filter.IsStatic || original.ReturnType == typeof(void) || filter.ReturnType != original.ReturnType
                || !filter.GetParameters().Select(p => p.ParameterType).SequenceEqual(signature.Concat(new[] { original.ReturnType })))
                throw new ArgumentException("A result filter must accept the full entry signature followed by its result.", nameof(filter));
            DynamicMethod wrapper = New(original);
            ILGenerator il = wrapper.GetILGenerator();
            LocalBuilder result = il.DeclareLocal(original.ReturnType);
            for (int i = 0; i < signature.Length; i++) il.Emit(OpCodes.Ldarg, (short)i);
            il.Emit(OpCodes.Call, continuation);
            il.Emit(OpCodes.Stloc, result);
            for (int i = 0; i < signature.Length; i++) il.Emit(OpCodes.Ldarg, (short)i);
            il.Emit(OpCodes.Ldloc, result);
            il.Emit(OpCodes.Call, GeneratedIntegrity.Create(filter));
            il.Emit(OpCodes.Ret);
            return wrapper;
        }
    }
}
