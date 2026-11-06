using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace LihuatLib.Mechanisms
{
    internal sealed class LifeFieldSetterEvidence
    {
        private readonly LifeIlReader reader;
        private readonly Dictionary<MethodInfo, bool> known = new();
        internal LifeFieldSetterEvidence(LifeIlReader reader) { this.reader = reader; }

        internal bool Matches(MethodInfo method)
        {
            if (!method.IsStatic || method.ReturnType != typeof(void) || !LifeResourceEvidence.IsCustom(method.DeclaringType)) return false;
            ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length != 3 || parameters[0].ParameterType != typeof(FieldInfo)
                || parameters[1].ParameterType != typeof(object) || parameters[2].ParameterType != typeof(object)) return false;
            if (known.TryGetValue(method, out bool result)) return result;
            bool emitsField = false, invokes = false;
            var visited = new HashSet<MethodInfo>();
            var pending = new Stack<MethodInfo>();
            pending.Push(method);
            while (pending.Count != 0 && visited.Count < 128)
            {
                MethodInfo current = pending.Pop();
                if (!visited.Add(current)) continue;
                var code = reader.Read(current);
                if (code == null) continue;
                bool storeOpcode = false, fieldEmit = false;
                foreach (var line in code)
                {
                    if (line.opcode == OpCodes.Ldsfld && line.operand is FieldInfo opcode && opcode.DeclaringType == typeof(OpCodes)
                        && (opcode.Name == nameof(OpCodes.Stfld) || opcode.Name == nameof(OpCodes.Stsfld))) storeOpcode = true;
                    if (line.operand is not MethodInfo call) continue;
                    if (call.DeclaringType == typeof(ILGenerator) && call.Name == nameof(ILGenerator.Emit))
                    {
                        ParameterInfo[] args = call.GetParameters();
                        fieldEmit |= args.Length == 2 && args[1].ParameterType == typeof(FieldInfo);
                    }
                    invokes |= typeof(Delegate).IsAssignableFrom(call.DeclaringType) && call.Name == "Invoke";
                    if (call.Module.Assembly == method.Module.Assembly && !call.ContainsGenericParameters) pending.Push(call);
                }
                emitsField |= storeOpcode && fieldEmit;
            }
            return known[method] = emitsField && invokes;
        }
    }
}
