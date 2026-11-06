using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using LihuatLib.StarBlood;
using System.Runtime.InteropServices;
using Verse;

namespace LihuatLib.Mechanisms
{
    public static class PointerAccessProtection
    {
        private static readonly HashSet<MethodInfo> registered = new HashSet<MethodInfo>();
        private static readonly List<Func<object, bool>> handlers = new List<Func<object, bool>>();
        public static long Rejected { get; private set; }

        internal static void Install()
        {
            if (!LihuatLib.Configuration.HighMechanismPolicy.NativeProtection) return;
            MethodInfo pin = AccessTools.Method(typeof(GCHandle), nameof(GCHandle.Alloc), new[] { typeof(object), typeof(GCHandleType) });
            NativeIlGuards.Guard(pin, AccessTools.Method(typeof(PointerAccessProtection), nameof(RequirePinnedAccess)), 0, 1);
            ObjectAddressBoundary.Install();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void Register(MethodInfo handler)
        {
            if (!LihuatLib.Configuration.HighMechanismPolicy.NativeProtection) return;
            CodePublicationAuthority.RequireCaller();
            CodePublicationAuthority.RequireBlueprint(handler);
            if (registered.Contains(handler)) return;
            var callback = (Func<object, bool>)GeneratedIntegrity.Create(handler).CreateDelegate(typeof(Func<object, bool>));
            handlers.Add(callback);
            registered.Add(handler);
        }

        private static void RequirePinnedAccess(object target, GCHandleType type)
        {
            if (type == GCHandleType.Pinned) RequireAccess(target);
        }

        internal static void RequireAccess(object target)
        {
            if (target == null) return;
            bool denied = Core.InfrastructureAssemblies.Contains(target.GetType().Assembly)
                || target is Hediff hediff && PinnedHediffs.Contains(hediff);
            if (!denied)
            {
                Pawn pawn = target as Pawn ?? LihuatGame.Current?.Subjects.Find(target)?.Subject.Pawn;
                denied = pawn != null && PawnContinuity.Protects(pawn);
            }
            for (int i = 0; !denied && i < handlers.Count; i++) denied = !handlers[i](target);
            if (!denied) return;
            Rejected++;
            // Returning null would let an unchecked native caller dereference an invalid address.
            throw new UnauthorizedAccessException("Cannot expose the native address of a protected object.");
        }
    }
}
