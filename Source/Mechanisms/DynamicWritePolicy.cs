using System;
using System.Reflection;
using System.Threading;
using LihuatLib.Core;
using LihuatLib.StarBlood;
using Verse;

namespace LihuatLib.Mechanisms
{
    internal sealed class DynamicWritePolicy
    {
        internal readonly Delegate Original;
        private readonly FieldInfo[] fields;
        private readonly bool changesAuthority;
        private static long blocked;
        internal static long Blocked => Interlocked.Read(ref blocked);
        internal DynamicWritePolicy(Delegate original, FieldInfo[] fields, bool changesAuthority)
        { Original = original; this.fields = fields; this.changesAuthority = changesAuthority; }
        internal bool Allows()
        {
            if (DeveloperExecution.Active || Scribe.mode != LoadSaveMode.Inactive && Scribe.mode != LoadSaveMode.Saving) return true;
            if (changesAuthority) return false;
            foreach (FieldInfo field in fields) if (GeneratedIntegrity.Owns(field)) return false;
            return AllowsTarget(Original?.Target);
        }
        internal bool AllowsTarget(object target) => AllowsNativeTarget(target)
            && (target is not Thing thing || LihuatGame.Current?.Lifecycle.AllowsGeneratedMutation(thing, fields) != false);
        internal static bool AllowsNativeFields(bool ownsFields) => DeveloperExecution.Active
            || Scribe.mode != LoadSaveMode.Inactive && Scribe.mode != LoadSaveMode.Saving || !ownsFields;
        internal static bool AllowsNativeTarget(object target)
        {
            if (DeveloperExecution.Active) return true;
            Pawn pawn = target as Pawn ?? LihuatGame.Current?.Subjects.Find(target)?.Subject.Pawn;
            return !PawnContinuity.Protects(pawn);
        }
        internal static void Denied() => Interlocked.Increment(ref blocked);
        internal static void RequireInvoke(System.Reflection.Emit.DynamicMethod method, object[] arguments)
        {
            DynamicWriteCatalogue.Prepare(method);
            if (!DynamicWriteCatalogue.Ready || DynamicWriteCatalogue.Building
                || !DynamicWriteCatalogue.TryPolicy(method, out FieldInfo[] writes, out bool authority)) return;
            var policy = new DynamicWritePolicy(null, writes, authority);
            bool allowed = policy.Allows();
            if (allowed && arguments != null)
                foreach (object argument in arguments) if (!policy.AllowsTarget(argument)) { allowed = false; break; }
            if (allowed) return;
            Denied();
            throw new UnauthorizedAccessException("Generated mutation of protected state was rejected.");
        }
    }
}
