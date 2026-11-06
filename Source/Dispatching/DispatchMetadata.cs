using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using HarmonyLib;
using LihuatLib.Mechanisms;

namespace LihuatLib.Dispatching
{
    internal static class DispatchMetadata
    {
        internal static IEnumerable<(MethodBase Target, DispatchHandler Handler)> Read(Type type)
        {
            var target = type.GetCustomAttribute<DispatchTargetAttribute>();
            int order = type.GetCustomAttribute<DispatchOrderAttribute>()?.Value
                ?? throw new InvalidOperationException("Adapter needs an explicit dispatch order: " + type);
            MethodInfo single = AccessTools.DeclaredMethod(type, "TargetMethod");
            MethodInfo multiple = AccessTools.DeclaredMethod(type, "TargetMethods");
            MethodBase[] targets = single != null ? new[] { (MethodBase)Invoke(single) }
                : multiple != null ? ((IEnumerable<MethodBase>)Invoke(multiple)).ToArray() : null;
            foreach (MethodInfo method in type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                DispatchPhase? phase = Phase(method);
                if (!phase.HasValue) continue;
                var local = method.GetCustomAttribute<DispatchTargetAttribute>();
                MethodBase[] selected = local != null || targets == null ? new[] { Resolve(target, local) } : targets;
                foreach (MethodBase original in selected.Select(m => m is MethodInfo info ? NativeIlGuards.Definition(info) : m).Distinct())
                {
                    if (original == null) throw new MissingMethodException("Missing target for " + type.FullName);
                    yield return (original, new DispatchHandler { Method = method, Phase = phase.Value, Order = order,
                        Priority = method.GetCustomAttribute<DispatchPriorityAttribute>()?.Value ?? 400 });
                }
            }
        }

        private static DispatchPhase? Phase(MethodInfo method)
        {
            if (method.Name == "Prefix" || method.IsDefined(typeof(DispatchPrefixAttribute))) return DispatchPhase.Prefix;
            if (method.Name == "Postfix" || method.IsDefined(typeof(DispatchPostfixAttribute))) return DispatchPhase.Postfix;
            if (method.Name == "Transpiler" || method.IsDefined(typeof(DispatchTranspilerAttribute))) return DispatchPhase.Transpiler;
            if (method.Name == "Finalizer" || method.IsDefined(typeof(DispatchFinalizerAttribute))) return DispatchPhase.Finalizer;
            return null;
        }

        private static MethodBase Resolve(DispatchTargetAttribute parent, DispatchTargetAttribute local)
        {
            Type owner = local?.Type ?? parent?.Type;
            string name = local?.Name ?? parent?.Name;
            if (owner == null || name == null) throw new ArgumentException("Incomplete dispatch target.");
            DispatchMember member = local?.Member ?? parent.Member;
            return member == DispatchMember.Getter ? AccessTools.PropertyGetter(owner, name)
                : member == DispatchMember.Setter ? AccessTools.PropertySetter(owner, name)
                : AccessTools.Method(owner, name, local?.Arguments ?? parent?.Arguments);
        }

        internal static object Invoke(MethodInfo method, params object[] arguments)
        {
            try { return method.Invoke(null, arguments); }
            catch (TargetInvocationException error) when (error.InnerException != null)
            { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
        }
    }
}
