using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace LihuatLib.Mechanisms
{
    internal static class OwnedEmissionBoundary
    {
        internal static void Install()
        {
            var operations = new HashSet<string>
            {
                nameof(ILGenerator.Emit), nameof(ILGenerator.EmitCall), nameof(ILGenerator.EmitCalli),
                nameof(ILGenerator.DeclareLocal), nameof(ILGenerator.DefineLabel), nameof(ILGenerator.MarkLabel),
                nameof(ILGenerator.BeginExceptionBlock), nameof(ILGenerator.BeginCatchBlock),
                nameof(ILGenerator.BeginExceptFilterBlock), nameof(ILGenerator.BeginFaultBlock),
                nameof(ILGenerator.BeginFinallyBlock), nameof(ILGenerator.EndExceptionBlock)
            };
            MethodInfo ownsGenerator = AccessTools.Method(typeof(DynamicWriteCatalogue), nameof(DynamicWriteCatalogue.OwnsEmission));
            foreach (MethodInfo method in DynamicWriteCatalogue.GeneratorType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(method => operations.Contains(method.Name)).Select(NativeIlGuards.Definition).Distinct()
                .OrderBy(method => method.MetadataToken))
                Preserve(method, ownsGenerator);

            MethodInfo ownsMethod = AccessTools.Method(typeof(DynamicWriteCatalogue), nameof(DynamicWriteCatalogue.OwnsMethod));
            foreach (MethodInfo method in typeof(DynamicMethod).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(method => method.Name == nameof(DynamicMethod.GetILGenerator) || method.Name == nameof(DynamicMethod.CreateDelegate))
                .OrderBy(method => method.MetadataToken))
                Preserve(method, ownsMethod);
        }

        private static void Preserve(MethodInfo method, MethodInfo owns)
        {
            DynamicMethod source = IndependentIl.Clone(method);
            NativeIlGuards.RegisterLayer(method, typeof(OwnedEmissionBoundary).FullName, NativeIlGuards.BodyOrder,
                body => IlBoundaryEmitter.Conditional(method, body, source, owns, new[] { 0 }), false);
        }
    }
}
