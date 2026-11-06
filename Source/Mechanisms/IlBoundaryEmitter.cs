using System;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace LihuatLib.Mechanisms
{
    internal static class IlBoundaryEmitter
    {
        private static ParameterInfo[] Validate(MethodInfo callback, Type result, Type[] signature, int[] arguments, int extra)
        {
            ParameterInfo[] parameters = callback.GetParameters();
            if (!callback.IsStatic || callback.ReturnType != result || parameters.Length != arguments.Length + extra)
                throw new ArgumentException("Boundary callback signature differs: " + callback);
            for (int i = 0; i < arguments.Length; i++)
            {
                int index = arguments[i];
                if (index < 0 || index >= signature.Length || !Accepts(signature[index], parameters[i].ParameterType))
                    throw new ArgumentException("Boundary argument differs at " + i + ": " + callback);
            }
            return parameters;
        }

        private static bool Accepts(Type source, Type destination) => source == destination
            || destination.IsByRef && destination.GetElementType() == source
            || !source.IsValueType && !source.IsByRef && !destination.IsByRef && destination.IsAssignableFrom(source);

        private static void Arguments(ILGenerator il, Type[] signature, int[] arguments, ParameterInfo[] parameters)
        {
            for (int i = 0; i < arguments.Length; i++)
            {
                int index = arguments[i];
                il.Emit(parameters[i].ParameterType.IsByRef && !signature[index].IsByRef ? OpCodes.Ldarga : OpCodes.Ldarg, (short)index);
            }
        }

        internal static DynamicMethod Filter(MethodInfo original, MethodInfo continuation, MethodInfo filter, int[] arguments)
        {
            Type[] signature = IndependentIl.Parameters(original);
            ParameterInfo[] parameters = Validate(filter, original.ReturnType, signature, arguments, 1);
            if (original.ReturnType == typeof(void) || parameters.Last().ParameterType != original.ReturnType)
                throw new ArgumentException("A result filter must end with the original result type.");
            DynamicMethod wrapper = IndependentIl.New(original);
            ILGenerator il = wrapper.GetILGenerator();
            LocalBuilder result = il.DeclareLocal(original.ReturnType);
            for (int i = 0; i < signature.Length; i++) il.Emit(OpCodes.Ldarg, (short)i);
            il.Emit(OpCodes.Call, continuation);
            il.Emit(OpCodes.Stloc, result);
            Arguments(il, signature, arguments, parameters);
            il.Emit(OpCodes.Ldloc, result);
            il.Emit(OpCodes.Call, GeneratedIntegrity.Create(filter));
            il.Emit(OpCodes.Ret);
            return wrapper;
        }

        internal static DynamicMethod Conditional(MethodInfo original, MethodInfo continuation, MethodInfo selected,
            MethodInfo applies, int[] arguments)
        {
            Type[] signature = IndependentIl.Parameters(original);
            ParameterInfo[] parameters = Validate(applies, typeof(bool), signature, arguments, 0);
            DynamicMethod wrapper = IndependentIl.New(original);
            ILGenerator il = wrapper.GetILGenerator();
            Label specialized = il.DefineLabel();
            Arguments(il, signature, arguments, parameters);
            il.Emit(OpCodes.Call, GeneratedIntegrity.Create(applies));
            il.Emit(OpCodes.Brtrue, specialized);
            for (int i = 0; i < signature.Length; i++) il.Emit(OpCodes.Ldarg, (short)i);
            il.Emit(OpCodes.Call, continuation);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(specialized);
            for (int i = 0; i < signature.Length; i++) il.Emit(OpCodes.Ldarg, (short)i);
            il.Emit(OpCodes.Call, selected);
            il.Emit(OpCodes.Ret);
            return wrapper;
        }

        internal static DynamicMethod Scope(MethodInfo original, MethodInfo continuation, MethodInfo begin, int[] beforeArguments,
            MethodInfo end, int[] afterArguments)
        {
            Type[] signature = IndependentIl.Parameters(original);
            bool hasResult = original.ReturnType != typeof(void);
            ParameterInfo[] before = Validate(begin, typeof(bool), signature, beforeArguments, hasResult ? 2 : 1);
            Type stateReference = before[beforeArguments.Length].ParameterType;
            if (!stateReference.IsByRef || hasResult && before.Last().ParameterType != original.ReturnType.MakeByRefType())
                throw new ArgumentException("A scoped gate must expose its state and optional result by reference.");
            Type stateType = stateReference.GetElementType();
            ParameterInfo[] after = Validate(end, typeof(void), signature, afterArguments, 1);
            if (after.Last().ParameterType != stateType) throw new ArgumentException("Scope cleanup state differs.");

            DynamicMethod wrapper = IndependentIl.New(original);
            wrapper.InitLocals = true;
            ILGenerator il = wrapper.GetILGenerator();
            LocalBuilder state = il.DeclareLocal(stateType);
            LocalBuilder result = hasResult ? il.DeclareLocal(original.ReturnType) : null;
            Label skipped = il.DefineLabel();
            il.BeginExceptionBlock();
            Arguments(il, signature, beforeArguments, before);
            il.Emit(OpCodes.Ldloca, state);
            if (hasResult) il.Emit(OpCodes.Ldloca, result);
            il.Emit(OpCodes.Call, GeneratedIntegrity.Create(begin));
            il.Emit(OpCodes.Brfalse, skipped);
            for (int i = 0; i < signature.Length; i++) il.Emit(OpCodes.Ldarg, (short)i);
            il.Emit(OpCodes.Call, continuation);
            if (hasResult) il.Emit(OpCodes.Stloc, result);
            il.MarkLabel(skipped);
            il.BeginFinallyBlock();
            Arguments(il, signature, afterArguments, after);
            il.Emit(OpCodes.Ldloc, state);
            il.Emit(OpCodes.Call, GeneratedIntegrity.Create(end));
            il.EndExceptionBlock();
            if (hasResult) il.Emit(OpCodes.Ldloc, result);
            il.Emit(OpCodes.Ret);
            return wrapper;
        }
    }
}
