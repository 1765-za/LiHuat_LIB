using System;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using LihuatLib.Core;

namespace LihuatLib.Mechanisms
{
    public static class ReverseLifecycle
    {
        private static readonly AccessTools.FieldRef<Pawn_HealthTracker, Pawn> owner = AccessTools.FieldRefAccess<Pawn_HealthTracker, Pawn>("pawn");
        private const int Restore = 7;
        private const int ReleaseKilled = 8;
        private const int ReleaseDespawned = 9;
        public static long GuardedCopies { get; private set; }
        internal static bool Destructive(MethodInfo method) { int entry = Entry(method); return entry >= 0 && entry != Restore; }

        private static int Entry(MethodBase original)
        {
            Type type = original.DeclaringType;
            if (type?.Assembly != typeof(Pawn).Assembly) return -1;
            if (type == typeof(PawnComponentsUtility))
            {
                if (original.Name == nameof(PawnComponentsUtility.RemoveComponentsOnKilled)) return ReleaseKilled;
                if (original.Name == nameof(PawnComponentsUtility.RemoveComponentsOnDespawned)) return ReleaseDespawned;
            }
            if (type == typeof(Pawn_HealthTracker))
            {
                if (original.Name == nameof(Pawn_HealthTracker.SetDead)) return (int)DestructiveEntry.SetDead;
                if (original.Name == nameof(Pawn_HealthTracker.ShouldBeDead)) return (int)DestructiveEntry.ShouldBeDead;
                if (original.Name == nameof(Pawn_HealthTracker.Notify_Resurrected)) return Restore;
            }
            if (typeof(Entity).IsAssignableFrom(type))
            {
                if (original.Name == nameof(Pawn.Kill)) return (int)DestructiveEntry.Kill;
                if (original.Name == nameof(Thing.Destroy)) return (int)DestructiveEntry.Destroy;
                if (original.Name == nameof(Pawn.Discard)) return (int)DestructiveEntry.Discard;
                if (original.Name == nameof(Thing.DeSpawn) || original.Name == nameof(Thing.ForceSetStateToUnspawned)) return (int)DestructiveEntry.DeSpawn;
                if (original.Name == nameof(Thing.SpawnSetup)) return Restore;
            }
            if (type == typeof(ResurrectionUtility) && original.Name == nameof(ResurrectionUtility.TryResurrect)) return Restore;
            if (type == typeof(WorldPawns) && original.Name == nameof(WorldPawns.RemoveAndDiscardPawnViaGC)) return (int)DestructiveEntry.WorldGC;
            return -1;
        }

        public static MethodInfo Wrap(MethodInfo replacement, MethodBase original)
        {
            if (replacement == null || original == null) return replacement;
            int entry = Entry(original);
            if (entry < 0) return replacement;
            Type[] arguments = replacement.GetParameters().Select(p => p.ParameterType).ToArray();
            int recipient = original.DeclaringType == typeof(WorldPawns) ? 1 : 0;
            DynamicMethod wrapper = new DynamicMethod(Guid.NewGuid().ToString("N"), replacement.ReturnType, arguments, typeof(ReverseLifecycle).Module, true);
            ILGenerator il = wrapper.GetILGenerator();
            Label allowed = il.DefineLabel();
            int mask = Guid.NewGuid().GetHashCode();
            Action load = () =>
            {
                il.Emit(OpCodes.Ldarg, (short)recipient); il.Emit(OpCodes.Ldc_I4, entry ^ mask); il.Emit(OpCodes.Ldc_I4, mask); il.Emit(OpCodes.Xor);
            };
            load(); il.Emit(OpCodes.Call, AccessTools.Method(typeof(ReverseLifecycle), nameof(Before)));
            il.Emit(OpCodes.Brtrue, allowed);
            if (replacement.ReturnType == typeof(bool)) il.Emit(OpCodes.Ldc_I4_0);
            else if (replacement.ReturnType != typeof(void)) throw new InvalidOperationException("Unsupported lifecycle return type");
            il.Emit(OpCodes.Ret);
            il.MarkLabel(allowed);
            for (int i = 0; i < arguments.Length; i++) il.Emit(OpCodes.Ldarg, (short)i);
            il.Emit(OpCodes.Call, replacement);
            LocalBuilder result = replacement.ReturnType == typeof(void) ? null : il.DeclareLocal(replacement.ReturnType);
            if (result != null) il.Emit(OpCodes.Stloc, result);
            load(); il.Emit(OpCodes.Call, AccessTools.Method(typeof(ReverseLifecycle), nameof(After)));
            if (result != null) il.Emit(OpCodes.Ldloc, result);
            il.Emit(OpCodes.Ret);
            GuardedCopies++;
            return wrapper;
        }

        private static Pawn PawnFor(object receiver) => receiver as Pawn ?? (receiver is Pawn_HealthTracker health ? owner(health) : null);
        private static bool Before(object receiver, int entry)
        {
            Pawn pawn = PawnFor(receiver);
            if (pawn == null || LihuatGame.Current == null) return true;
            if (entry == ReleaseKilled || entry == ReleaseDespawned)
                return LifecycleComponentRelease.Allowed(pawn, entry == ReleaseKilled)
                    && LihuatGame.Current.Lifecycle.AllowsComponentRemoval(pawn);
            return entry == Restore ? LihuatGame.Current.Lifecycle.AllowsRestore(pawn)
                : LihuatGame.Current.Lifecycle.Before(pawn, (DestructiveEntry)entry);
        }
        private static void After(object receiver, int entry)
        {
            if (entry == ReleaseKilled || entry == ReleaseDespawned) return;
            Pawn pawn = PawnFor(receiver);
            if (pawn == null || LihuatGame.Current == null) return;
            if (entry == Restore) LihuatGame.Current.Lifecycle.Restored(pawn);
            else LihuatGame.Current.Lifecycle.After(pawn, (DestructiveEntry)entry);
        }
    }
}
