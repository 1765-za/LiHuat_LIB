using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

namespace LihuatLib.Combat
{
    internal sealed class ManagedNativeReadPlan
    {
        private readonly Dictionary<(MethodInfo, int), int> exits = new();
        private readonly HashSet<FieldInfo> bridges = new();
        internal bool HasExits => exits.Count != 0;
        internal bool HasBridge => bridges.Count != 0;
        internal bool Ready => bridges.All(slot => slot.GetValue(null) is Delegate function && function.HasSingleTarget
            && function.Method.GetMethodBody() == null);
        internal void Exit(MethodInfo method, int instruction, int stackDepth) => exits[(method, instruction)] = stackDepth;
        internal bool Exits(MethodInfo method, int instruction, out int stackDepth) => exits.TryGetValue((method, instruction), out stackDepth);
        internal void Register(FieldInfo field) => bridges.Add(field);
        internal static bool Unmanaged(Type type) => typeof(Delegate).IsAssignableFrom(type)
            && type.GetCustomAttribute<UnmanagedFunctionPointerAttribute>() != null;

        internal static bool Bridge(Type type)
        {
            if (!Unmanaged(type)) return false;
            MethodInfo invoke = type.GetMethod("Invoke");
            if (invoke?.ReturnType != typeof(int) && invoke?.ReturnType != typeof(bool)) return false;
            var parameters = invoke.GetParameters();
            return parameters.Length is > 1 and <= 8 && parameters.Any(p => p.IsOut)
                && parameters.All(p => p.ParameterType.IsByRef
                    ? p.IsOut && p.ParameterType.GetElementType().IsPrimitive
                    : p.ParameterType.IsPrimitive);
        }

        internal bool Invoke(FieldInfo source, MethodInfo call, ManagedReadValue[] arguments, ManagedReadFrame frame)
        {
            if (source == null || !source.IsStatic || !Bridge(source.FieldType) || source.FieldType != call.DeclaringType) return false;
            var parameters = call.GetParameters();
            for (int i = 0; i < parameters.Length; i++)
                if (parameters[i].IsOut && (!arguments[i + 1].Address || !arguments[i + 1].Writable)) return false;
            for (int i = 0; i < parameters.Length; i++)
                if (parameters[i].IsOut) frame.Write(arguments[i + 1], ManagedReadValue.Scalar(parameters[i].ParameterType.GetElementType()));
            Register(source);
            return true;
        }
    }
}
