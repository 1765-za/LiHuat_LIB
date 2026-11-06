using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Verse;

namespace LihuatLib.Mechanisms
{
    internal static class RegenerationReceiverSource
    {
        private static readonly Dictionary<Type, HashSet<FieldInfo>> members = new();

        internal static FieldInfo Find(MethodInfo method)
        {
            Type type = method.DeclaringType;
            if (method.IsStatic || !LifeResourceEvidence.IsCustom(type)) return null;
            if (!members.TryGetValue(type, out HashSet<FieldInfo> fields))
            {
                fields = new HashSet<FieldInfo>();
                for (Type cursor = type; LifeResourceEvidence.IsCustom(cursor); cursor = cursor.BaseType)
                    foreach (FieldInfo field in cursor.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                        if (typeof(Pawn).IsAssignableFrom(field.FieldType)) fields.Add(field);
                members.Add(type, fields);
            }
            if (fields.Count == 0) return null;
            var code = new LifeIlReader().Read(method);
            if (code == null) return null;
            FieldInfo selected = null, touched = null;
            for (int i = 0; i < code.Count; i++)
            {
                var line = code[i];
                OpCode op = line.opcode;
                if ((op == OpCodes.Starg || op == OpCodes.Starg_S || op == OpCodes.Ldarga || op == OpCodes.Ldarga_S)
                    && Convert.ToInt32(line.operand) == 0) return null;
                if (line.operand is not FieldInfo field || !fields.Contains(field)
                    || op != OpCodes.Ldfld && op != OpCodes.Ldflda && op != OpCodes.Stfld) continue;
                if (op == OpCodes.Stfld) continue;
                if (touched != null && touched != field) return null;
                touched = field;
                int receiver = i - 1;
                while (receiver >= 0 && code[receiver].opcode == OpCodes.Nop) receiver--;
                if (receiver < 0 || MapSpawnSource.LoadedArgument(code[receiver]) != 0 || line.labels.Count != 0) return null;
                selected = field;
            }
            return selected;
        }
    }
}
