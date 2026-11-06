using LihuatLib.Dispatching;
using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;
using LihuatLib.Domains;

namespace LihuatLib.Patches
{
    [DispatchTarget(typeof(OverlayDrawer), nameof(OverlayDrawer.DrawOverlay))]
    [DispatchOrder(400)]
    internal static class DomainShellOverlayPatch
    {
        static bool Prefix(Thing t) => !DomainShellRules.Conceals(t);
    }

    [DispatchTarget(typeof(GenUI), nameof(GenUI.ThingsUnderMouse))]
    [DispatchOrder(410)]
    internal static class DomainShellSelectionPatch
    {
        static void Postfix(List<Thing> __result)
        {
            for (int i = __result.Count - 1; i >= 0; i--)
                if (DomainShellRules.Conceals(__result[i])) __result.RemoveAt(i);
        }
    }

    [DispatchTarget(typeof(ThingOverlays), nameof(ThingOverlays.ThingOverlaysOnGUI))]
    [DispatchOrder(420)]
    internal static class DomainShellLabelPatch
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var source = AccessTools.Method(typeof(Thing), nameof(Thing.DrawGUIOverlay));
            var replacement = AccessTools.Method(typeof(DomainShellLabelPatch), nameof(DrawVisible));
            int replaced = 0;
            foreach (CodeInstruction line in instructions)
            {
                if (line.Calls(source)) { line.opcode = OpCodes.Call; line.operand = replacement; replaced++; }
                yield return line;
            }
            if (replaced != 1) throw new InvalidOperationException("Domain shell label call site is not unique.");
        }
        private static void DrawVisible(Thing thing)
        {
            if (!DomainShellRules.Conceals(thing)) thing.DrawGUIOverlay();
        }
    }
}
