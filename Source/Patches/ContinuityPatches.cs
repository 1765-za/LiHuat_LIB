using LihuatLib.Dispatching;
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using LihuatLib.StarBlood;
using Verse;

namespace LihuatLib.Patches
{
    [DispatchTarget]
    [DispatchOrder(140)]
    internal static class RegistrationRemovalPatch
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(MapPawns), nameof(MapPawns.DeRegisterPawn));
            yield return AccessTools.Method(typeof(TickManager), nameof(TickManager.DeRegisterAllTickabilityFor));
        }
        static bool Prefix(Thing __0) => LihuatGame.Current?.Domains.AllowsRegistrationRemoval(__0) != false && !PawnContinuity.BlocksRemoval(__0);
    }
    [DispatchTarget(typeof(ListerThings), nameof(ListerThings.Remove))]
    [DispatchOrder(150)]
    internal static class ListedPawnRemovalPatch
    {
        static bool Prefix(ListerThings __instance, Thing t)
        {
            if (LihuatGame.Current?.Domains.AllowsRegistrationRemoval(t) == false) return false;
            if (!(t is Pawn pawn) || PawnContinuity.Find(pawn)?.Map?.listerThings != __instance) return true;
            return !PawnContinuity.BlocksRemoval(t);
        }
    }
    [DispatchTarget(typeof(ThingGrid), nameof(ThingGrid.Deregister))]
    [DispatchOrder(160)]
    internal static class GriddedPawnRemovalPatch
    {
        static bool Prefix(Thing t) => LihuatGame.Current?.Domains.AllowsRegistrationRemoval(t, true) != false
            && !PawnContinuity.BlocksRemoval(t, movementAllowed: true);
    }
    [DispatchTarget]
    [DispatchOrder(170)]
    internal static class LivingCorpsePatch
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (MethodInfo method in AccessTools.GetDeclaredMethods(typeof(Pawn)))
                if (method.Name == nameof(Pawn.MakeCorpse)) yield return method;
        }
        static bool Prefix(Pawn __instance, ref Corpse __result)
        {
            if (!PawnContinuity.Protects(__instance)) return true;
            __result = null;
            return false;
        }
    }
    [DispatchTarget(typeof(Corpse), nameof(Corpse.InnerPawn), DispatchMember.Setter)]
    [DispatchOrder(180)]
    internal static class CorpseOccupantPatch
    {
        static bool Prefix(Pawn value) => !PawnContinuity.Protects(value);
    }
}
