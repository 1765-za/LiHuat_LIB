using RimWorld;
using Verse;

namespace LihuatLib.AstralArts
{
    internal static class AstralBuffStats
    {
        internal static void Apply(StatDef stat, StatRequest request, ref float value)
        {
            string name = stat.defName;
            bool weapon = name == "RangedWeapon_DamageMultiplier" || name == "RangedWeapon_Cooldown";
            if (!weapon && name != "MoveSpeed" && name != "MeleeDamageFactor" && name != "MeleeCooldownFactor" && name != "AimingDelayFactor") return;
            Pawn pawn = weapon ? (request.Thing?.ParentHolder as Pawn_EquipmentTracker)?.pawn : request.Thing as Pawn;
            if (pawn == null || Core.DeveloperExecution.Active) return;
            AstralSpellRuntime runtime = LihuatGame.Current?.AstralArts.SpellRuntime;
            if (runtime == null) return;
            if (name == "MoveSpeed") value *= runtime.Active(pawn, AstralSpellKind.Haste)?.MoveFactor ?? 1f;
            else
            {
                AstralTimedEffect rally = runtime.Active(pawn, AstralSpellKind.Rally);
                if (rally != null) value *= name == "MeleeDamageFactor" || name == "RangedWeapon_DamageMultiplier" ? rally.DamageFactor : rally.AimFactor;
            }
        }
    }
}
