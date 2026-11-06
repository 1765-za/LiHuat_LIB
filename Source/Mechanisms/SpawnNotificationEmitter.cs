using System;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Verse;

namespace LihuatLib.Mechanisms
{
    internal static class SpawnNotificationEmitter
    {
        internal delegate void Notification(Thing thing, IntVec3 cell, Map map, Rot4 rotation,
            WipeMode wipeMode, bool respawningAfterLoad, bool forbidLeavings, Thing result);

        internal static Notification Compile(MethodInfo original, MethodInfo notification, bool after)
        {
            if (!notification.IsStatic || notification.ReturnType != typeof(void) || notification.ContainsGenericParameters) return null;
            ParameterInfo[] inputs = original.GetParameters();
            ParameterInfo[] arguments = notification.GetParameters();
            var positions = new int[arguments.Length];
            for (int i = 0; i < arguments.Length; i++)
            {
                ParameterInfo argument = arguments[i];
                if (argument.ParameterType.IsByRef || argument.ParameterType.IsPointer) return null;
                int position = Array.FindIndex(inputs, p => p.Name == argument.Name);
                if (after && argument.Name == "__result") position = inputs.Length;
                else if (position < 0 && argument.Name.StartsWith("__", StringComparison.Ordinal)
                    && int.TryParse(argument.Name.Substring(2), out int index) && index >= 0 && index < inputs.Length) position = index;
                if (position < 0) return null;
                Type source = position == inputs.Length ? original.ReturnType : inputs[position].ParameterType;
                if (!argument.ParameterType.IsAssignableFrom(source)) return null;
                positions[i] = position;
            }
            Type[] parameters = inputs.Select(p => p.ParameterType).Concat(new[] { original.ReturnType }).ToArray();
            var method = new DynamicMethod("LihuatSpawnNotification_" + Guid.NewGuid().ToString("N"),
                typeof(void), parameters, typeof(SpawnNotificationEmitter).Module, true);
            ILGenerator il = method.GetILGenerator();
            for (int i = 0; i < arguments.Length; i++)
            {
                int position = positions[i];
                il.Emit(OpCodes.Ldarg, (short)position);
                if (parameters[position].IsValueType && !arguments[i].ParameterType.IsValueType) il.Emit(OpCodes.Box, parameters[position]);
            }
            il.Emit(OpCodes.Call, notification);
            il.Emit(OpCodes.Ret);
            return (Notification)method.CreateDelegate(typeof(Notification));
        }
    }
}
