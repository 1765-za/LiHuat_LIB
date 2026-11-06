using System;
using System.Reflection;
using Verse;

namespace LihuatLib.Mechanisms
{
    internal static class LifeRetirementRecipient
    {
        internal const int Receiver = -1;
        internal const int Ambiguous = -2;

        internal static bool Candidate(MethodInfo method) => !method.IsAbstract && !method.ContainsGenericParameters
            && !method.ReturnType.IsByRef && !method.ReturnType.IsPointer && !method.ReturnType.IsFunctionPointer
            && !method.ReturnType.IsByRefLike
            && For(method) != Ambiguous;

        internal static bool UsesExplicitResult(MethodInfo method) => method.ReturnType != typeof(void)
            && method.ReturnType != typeof(bool) && !typeof(Exception).IsAssignableFrom(method.ReturnType);

        internal static int? ExceptionArgument(MethodInfo method)
        {
            if (!typeof(Exception).IsAssignableFrom(method.ReturnType)) return null;
            int? found = null;
            ParameterInfo[] parameters = method.GetParameters();
            for (int i = 0; i < parameters.Length; i++)
                if (typeof(Exception).IsAssignableFrom(parameters[i].ParameterType)
                    && method.ReturnType.IsAssignableFrom(parameters[i].ParameterType))
                {
                    if (found.HasValue) return null;
                    found = i + (method.IsStatic ? 0 : 1);
                }
            return found;
        }

        internal static int For(MethodInfo method)
        {
            if (!method.IsStatic && typeof(Pawn).IsAssignableFrom(method.DeclaringType)) return Receiver;
            int recipient = Ambiguous;
            int boxed = Ambiguous, boxedCount = 0;
            ParameterInfo[] parameters = method.GetParameters();
            for (int i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].IsOut) continue;
                Type type = parameters[i].ParameterType;
                if (type.IsByRef) type = type.GetElementType();
                if (type == typeof(object)) { boxed = i; boxedCount++; continue; }
                if (!typeof(Thing).IsAssignableFrom(type)
                    && !typeof(Pawn_HealthTracker).IsAssignableFrom(type) && !typeof(HediffSet).IsAssignableFrom(type)) continue;
                if (recipient != Ambiguous) return Ambiguous;
                recipient = i;
            }
            return recipient != Ambiguous ? recipient : boxedCount == 1 ? boxed : Ambiguous;
        }
    }
}
