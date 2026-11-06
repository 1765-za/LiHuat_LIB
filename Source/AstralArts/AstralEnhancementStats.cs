using System;
using System.Linq;
using RimWorld;
using Verse;

namespace LihuatLib.AstralArts
{
    [StaticConstructorOnStartup]
    internal static class AstralEnhancementStats
    {
        private enum Effect { None, Armor, Positive, Negative, WeaponPositive, WeaponNegative }
        private static readonly StatDef[] affected = DefDatabase<StatDef>.AllDefsListForReading.Where(s => Kind(s) != Effect.None).ToArray();

        private static Effect Kind(StatDef stat)
        {
            string name = stat?.defName;
            if (name?.StartsWith("ArmorRating_", StringComparison.Ordinal) == true) return Effect.Armor;
            switch (name)
            {
                case "MeleeDamageFactor": case "MeleeHitChance": case "MeleeDodgeChance":
                case "ShootingAccuracyPawn": case "MoveSpeed": case "MeleeArmorPenetrationFactor": return Effect.Positive;
                case "IncomingDamageFactor": case "MeleeCooldownFactor": case "AimingDelayFactor": return Effect.Negative;
                case "RangedWeapon_DamageMultiplier": case "RangedWeapon_ArmorPenetrationMultiplier": return Effect.WeaponPositive;
                case "RangedWeapon_Cooldown": return Effect.WeaponNegative;
                default: return Effect.None;
            }
        }

        internal static void Apply(StatDef stat, StatRequest request, ref float value)
        {
            Effect effect = Kind(stat);
            if (effect == Effect.None || DeveloperExecution.Active) return;
            Pawn pawn;
            if (effect == Effect.WeaponPositive || effect == Effect.WeaponNegative)
                pawn = (request.Thing?.ParentHolder as Pawn_EquipmentTracker)?.pawn;
            else pawn = request.Thing as Pawn;
            AstralEntry entry = LihuatGame.Current?.AstralArts.ActiveEnhancement(pawn);
            if (entry == null) return;
            value = effect == Effect.Armor ? value + 1.2f
                : value * (effect == Effect.Negative || effect == Effect.WeaponNegative
                    ? AstralEnhancement.IncomingFactor(entry) : AstralEnhancement.PositiveFactor(entry));
        }

        internal static void Invalidate(Pawn pawn)
        {
            if (pawn == null) return;
            foreach (StatDef stat in affected)
            {
                stat.Worker.ClearCacheForThing(pawn);
                var weapons = pawn.equipment?.AllEquipmentListForReading;
                if (weapons != null) foreach (ThingWithComps weapon in weapons) stat.Worker.ClearCacheForThing(weapon);
            }
        }
    }
}
