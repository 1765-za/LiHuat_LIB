using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace LihuatLib.Mechanisms
{
    internal static class PrefixAdmissionEmission
    {
        private const BindingFlags StaticMethods = BindingFlags.Static | BindingFlags.NonPublic;
        private static readonly Type config = typeof(Harmony).Assembly.GetType("HarmonyLib.MethodCreatorConfig");
        private static readonly MethodInfo addCode = AccessTools.Method(config, "AddCodes", new[] { typeof(IEnumerable<CodeInstruction>) });
        private static readonly Func<object, MethodBase> original = OriginalReader();
        private static bool installed;
        internal static MethodBase Original(object builder) => original(builder);

        private static Func<object, MethodBase> OriginalReader()
        {
            var read = new DynamicMethod(Guid.NewGuid().ToString("N"), typeof(MethodBase), new[] { typeof(object) }, typeof(PrefixAdmissionEmission).Module, true);
            ILGenerator il = read.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Castclass, config);
            il.Emit(OpCodes.Call, AccessTools.PropertyGetter(config, "MethodBase")); il.Emit(OpCodes.Ret);
            return (Func<object, MethodBase>)read.CreateDelegate(typeof(Func<object, MethodBase>));
        }

        internal static void Install()
        {
            if (installed) return;
            MethodInfo prefixes = AccessTools.Method(typeof(Harmony).Assembly.GetType("HarmonyLib.MethodCreator"), "AddPrefixes");
            DynamicMethod routedCode = CodeEmitter();
            int sites = 0;
            DynamicMethod routedPrefixes = IndependentIl.Clone(prefixes, (called, virtualCall) =>
            {
                if (called != addCode) return called;
                sites++;
                return routedCode;
            });
            if (sites == 0) throw new InvalidOperationException("Prefix call emission metadata changed.");
            NativeIlGuards.InstallControls(new[] { prefixes }, new[] { routedPrefixes });
            installed = true;
        }

        private static DynamicMethod CodeEmitter()
        {
            DynamicMethod body = IndependentIl.Clone(addCode);
            DynamicMethod method = IndependentIl.New(addCode);
            ILGenerator il = method.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, typeof(PrefixAdmissionEmission).GetMethod(nameof(RouteCodes), StaticMethods));
            il.Emit(OpCodes.Call, body); il.Emit(OpCodes.Ret);
            return method;
        }

        private static IEnumerable<CodeInstruction> RouteCodes(object builder, IEnumerable<CodeInstruction> codes)
        {
            foreach (CodeInstruction code in codes)
            {
                CodeInstruction routed = PrefixAdmission.Route(builder, code);
                if (routed == null) { yield return code; continue; }
                // Branches and exception regions must enter before the additional receiver argument.
                var receiver = new CodeInstruction(OpCodes.Ldarg_0);
                receiver.labels.AddRange(routed.labels); routed.labels.Clear();
                receiver.blocks.AddRange(routed.blocks); routed.blocks.Clear();
                yield return receiver;
                yield return routed;
            }
        }
    }
}
