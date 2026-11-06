using System;
using HarmonyLib;
using LihuatLib.Mechanisms;
using Verse;

namespace LihuatLib.Presentation
{
    internal static class RenderLifecycleBoundary
    {
        private static readonly AccessTools.FieldRef<DynamicDrawManager, bool> drawing =
            AccessTools.FieldRefAccess<DynamicDrawManager, bool>("drawingNow");
        internal static void Install()
        {
            NativeIlGuards.Guard(AccessTools.DeclaredMethod(typeof(Corpse), nameof(Corpse.DynamicDrawPhaseAt)),
                AccessTools.Method(typeof(RenderLifecycleBoundary), nameof(HasBody)), 0);
            NativeIlGuards.Guard(AccessTools.Method(typeof(DynamicDrawManager), nameof(DynamicDrawManager.DeRegisterDrawable)),
                AccessTools.Method(typeof(RenderLifecycleBoundary), nameof(AllowsRemoval)), 1);
            NativeIlGuards.Scoped(AccessTools.PropertyGetter(typeof(HediffComp_Invisibility), "ForcedVisible"),
                AccessTools.Method(typeof(InvisibilityReadRules), nameof(InvisibilityReadRules.Begin)), new[] { 0 },
                AccessTools.Method(typeof(InvisibilityReadRules), nameof(InvisibilityReadRules.End)), Array.Empty<int>());
            NativeIlGuards.Scoped(AccessTools.Method(typeof(DynamicDrawManager), nameof(DynamicDrawManager.DrawDynamicThings)),
                AccessTools.Method(typeof(RenderLifecycleBoundary), nameof(BeginDraw)), new[] { 0 },
                AccessTools.Method(typeof(RenderLifecycleBoundary), nameof(EndDraw)), new[] { 0 });
        }
        private static bool BeginDraw(DynamicDrawManager manager, out bool previous)
        { previous = drawing(manager); return true; }
        private static void EndDraw(DynamicDrawManager manager, bool previous) => drawing(manager) = previous;
        private static bool AllowsRemoval(Thing thing) => !StarBlood.PawnContinuity.BlocksRemoval(thing);
        private static bool HasBody(Corpse corpse) => corpse.InnerPawn != null;
    }
}
