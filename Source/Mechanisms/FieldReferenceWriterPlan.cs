using System;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace LihuatLib.Mechanisms
{
    internal static class FieldReferenceWriterPlan
    {
        internal static int[] Arguments(MethodInfo method, Type[] types)
        {
            if (method.ReturnType != typeof(void) || method.IsAbstract || method.ContainsGenericParameters) return null;
            ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Count(p => p.ParameterType == typeof(FieldInfo)) != 1
                || parameters.Count(p => p.ParameterType == typeof(object)) < 2) return null;
            var instructions = new LifeIlReader().Read(method);
            if (instructions == null) return null;
            int[] found = null;
            for (int at = 0; at < instructions.Count; at++)
            {
                CodeInstruction line = instructions[at];
                if (line.opcode != OpCodes.Call && line.opcode != OpCodes.Callvirt || line.operand is not MethodInfo write
                    || write.IsStatic || write.ReturnType != typeof(void)
                    || !write.GetParameters().Select(p => p.ParameterType).SequenceEqual(new[] { typeof(object), typeof(object) })) continue;
                int[] arguments = FieldReferenceWriterFlow.Resolve(method, instructions, at, write.DeclaringType);
                if (arguments == null || !ImplementsStore(write, types)) continue;
                if (found != null && !found.SequenceEqual(arguments)) return null;
                found = arguments;
            }
            return found;
        }

        private static bool ImplementsStore(MethodInfo contract, Type[] types)
        {
            if (!contract.DeclaringType.IsInterface) return Stores(contract);
            foreach (Type type in types)
            {
                if (type == null || type.IsInterface || !contract.DeclaringType.IsAssignableFrom(type)) continue;
                InterfaceMapping map = type.GetInterfaceMap(contract.DeclaringType);
                int index = Array.IndexOf(map.InterfaceMethods, contract);
                if (index < 0 || map.TargetMethods[index].IsAbstract) continue;
                if (Stores(map.TargetMethods[index])) return true;
            }
            return false;
        }

        private static bool Stores(MethodInfo body)
        {
            byte[] il = body.GetMethodBody()?.GetILAsByteArray();
            if (il == null) return false;
            var code = LifeIlReader.Decode(body, il);
            return code != null && IndirectFieldMutation.Writes(code, _ => false, Reference);
        }

        private static bool Reference(MethodInfo method) => method.ReturnType.IsByRef
            && method.Name == "Invoke" && typeof(Delegate).IsAssignableFrom(method.DeclaringType);
    }
}
