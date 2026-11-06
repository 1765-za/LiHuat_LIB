using System;
using System.Reflection;
using System.Reflection.Emit;

namespace LihuatLib.Mechanisms
{
    internal static class IntegrityEmission
    {
        private const BindingFlags Methods = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        internal static DynamicMethod Registration(MethodInfo source, MethodInfo detour, MethodInfo gate, MethodInfo commit) =>
            Build(source, detour, gate, commit, false);
        internal static DynamicMethod Reverse(MethodInfo source, MethodInfo detour, MethodInfo gate, MethodInfo commit) =>
            Build(source, detour, gate, commit, true);

        private static DynamicMethod Build(MethodInfo source, MethodInfo detour, MethodInfo gate, MethodInfo commit, bool reverse)
        {
            int commits = 0, replacements = 0, admissions = 0;
            bool prefixList = false;
            DynamicMethod commitCopy = GeneratedIntegrity.Create(commit);
            MethodInfo wrap = reverse ? typeof(ProtectedReverseCopy).GetMethod(nameof(ProtectedReverseCopy.Wrap))
                : typeof(NativeIlGuards).GetMethod("Wrap", Methods);
            DynamicMethod wrapCopy = GeneratedIntegrity.Create(wrap);
            DynamicMethod body = IndependentIl.Clone(source, (called, virtualCall) =>
            {
                if (called != detour) return called;
                commits++;
                return commitCopy;
            }, (il, opcode, operand) =>
            {
                if (!reverse && opcode == OpCodes.Ldfld && operand is FieldInfo group && group.DeclaringType == typeof(HarmonyLib.PatchInfo))
                    prefixList = group.Name == nameof(HarmonyLib.PatchInfo.prefixes);
                if (!reverse && prefixList && opcode == OpCodes.Call && operand is MethodInfo sorted
                    && sorted.DeclaringType == source.DeclaringType && sorted.Name == "GetSortedPatchCalls")
                {
                    il.Emit(opcode, sorted); il.Emit(OpCodes.Dup); il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Call, typeof(PrefixAdmission).GetMethod(nameof(PrefixAdmission.Prepare), Methods));
                    prefixList = false; admissions++;
                    return true;
                }
                if (opcode != OpCodes.Ldfld || !(operand is FieldInfo field)
                    || field.Name != "Item1" || field.FieldType != typeof(MethodInfo)) return false;
                il.Emit(opcode, field);
                il.Emit(reverse ? OpCodes.Ldarg_1 : OpCodes.Ldarg_0);
                il.Emit(OpCodes.Call, wrapCopy);
                il.Emit(OpCodes.Call, typeof(PublishedMethod).GetMethod(nameof(PublishedMethod.Materialize), Methods));
                replacements++;
                return true;
            });
            if (commits != 1 || replacements != 1 || !reverse && admissions != 1)
                throw new InvalidOperationException("Registration metadata IL shape changed: " + source);
            DynamicMethod wrapper = IndependentIl.New(source);
            ILGenerator generator = wrapper.GetILGenerator();
            LocalBuilder result = generator.DeclareLocal(typeof(MethodInfo));
            Label allowed = generator.DefineLabel();
            generator.Emit(OpCodes.Ldarg_0); generator.Emit(OpCodes.Ldarg_1); generator.Emit(OpCodes.Ldloca, result);
            generator.Emit(OpCodes.Call, GeneratedIntegrity.Create(gate));
            generator.Emit(OpCodes.Brtrue, allowed);
            generator.Emit(OpCodes.Ldloc, result); generator.Emit(OpCodes.Ret);
            generator.MarkLabel(allowed);
            for (int i = 0; i < source.GetParameters().Length; i++) generator.Emit(OpCodes.Ldarg, (short)i);
            generator.Emit(OpCodes.Call, body); generator.Emit(OpCodes.Ret);
            return wrapper;
        }
    }
}
