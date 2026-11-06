using System;
using System.Reflection;
using HarmonyLib;
using RimWorld.Planet;
using RimWorld;
using Verse;

namespace LihuatLib.Mechanisms
{
    internal static class NativeAdmissionGuards
    {
        internal static void Install()
        {
            PawnGenerationBoundary.Install();
            MethodInfo gate = AccessTools.Method(typeof(NativeAdmissionGuards), nameof(Admit));
            NativeIlGuards.Scoped(AccessTools.Method(typeof(SkipUtility), nameof(SkipUtility.SkipTo)),
                AccessTools.Method(typeof(NativeAdmissionGuards), nameof(BeginSkip)), new[] { 0, 1, 2 },
                AccessTools.Method(typeof(NativeAdmissionGuards), nameof(EndSkip)), Array.Empty<int>());
            NativeIlGuards.Guard(AccessTools.Method(typeof(FleshbeastUtility), nameof(FleshbeastUtility.SpawnPawnAsFlyer)),
                AccessTools.Method(typeof(NativeAdmissionGuards), nameof(AdmitFlightSpawn)), 0, 1);
            MethodInfo spawn = AccessTools.Method(typeof(GenSpawn), nameof(GenSpawn.Spawn),
                new[] { typeof(Thing), typeof(IntVec3), typeof(Map), typeof(Rot4), typeof(WipeMode), typeof(bool), typeof(bool) });
            NativeIlGuards.RegisterLayer(spawn, typeof(SpawnCompletion).FullName, NativeIlGuards.BodyOrder,
                body => SpawnCompletionEmitter.CatchRejected(spawn, body), false);
            NativeIlGuards.Scoped(spawn,
                AccessTools.Method(typeof(NativeAdmissionGuards), nameof(BeginSpawn)), new[] { 0, 1, 2, 5 },
                AccessTools.Method(typeof(NativeAdmissionGuards), nameof(EndSpawn)), Array.Empty<int>());
            foreach (Type type in new[] { typeof(Thing), typeof(ThingWithComps), typeof(Pawn) })
            {
                MethodInfo setup = AccessTools.DeclaredMethod(type, nameof(Thing.SpawnSetup));
                NativeIlGuards.Guard(setup,
                    AccessTools.Method(typeof(NativeAdmissionGuards), nameof(AdmitSpawn)), 0, 1);
                NativeIlGuards.RegisterLayer(setup, typeof(SpawnCompletion).FullName, NativeIlGuards.ResultOrder,
                    body => SpawnCompletionEmitter.ValidateSetup(setup, body), false);
            }
            NativeIlGuards.Guard(AccessTools.Method(typeof(MapPawns), nameof(MapPawns.RegisterPawn)),
                AccessTools.Method(typeof(MapRegistrationAdmission), nameof(MapRegistrationAdmission.Pawn)), 0, 1);
            MethodInfo grid = AccessTools.Method(typeof(MapRegistrationAdmission), nameof(MapRegistrationAdmission.Grid));
            NativeIlGuards.Guard(AccessTools.Method(typeof(ThingGrid), nameof(ThingGrid.Register)), grid, 0, 1);
            NativeIlGuards.Guard(AccessTools.Method(typeof(ThingGrid), "RegisterInCell"), grid, 0, 1);
            NativeIlGuards.Guard(AccessTools.Method(typeof(ListerThings), nameof(ListerThings.Add)), gate, 1);
            NativeIlGuards.Guard(AccessTools.Method(typeof(DynamicDrawManager), nameof(DynamicDrawManager.RegisterDrawable)),
                AccessTools.Method(typeof(MapRegistrationAdmission), nameof(MapRegistrationAdmission.Draw)), 0, 1);
            NativeIlGuards.Guard(AccessTools.Method(typeof(TickManager), nameof(TickManager.RegisterAllTickabilityFor)), gate, 1);
            NativeIlGuards.Guard(AccessTools.Method(typeof(WorldPawns), nameof(WorldPawns.PassToWorld)),
                AccessTools.Method(typeof(NativeAdmissionGuards), nameof(AdmitWorld)), 1);
            NativeIlGuards.FilterArguments(AccessTools.Method(typeof(PawnGenerator), nameof(PawnGenerator.GeneratePawn), new[] { typeof(PawnGenerationRequest) }),
                AccessTools.Method(typeof(NativeAdmissionGuards), nameof(Generated)));
            NativeIlGuards.Guard(AccessTools.Method(typeof(PawnGenerator), nameof(PawnGenerator.GeneratePawn), new[] { typeof(PawnGenerationRequest) }),
                AccessTools.Method(typeof(NativeAdmissionGuards), nameof(BeforeGeneration)));
            MethodInfo pawnSetup = AccessTools.DeclaredMethod(typeof(Pawn), nameof(Pawn.SpawnSetup));
            NativeIlGuards.RegisterLayer(pawnSetup, typeof(NativeAdmissionGuards).FullName + ".SetupCompletion", NativeIlGuards.ScopeOrder,
                body => IlBoundaryEmitter.Scope(pawnSetup, body,
                    AccessTools.Method(typeof(NativeAdmissionGuards), nameof(BeginSetup)), new[] { 0 },
                    AccessTools.Method(typeof(NativeAdmissionGuards), nameof(EndSetup)), Array.Empty<int>()), false);
        }

        private static bool Admit(Thing thing) => !(thing is Pawn pawn)
            || LihuatGame.Current?.Lifecycle.AllowsRegistration(pawn) != false
                && (pawn.MapHeld == null || LihuatGame.Current?.Domains.AllowsSpawn(pawn, pawn.MapHeld) != false);
        private static bool AdmitSpawn(Thing thing, Map map)
        {
            if (LihuatGame.Current?.Domains.AllowsDestination(thing, map) == false) return false;
            return Admit(thing) && (thing is not Pawn pawn || LihuatGame.Current?.Domains.AllowsSpawn(pawn, map) != false);
        }
        private static bool BeginSpawn(Thing thing, IntVec3 cell, Map map, bool afterLoad, out SpawnCompletion.State state, ref Thing result)
        {
            state = default; result = thing;
            if (!AdmitSpawn(thing, map)) return false;
            state = SpawnCompletion.Begin(thing, map);
            state.Handoff = SubmapTransferScope.Begin(thing, cell, map, afterLoad);
            state.Handoff?.Detach();
            return true;
        }
        private static void EndSpawn(SpawnCompletion.State state) => SpawnCompletion.End(state);
        private static bool BeginSkip(Thing thing, IntVec3 cell, Map destination, out IDisposable scope, ref Thing result)
        {
            scope = null; result = thing;
            if (LihuatGame.Current?.Domains.AllowsDestination(thing, destination) == false
                || LihuatGame.Current?.Domains.AllowsCell(thing, cell) == false) return false;
            if (thing is Pawn participant && Integrations.AnomalyTransferIntegration.AllowsSkip(participant, destination))
            {
                scope = ContainerTransferScope.ToMap(participant, destination);
                return true;
            }
            scope = new MapTransferIntent(thing, destination);
            return !(thing is Pawn pawn) || !pawn.Spawned
                || LihuatGame.Current?.Lifecycle.Block(pawn, DestructiveEntry.DeSpawn) != true && !StarBlood.PawnContinuity.Protects(pawn);
        }
        private static void EndSkip(IDisposable scope) => scope?.Dispose();
        private static bool AdmitFlightSpawn(Pawn pawn, Map map)
        {
            if (LihuatGame.Current?.Domains.AllowsDestination(pawn, map) == false) return false;
            if (pawn == null || map == null || pawn.Spawned || OperationScope.Transferring || OperationScope.Terminates(pawn)) return true;
            return Admit(pawn) && LihuatGame.Current?.Domains.AllowsSpawn(pawn, map) != false;
        }
        private static bool AdmitWorld(Pawn pawn) => LihuatGame.Current?.Domains.AllowsWorld(pawn) != false && Admit(pawn);
        private static Pawn Generated(Pawn result) { if (result != null) LihuatGame.Current?.Lifecycle.Generated(result); return result; }
        private static void BeforeGeneration() => LihuatGame.Current?.Regeneration.BeforeGeneration();
        private static bool BeginSetup(Pawn pawn, out Pawn state) { state = pawn; return true; }
        private static void EndSetup(Pawn pawn) { if (pawn?.Spawned == true) LihuatGame.Current?.Lifecycle.Registered(pawn); }
    }
}
