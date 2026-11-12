using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace LihuatLib.Mechanisms
{
    internal static class ReferenceArgumentMutation
    {
        private sealed class Arguments { internal readonly Dictionary<int, bool> Writes = new(); }
        private static readonly ConditionalWeakTable<MethodInfo, Arguments> methods = new();
        internal static void Invalidate() => methods.Clear();
        internal static bool Writes(MethodInfo method, int argument)
            => Analyze(method, argument, new HashSet<(MethodInfo, int)>());

        private static bool Analyze(MethodInfo method, int argument, HashSet<(MethodInfo, int)> path)
        {
            Arguments result = methods.GetValue(method, _ => new Arguments());
            lock (result.Writes) if (result.Writes.TryGetValue(argument, out bool known)) return known;
            if (path.Count >= 64 || !path.Add((method, argument))) return true;
            bool writes;
            try
            {
                if (UnsafeFieldMetadata.Resolve(method) != null) writes = false;
                else
                {
                    var code = method.IsVirtual && !method.IsFinal ? null : MethodInstructionSource.Read(method);
                    if (code == null)
                    {
                        int parameter = argument - (method.IsStatic ? 0 : 1);
                        ParameterInfo[] parameters = method.GetParameters();
                        writes = parameter < 0 || !parameters[parameter].IsIn || parameters[parameter].IsOut;
                    }
                    else
                    {
                        writes = IndirectFieldMutation.Writes(code, _ => false,
                            argument: index => index == argument, callWrite: (called, index) => Analyze(called, index, path));
                    }
                }
            }
            finally { path.Remove((method, argument)); }
            lock (result.Writes) result.Writes[argument] = writes;
            return writes;
        }
    }
}
