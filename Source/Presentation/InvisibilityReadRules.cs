using Verse;

namespace LihuatLib.Presentation
{
    internal static class InvisibilityReadRules
    {
        internal static bool Begin(HediffComp_Invisibility component, out byte state, ref bool visible)
        {
            state = 0;
            if (component.parent?.pawn?.mindState != null) return true;
            visible = true;
            return false;
        }
        internal static void End(byte state) { }
    }
}
