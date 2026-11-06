using LihuatLib.Core;
using Verse;

namespace LihuatLib.AstralArts
{
    internal static class AstralCombat
    {
        internal static void Incoming(Hediff_Injury injury, float reduction)
        {
            if (reduction > 0f && AstralAccount.PositiveFinite(injury.Severity)) injury.Severity *= 1f - reduction;
        }
    }
}
