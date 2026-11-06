using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace LihuatLib.Combat
{
    internal static class ManagedHealthStorage
    {
        internal static bool Numeric(Type type) => type == typeof(float) || type == typeof(double) || type == typeof(int);
        internal static Type Element(Type type) => type.IsArray && type.GetArrayRank() == 1 ? type.GetElementType()
            : type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>) ? type.GetGenericArguments()[0]
            : type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>) && type.GetGenericArguments()[0] == typeof(int)
                ? type.GetGenericArguments()[1] : null;
        internal static bool Supported(Type type) => Numeric(type) || Numeric(Element(type));
        internal static float Read(FieldInfo field, object state, int core)
        {
            object value = field.GetValue(state);
            if (value is Array array) value = array.GetValue(core);
            else if (value is IList list) value = list[core];
            else if (value is IDictionary dictionary) value = dictionary[core];
            return Convert.ToSingle(value);
        }
        internal static bool Indexer(MethodInfo method, string name) => method.Name == name
            && Element(method.DeclaringType) != null && !method.DeclaringType.IsArray;
        internal static bool Reads(ManagedValueExpression value, FieldInfo field, ManagedValueExpression index = null)
        {
            value = value?.Unwrapped();
            if (value == null) return false;
            if (value.Operation.Name.StartsWith("ldind", StringComparison.Ordinal) && value.Inputs.Length == 1) value = value.Inputs[0];
            if (Numeric(field.FieldType)) return value.Operation == OpCodes.Ldfld && Equals(value.Operand, field)
                && value.Inputs.Length == 1 && value.Inputs[0].IsThis;
            if (value.Inputs.Length != 2 || !Field(value.Inputs[0], field)) return false;
            if (!value.Operation.Name.StartsWith("ldelem", StringComparison.Ordinal)
                && (value.Operand is not MethodInfo method || !Indexer(method, "get_Item"))) return false;
            return index == null || Equivalent(value.Inputs[1], index);
        }
        internal static bool Field(ManagedValueExpression value, FieldInfo field) => value?.Operation == OpCodes.Ldfld
            && Equals(value.Operand, field) && value.Inputs.Length == 1 && value.Inputs[0].IsThis;
        internal static bool Equivalent(ManagedValueExpression left, ManagedValueExpression right)
        {
            left = left?.Unwrapped(); right = right?.Unwrapped();
            if (left == null || right == null || left.Operation != right.Operation || left.Argument != right.Argument
                || !Equals(left.Operand, right.Operand) || left.Inputs.Length != right.Inputs.Length) return false;
            for (int i = 0; i < left.Inputs.Length; i++) if (!Equivalent(left.Inputs[i], right.Inputs[i])) return false;
            return true;
        }
    }
}
