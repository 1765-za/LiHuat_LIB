using System;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using LihuatLib.Mechanisms;

namespace LihuatLib.Integrations
{
    internal static class HarmonyLifecycleCopies
    {
        internal static void Install()
        {
            InstallFor(typeof(Harmony).Assembly);
            MonoModAssemblyIntegration.Install();
        }

        internal static void InstallFor(Assembly assembly)
        {
            var definition = assembly.GetType("MonoMod.Utils.DynamicMethodDefinition");
            if (definition == null) return;
            MethodInfo generate = AccessTools.DeclaredMethod(definition, "Generate", new[] { typeof(object) });
            MethodInfo source = AccessTools.PropertyGetter(definition, "OriginalMethod");
            if (NativeIlGuards.Contains(generate)) return;
            DynamicMethod body = IndependentIl.Clone(generate);
            DynamicMethod wrapper = IndependentIl.New(generate);
            ILGenerator il = wrapper.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, body);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, source);
            il.Emit(OpCodes.Call, GeneratedIntegrity.Create(AccessTools.Method(typeof(ProtectedReverseCopy), nameof(ProtectedReverseCopy.Wrap))));
            il.Emit(OpCodes.Ret);
            if (assembly == typeof(Harmony).Assembly)
                NativeIlGuards.InstallControls(new[] { generate }, new[] { wrapper });
            else
            {
                MethodInfo copy = AccessTools.Method(definition, "_CopyMethodToDefinition");
                MethodInfo getBody = AccessTools.Method(typeof(MethodBase), nameof(MethodBase.GetMethodBody));
                MethodInfo read = AccessTools.Method(typeof(NativeRuntime), nameof(NativeRuntime.SourceBody));
                int reads = 0;
                DynamicMethod copied = IndependentIl.Clone(copy, rewrite: (writer, opcode, operand) =>
                {
                    if (!Equals(operand, getBody)) return false;
                    writer.Emit(OpCodes.Call, read); reads++;
                    return true;
                });
                if (reads != 1) throw new InvalidOperationException("MonoMod source-body reader changed: " + assembly.FullName);
                NativeIlGuards.InstallControls(new[] { copy, generate }, new[] { copied, wrapper });
            }
        }
    }
}
