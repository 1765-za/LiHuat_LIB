using HarmonyLib;
using LihuatLib.Mechanisms;
using RimWorld;
using Verse;

namespace LihuatLib.StarBlood
{
    internal static class PawnTickBoundary
    {
        internal static void Install()
        {
            var tick = AccessTools.Method(typeof(Pawn), "Tick");
            var source = IndependentIl.Clone(tick, rewrite: TickRuntimeRecovery.IndependentRewrite(tick, 2, null));
            var participates = AccessTools.Method(typeof(PawnTickBoundary), nameof(Participates));
            NativeIlGuards.RegisterTransform(tick, typeof(PawnTickBoundary).FullName, NativeIlGuards.BodyOrder,
                body => IlBoundaryEmitter.Conditional(tick, body, source, participates, new[] { 0 }));
            NativeIlGuards.Guard(tick, AccessTools.Method(typeof(PawnTickBoundary), nameof(Prepare)), 0);
            NativeIlGuards.Guard(AccessTools.Method(typeof(Pawn_DraftController), nameof(Pawn_DraftController.DraftControllerTickInterval)),
                AccessTools.Method(typeof(RetainedDraftRules), nameof(RetainedDraftRules.AllowsTick)), 0);
        }
        private static bool Participates(Pawn pawn) => StarBloodRegistry.For(pawn) != null;
        private static void Prepare(Pawn pawn)
        {
            StarBloodRules engine = StarBloodRegistry.For(pawn);
            engine?.PrepareTick();
            if (engine?.Subject.AutomaticObservation == true) engine.Observe(pawn);
        }
    }
}
