using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace LihuatLib.Mechanisms
{
    internal static class FieldReferenceConsumer
    {
        internal static List<CodeInstruction> Read(MethodInfo method, byte[] metadata)
        {
            var calls = new LifeCallGraphReader();
            var code = calls.Read(method, metadata);
            if (calls.Failure != null) LifeDiscoveryMetadata.Report(method, calls.Failure);
            return code;
        }

        internal static bool Special(MethodInfo method) => method != null && !method.ContainsGenericParameters
            && method.ReturnType.IsByRef && (method.IsGenericMethod || method.DeclaringType?.IsGenericType == true
                || UnsafeFieldMetadata.Resolve(method) != null);

        internal static bool Required(MethodInfo method, List<CodeInstruction> code)
        {
            if (code == null) return false;
            foreach (CodeInstruction line in code)
                if (line.operand is MethodInfo called && Special(called) && Escapes(method, code, line, called)
                    && GeneratedMethodCatalogue.TryGet(called, out var policy, true) && policy.Required) return true;
            return false;
        }

        internal static bool Escapes(MethodInfo method, List<CodeInstruction> code, CodeInstruction line, MethodInfo called)
            => line.opcode == OpCodes.Ldftn || (line.opcode == OpCodes.Call || line.opcode == OpCodes.Callvirt)
                && (DynamicWriteCatalogue.AddressResult(method.ReturnType)
                    || IndirectFieldMutation.Writes(code, _ => false, reference: candidate => candidate == called));
    }
}
