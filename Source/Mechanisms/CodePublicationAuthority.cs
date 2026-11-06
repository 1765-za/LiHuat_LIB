using System;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace LihuatLib.Mechanisms
{
    internal static class CodePublicationAuthority
    {
        private sealed class Issued { }
        private static readonly ConditionalWeakTable<MethodBase, Issued> issued = new();
        private static readonly Type metadataMethod = typeof(object).GetMethod(nameof(ToString)).GetType();

        internal static bool Metadata(MethodInfo method) => method != null && method.GetType() == metadataMethod
            && method.DeclaringType != null && !method.Module.Assembly.IsDynamic && InfrastructureAssemblies.Contains(method.Module.Assembly);
        internal static bool Callback(MethodInfo method) => Metadata(method) || method != null && issued.TryGetValue(method, out _);

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void RequireCaller()
        {
            MethodBase caller = new StackFrame(2, false).GetMethod();
            if (caller is MethodInfo method && Callback(method) || caller is ConstructorInfo constructor
                && !constructor.Module.Assembly.IsDynamic && InfrastructureAssemblies.Contains(constructor.Module.Assembly)
                || caller != null && PatchProvenance.ReplacesInfrastructure(caller)) return;
            throw new UnauthorizedAccessException("Code publication requires an authenticated infrastructure caller.");
        }

        internal static void RequireBlueprint(MethodInfo method)
        {
            if (!Metadata(method)) throw new UnauthorizedAccessException("Detector blueprint is not registered metadata.");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void Record(DynamicMethod method)
        {
            RequireCaller();
            issued.GetValue(method, _ => new Issued());
        }
    }
}
