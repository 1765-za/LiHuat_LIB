using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using LihuatLib.Mechanisms;

namespace LihuatLib.Dispatching
{
    internal sealed class DispatchArguments
    {
        private readonly MethodBase original;
        private readonly ILGenerator il;
        private readonly ParameterInfo[] parameters;
        private readonly Dictionary<Type, LocalBuilder> states = new();
        internal readonly LocalBuilder Result, Exception, RunOriginal;
        private readonly LocalBuilder arguments;

        internal DispatchArguments(MethodBase original, ILGenerator il, IReadOnlyList<DispatchHandler> handlers)
        {
            this.original = original; this.il = il; parameters = original.GetParameters();
            Type result = IndependentIl.ResultType(original);
            if (result != typeof(void)) Result = il.DeclareLocal(result);
            if (handlers.Any(h => h.Phase == DispatchPhase.Finalizer)) Exception = il.DeclareLocal(typeof(Exception));
            if (handlers.Any(h => h.Phase == DispatchPhase.Prefix && h.Method.ReturnType == typeof(bool)))
            { RunOriginal = il.DeclareLocal(typeof(bool)); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Stloc, RunOriginal); }
            if (handlers.Any(h => h.Method.GetParameters().Any(p => p.Name == "__args"))) arguments = il.DeclareLocal(typeof(object[]));
            foreach (DispatchHandler handler in handlers)
                foreach (ParameterInfo parameter in handler.Method.GetParameters())
                    if (parameter.Name == "__state")
                    {
                        Type type = Element(parameter.ParameterType);
                        if (states.TryGetValue(handler.StateOwner, out LocalBuilder existing))
                        { if (existing.LocalType != type) throw new ArgumentException("Dispatcher state types disagree: " + handler.Method); }
                        else states.Add(handler.StateOwner, il.DeclareLocal(type));
                    }
            for (int i = 0; i < parameters.Length; i++)
                if (parameters[i].IsOut)
                { il.Emit(OpCodes.Ldarg, (short)(i + Offset)); il.Emit(OpCodes.Initobj, Element(parameters[i].ParameterType)); }
        }

        private int Offset => original.IsStatic ? 0 : 1;
        private static Type Element(Type type) => type.IsByRef ? type.GetElementType() : type;

        internal void Call(DispatchHandler handler)
        {
            bool array = handler.Method.GetParameters().Any(p => p.Name == "__args");
            if (array) PackArguments();
            foreach (ParameterInfo parameter in handler.Method.GetParameters()) Load(handler, parameter);
            il.Emit(OpCodes.Call, handler.Method);
            if (array) UnpackArguments();
        }

        private void Load(DispatchHandler handler, ParameterInfo parameter)
        {
            string name = parameter.Name;
            Type requested = parameter.ParameterType;
            switch (name)
            {
                case "__instance":
                    if (original.IsStatic) throw new ArgumentException("Static target has no instance: " + original);
                    LoadArgument(0, IndependentIl.Parameters(original)[0], requested); return;
                case "__result": LoadLocal(Result, requested); return;
                case "__state": LoadLocal(states[handler.StateOwner], requested); return;
                case "__exception": LoadLocal(Exception, requested); return;
                case "__runOriginal": LoadLocal(RunOriginal, requested); return;
                case "__args": LoadLocal(arguments, requested); return;
                case "__originalMethod":
                    if (original is ConstructorInfo constructor) il.Emit(OpCodes.Ldtoken, constructor);
                    else il.Emit(OpCodes.Ldtoken, (MethodInfo)original);
                    il.Emit(OpCodes.Ldtoken, original.DeclaringType);
                    il.Emit(OpCodes.Call, typeof(MethodBase).GetMethod(nameof(MethodBase.GetMethodFromHandle), new[] { typeof(RuntimeMethodHandle), typeof(RuntimeTypeHandle) }));
                    ConvertValue(typeof(MethodBase), requested); return;
            }
            if (name.StartsWith("___", StringComparison.Ordinal))
            {
                FieldInfo field = AccessTools.Field(original.DeclaringType, name.Substring(3))
                    ?? throw new MissingFieldException(original.DeclaringType.FullName, name.Substring(3));
                if (!field.IsStatic) il.Emit(OpCodes.Ldarg_0);
                if (requested.IsByRef)
                {
                    if (field.FieldType != requested.GetElementType()) throw new ArgumentException("Reference field type differs: " + parameter);
                    il.Emit(field.IsStatic ? OpCodes.Ldsflda : OpCodes.Ldflda, field);
                }
                else { il.Emit(field.IsStatic ? OpCodes.Ldsfld : OpCodes.Ldfld, field); ConvertValue(field.FieldType, requested); }
                return;
            }
            int index = name.StartsWith("__", StringComparison.Ordinal) && int.TryParse(name.Substring(2), out int position)
                ? position : Array.FindIndex(parameters, p => p.Name == name);
            if (index < 0 || index >= parameters.Length) throw new ArgumentException("Unknown target argument " + parameter + " for " + original);
            LoadArgument(index + Offset, parameters[index].ParameterType, requested);
        }

        private void LoadArgument(int index, Type supplied, Type requested)
        {
            if (requested.IsByRef)
            {
                if (Element(supplied) != requested.GetElementType()) throw new ArgumentException("Reference argument type differs: " + original);
                il.Emit(supplied.IsByRef ? OpCodes.Ldarg : OpCodes.Ldarga, (short)index);
            }
            else
            {
                il.Emit(OpCodes.Ldarg, (short)index);
                if (supplied.IsByRef) { supplied = supplied.GetElementType(); il.Emit(OpCodes.Ldobj, supplied); }
                ConvertValue(supplied, requested);
            }
        }

        private void LoadLocal(LocalBuilder local, Type requested)
        {
            if (local == null) throw new ArgumentException("Callback requested unavailable dispatch storage.");
            if (requested.IsByRef)
            {
                if (local.LocalType != requested.GetElementType()) throw new ArgumentException("Reference storage type differs.");
                il.Emit(OpCodes.Ldloca, local);
            }
            else { il.Emit(OpCodes.Ldloc, local); ConvertValue(local.LocalType, requested); }
        }

        private void ConvertValue(Type supplied, Type requested)
        {
            if (supplied == requested) return;
            if (requested.IsByRef || supplied.IsPointer || requested.IsPointer || requested.IsValueType)
                throw new ArgumentException("Dispatch value type differs: " + supplied + " -> " + requested);
            if (supplied.IsValueType || supplied.IsGenericParameter)
            {
                if (!requested.IsAssignableFrom(supplied)) throw new ArgumentException("Cannot box dispatch argument as " + requested);
                il.Emit(OpCodes.Box, supplied);
            }
            else if (!requested.IsAssignableFrom(supplied)) il.Emit(OpCodes.Castclass, requested);
        }

        private void PackArguments()
        {
            il.Emit(OpCodes.Ldc_I4, parameters.Length); il.Emit(OpCodes.Newarr, typeof(object)); il.Emit(OpCodes.Stloc, arguments);
            for (int i = 0; i < parameters.Length; i++)
            {
                il.Emit(OpCodes.Ldloc, arguments); il.Emit(OpCodes.Ldc_I4, i);
                LoadArgument(i + Offset, parameters[i].ParameterType, typeof(object)); il.Emit(OpCodes.Stelem_Ref);
            }
        }

        private void UnpackArguments()
        {
            for (int i = 0; i < parameters.Length; i++)
            {
                Type type = Element(parameters[i].ParameterType);
                if (parameters[i].ParameterType.IsByRef) il.Emit(OpCodes.Ldarg, (short)(i + Offset));
                il.Emit(OpCodes.Ldloc, arguments); il.Emit(OpCodes.Ldc_I4, i); il.Emit(OpCodes.Ldelem_Ref);
                il.Emit(type.IsValueType || type.IsGenericParameter ? OpCodes.Unbox_Any : OpCodes.Castclass, type);
                if (parameters[i].ParameterType.IsByRef) il.Emit(OpCodes.Stobj, type);
                else il.Emit(OpCodes.Starg, (short)(i + Offset));
            }
        }
    }
}
