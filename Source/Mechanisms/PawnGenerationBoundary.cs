using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using LihuatLib.Core;
using RimWorld;
using Verse;

namespace LihuatLib.Mechanisms
{
    internal static class PawnGenerationBoundary
    {
        internal static long CancelledIncidents { get; private set; }

        internal static void Install()
        {
            var before = AccessTools.Method(typeof(PawnGenerationBoundary), nameof(BeforeGeneration));
            NativeIlGuards.Guard(AccessTools.Method(typeof(PawnGenerator), "GenerateNewPawnInternal"), before);
            NativeIlGuards.Guard(AccessTools.Method(typeof(PawnGenerator), "TryGenerateNewPawnInternal"), before);
            NativeIlGuards.Guard(AccessTools.Method(typeof(PawnGroupKindWorker), nameof(PawnGroupKindWorker.GeneratePawns),
                new[] { typeof(PawnGroupMakerParms), typeof(PawnGroupMaker), typeof(bool) }), before);
            NativeIlGuards.Guard(AccessTools.Method(typeof(ThingMaker), nameof(ThingMaker.MakeThing)),
                AccessTools.Method(typeof(PawnGenerationBoundary), nameof(BeforeThing)), 0);
            var allows = AccessTools.Method(typeof(PawnGenerationBoundary), nameof(AllowsGeneration));
            NativeIlGuards.Guard(AccessTools.Method(typeof(Hediff_Pregnant), nameof(Hediff_Pregnant.DoBirthSpawn)), allows);
            NativeIlGuards.Guard(AccessTools.Method(typeof(PregnancyUtility), nameof(PregnancyUtility.ApplyBirthOutcome)), allows);
            NativeIlGuards.Guard(AccessTools.Method(typeof(WildAnimalSpawner), nameof(WildAnimalSpawner.SpawnRandomWildAnimalAt)),
                AccessTools.Method(typeof(PawnGenerationBoundary), nameof(AdmitAttempt)));
            MethodInfo incident = AccessTools.Method(typeof(IncidentWorker), nameof(IncidentWorker.TryExecute));
            NativeIlGuards.RegisterTransform(incident, typeof(PawnGenerationBoundary).FullName, NativeIlGuards.ScopeOrder,
                body => CatchCancellation(incident, body));
            FieldWriteProtection.Register(AccessTools.Method(typeof(PawnAdmissionWrites), nameof(PawnAdmissionWrites.Allows)));
        }

        private static bool AllowsGeneration() => LihuatGame.Current?.Lifecycle.AllowsGeneration() != false;
        private static bool AdmitAttempt() => LihuatGame.Current?.Regeneration.TryAdmitGeneration() != false;
        private static void BeforeGeneration() => LihuatGame.Current?.Regeneration.BeforeGeneration();
        private static void BeforeThing(ThingDef def)
        {
            if (typeof(Pawn).IsAssignableFrom(def.thingClass)) BeforeGeneration();
        }
        private static void Cancelled() => CancelledIncidents++;

        private static DynamicMethod CatchCancellation(MethodInfo original, MethodInfo continuation)
        {
            DynamicMethod wrapper = IndependentIl.New(original);
            wrapper.InitLocals = true;
            ILGenerator il = wrapper.GetILGenerator();
            LocalBuilder result = il.DeclareLocal(typeof(bool));
            il.BeginExceptionBlock();
            for (int i = 0; i < IndependentIl.Parameters(original).Length; i++) il.Emit(OpCodes.Ldarg, (short)i);
            il.Emit(OpCodes.Call, continuation);
            il.Emit(OpCodes.Stloc, result);
            il.BeginCatchBlock(typeof(RegenerationDeniedException));
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Call, GeneratedIntegrity.Create(AccessTools.Method(typeof(PawnGenerationBoundary), nameof(Cancelled))));
            il.EndExceptionBlock();
            il.Emit(OpCodes.Ldloc, result);
            il.Emit(OpCodes.Ret);
            return wrapper;
        }
    }
}
