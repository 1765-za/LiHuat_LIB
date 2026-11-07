using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld.Planet;
using Verse;

namespace LihuatLib.Mechanisms
{
    public static class ErasureBody
    {
        private static readonly AccessTools.FieldRef<Thing, sbyte> mapIndex = AccessTools.FieldRefAccess<Thing, sbyte>("mapIndexOrState");
        private static readonly Action<Pawn> commit = BuildCommit();
        public static long Verified { get; private set; }
        public static long Incomplete { get; private set; }
        public static string LastFailure { get; private set; }

        private static Action<Pawn> BuildCommit()
        {
            var method = new DynamicMethod("LihuatErase_" + Guid.NewGuid().ToString("N"), typeof(void), new[] { typeof(Pawn) }, typeof(Pawn).Module, true);
            ILGenerator il = method.GetILGenerator();
            Label noHealth = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, AccessTools.Field(typeof(Pawn), nameof(Pawn.health)));
            il.Emit(OpCodes.Brfalse, noHealth);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, AccessTools.Field(typeof(Pawn), nameof(Pawn.health)));
            il.Emit(OpCodes.Ldc_I4, (int)PawnHealthState.Dead); il.Emit(OpCodes.Stfld, AccessTools.Field(typeof(Pawn_HealthTracker), "healthState"));
            il.MarkLabel(noHealth);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Stfld, AccessTools.Field(typeof(Pawn), nameof(Pawn.markedForDiscard)));
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldc_I4, -3); il.Emit(OpCodes.Stfld, AccessTools.Field(typeof(Thing), "mapIndexOrState"));
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Stfld, AccessTools.Field(typeof(Thing), nameof(Thing.holdingOwner)));
            il.Emit(OpCodes.Ret);
            return (Action<Pawn>)method.CreateDelegate(typeof(Action<Pawn>));
        }

        public static bool Apply(Pawn pawn) => Remove(pawn, true);

        public static bool Withdraw(Pawn pawn) => Remove(pawn, false);

        private static bool Remove(Pawn pawn, bool stopObservers)
        {
            if (pawn == null) return false;
            if (LihuatGame.Current?.Lifecycle.Block(pawn, DestructiveEntry.Destroy) == true) return false;
            using (new OperationScope(OperationKind.TerminalDeath, pawn))
            {
                try { return ApplyBody(pawn, stopObservers); }
                catch (Exception e) { LastFailure = e.GetType().Name + ": " + e.Message; Incomplete++; return false; }
            }
        }

        private static bool ApplyBody(Pawn pawn, bool stopObservers)
        {
            LastFailure = null;
            ThingOwner container = pawn.holdingOwner;
            Corpse corpse = container?.Owner as Corpse;
            CellRect footprint = MapRegistrationStorage.Footprint(pawn);
            LihuatGame.Current?.Admission.Forget(pawn);
            if (stopObservers) ErasureObservers.Stop(pawn);
            ErasureRelations.Remove(pawn);
            ErasureCorpse.Retire(corpse);
            ErasureDeparture.Remove(pawn);
            RegistrationStorage.Process(pawn, container, footprint, true);
            commit(pawn);
            Find.ColonistBar?.MarkColonistsDirty();
            bool result = (PawnLife.Dead(pawn) || !stopObservers && pawn.health == null)
                && mapIndex(pawn) == -3 && pawn.markedForDiscard && pawn.holdingOwner == null
                && LihuatGame.Current?.Admission.Contains(pawn) != true
                && !RegistrationStorage.Process(pawn, container, footprint, false);
            if (result) Verified++;
            else { LastFailure = "Terminal state or registration retained."; Incomplete++; }
            return result;
        }
    }
}
