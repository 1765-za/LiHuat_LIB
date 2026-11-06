using System;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace LihuatLib.Mechanisms
{
    internal static class HarmonyFieldReference
    {
        private static readonly Type[] owners = { typeof(Thing), typeof(Pawn), typeof(Pawn_HealthTracker), typeof(HediffSet) };

        // FieldRef returns an address, not a value. Match its signature without invoking the
        // delegate or a foreign type initializer; cached accessors must work the same way.
        internal static bool Sensitive(MethodInfo method, Func<FieldInfo, bool> sensitive)
        {
            if (method == null || !method.ReturnType.IsByRef) return false;
            Type owner;
            Type value = method.ReturnType.GetElementType();
            Type declaring = method.DeclaringType;
            if (method.Name == "Invoke" && declaring?.IsGenericType == true
                && declaring.GetGenericTypeDefinition() == typeof(AccessTools.FieldRef<,>))
                owner = declaring.GetGenericArguments()[0];
            else if (declaring == typeof(AccessTools) && method.IsGenericMethod && method.Name == nameof(AccessTools.FieldRefAccess)
                && method.GetGenericArguments().Length == 2 && method.GetParameters().Length > 0
                && method.GetParameters()[0].ParameterType == method.GetGenericArguments()[0])
                owner = method.GetGenericArguments()[0];
            else return false;

            foreach (Type type in owners)
            {
                if (!owner.IsAssignableFrom(type) && !type.IsAssignableFrom(owner)) continue;
                foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    if (Compatible(field.FieldType, value) && sensitive(field)) return true;
            }
            return false;
        }

        private static bool Compatible(Type field, Type reference) => field == reference
            || field.IsEnum && Enum.GetUnderlyingType(field) == reference
            || !field.IsValueType && reference.IsAssignableFrom(field);
    }
}
