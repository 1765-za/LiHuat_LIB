using System;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace LihuatLib.Mechanisms
{
    internal static class DynamicWriteBoundary
    {
        internal static void Install()
        {
            if (!LihuatLib.Configuration.HighMechanismPolicy.NativeProtection) return;
            PawnCollectionCalls.Prepare();
            OwnedEmissionBoundary.Install();
            NativeIlGuards.Guard(AccessTools.Method(DynamicWriteCatalogue.GeneratorType, nameof(ILGenerator.Emit), new[] { typeof(OpCode), typeof(ConstructorInfo) }),
                AccessTools.Method(typeof(DynamicWriteCatalogue), nameof(DynamicWriteCatalogue.ObserveConstructor)), 0, 1, 2);
            NativeIlGuards.Guard(AccessTools.Method(DynamicWriteCatalogue.GeneratorType, nameof(ILGenerator.Emit), new[] { typeof(OpCode), typeof(FieldInfo) }),
                AccessTools.Method(typeof(DynamicWriteBoundary), nameof(EmitField)), 0, 1, 2);
            NativeIlGuards.Guard(AccessTools.Method(DynamicWriteCatalogue.GeneratorType, nameof(ILGenerator.Emit), new[] { typeof(OpCode), typeof(MethodInfo) }),
                AccessTools.Method(typeof(DynamicWriteBoundary), nameof(EmitCall)), 0, 1, 2);
            NativeIlGuards.Guard(AccessTools.DeclaredMethod(typeof(DynamicMethod), nameof(DynamicMethod.Invoke)),
                AccessTools.Method(typeof(DynamicWritePolicy), nameof(DynamicWritePolicy.RequireInvoke)), 0, 4);
            foreach (Type[] signature in new[] { new[] { typeof(Type) }, new[] { typeof(Type), typeof(object) } })
            {
                NativeIlGuards.Guard(AccessTools.Method(typeof(DynamicMethod), nameof(DynamicMethod.CreateDelegate), signature),
                    AccessTools.Method(typeof(DynamicWriteCatalogue), nameof(DynamicWriteCatalogue.Prepare)), 0);
                NativeIlGuards.FilterArguments(AccessTools.Method(typeof(DynamicMethod), nameof(DynamicMethod.CreateDelegate), signature),
                    AccessTools.Method(typeof(DynamicWriteDelegate), nameof(DynamicWriteDelegate.Publish)), 0);
            }
            DynamicIlCatalogue.Install();
            Integrations.HarmonyNativeAddressBoundary.Install();
            GeneratedMethodBoundary.Install();
            ReflectionInvocationBoundary.Install();
            DynamicWriteCatalogue.Ready = true;
        }
        private static bool EmitField(ILGenerator generator, OpCode opcode, FieldInfo field)
        {
            if (!DynamicRootMutation.Emit(generator, opcode, field)) return false;
            DynamicWriteCatalogue.Observe(generator, opcode, field);
            if (DynamicWriteCatalogue.Ready && !DynamicWriteCatalogue.Building && field != null
                && (opcode == OpCodes.Ldflda || opcode == OpCodes.Ldsflda)
                && DynamicWriteCatalogue.GeneratorType.IsInstanceOfType(generator)
                && DynamicWriteCatalogue.ReturnsReference(generator) && GeneratedMethodCatalogue.Sensitive(field)
                && !DynamicWriteCatalogue.For(generator).Trusted)
            {
                generator.Emit(OpCodes.Call, GeneratedFieldWrites.Reference(field));
                return false;
            }
            return true;
        }
        private static bool EmitCall(ILGenerator generator, OpCode opcode, MethodInfo method)
        {
            if (DynamicWriteCatalogue.Ready && !DynamicWriteCatalogue.Building && opcode == OpCodes.Call
                && DynamicWriteCatalogue.GeneratorType.IsInstanceOfType(generator)
                && !DynamicWriteCatalogue.For(generator).Trusted
                && ObjectAddressCalls.Replacement(method) is MethodInfo address)
            {
                generator.Emit(OpCodes.Call, address);
                return false;
            }
            if (DynamicWriteCatalogue.Ready && !DynamicWriteCatalogue.Building && method != null
                && (opcode == OpCodes.Call || opcode == OpCodes.Callvirt)
                && DynamicWriteCatalogue.GeneratorType.IsInstanceOfType(generator)
                && DynamicLifecycleCalls.Candidate(method, opcode)
                && !DynamicWriteCatalogue.For(generator).Trusted
                && DynamicLifecycleCalls.TryWrap(method, opcode, out DynamicMethod guarded))
            {
                generator.Emit(OpCodes.Call, guarded);
                return false;
            }
            DynamicWriteCatalogue.ObserveCall(generator, opcode, method);
            return true;
        }
    }
}
