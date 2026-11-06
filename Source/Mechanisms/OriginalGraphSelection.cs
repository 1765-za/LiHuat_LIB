using System;
using System.Reflection;
using Verse;

namespace LihuatLib.Mechanisms
{
    internal static class OriginalGraphSelection
    {
        internal static bool PrivateHelper(MethodInfo caller, MethodInfo called) => called.IsPrivate
            && called.Module.Assembly == typeof(Pawn).Assembly && Owner(caller.DeclaringType) == Owner(called.DeclaringType);

        private static Type Owner(Type type)
        {
            while (type?.DeclaringType != null) type = type.DeclaringType;
            return type;
        }
    }
}
