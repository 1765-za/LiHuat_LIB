using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using LihuatLib.StarBlood;

namespace LihuatLib.Mechanisms
{
    internal static class FieldReferenceWriterGuard
    {
        private static readonly HashSet<MethodInfo> installed = new();
        private static readonly MethodInfo admission = AccessTools.Method(typeof(FieldReferenceWriterGuard), nameof(Require));
        internal static void Discover(MethodInfo method, Type[] types)
        {
            if (installed.Contains(method) || PatchProvenance.IsReplacement(method)) return;
            int[] arguments = FieldReferenceWriterPlan.Arguments(method, types);
            if (arguments == null) return;
            NativeIlGuards.Guard(method, admission, arguments);
            installed.Add(method);
        }

        private static bool Require(FieldInfo field, object owner, object value)
        {
            if (field == null || !PawnContinuity.BlocksWrite(field, owner, value) && FieldWriteProtection.Allows(field, owner, value)) return true;
            DynamicWritePolicy.Denied();
            throw new UnauthorizedAccessException("Field reference mutation of protected state was rejected.");
        }
    }
}
