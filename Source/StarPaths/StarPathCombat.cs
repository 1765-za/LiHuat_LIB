using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace LihuatLib.StarPaths
{
    internal static class StarPathCombat
    {
        private static readonly AccessTools.StructFieldRef<DamageInfo, float> penetration = AccessTools.StructFieldRefAccess<DamageInfo, float>("armorPenetrationInt");
        internal static StarPathAttackScope Begin(Thing target, DamageInfo info)
        {
            if (!(target is Pawn pawn) || !(info.Instigator is Pawn attacker) || attacker == pawn
                || !info.Def.harmsHealth || info.Def.consideredHelpful || !float.IsFinite(info.Amount) || info.Amount <= 0f
                || StarPathDamageScope.For(target) != null || StarPathAttackScope.For(target) != null
                || OperationScope.MedicalFor(pawn) || OperationScope.ControlsDamage(pawn)) return null;
            StarPathService service = LihuatGame.Current?.StarPaths;
            return service?.Converts(attacker) == true ? new StarPathAttackScope(attacker, pawn, service.Split(attacker, info.Amount), info.Amount) : null;
        }

        internal static DamageWorker.DamageResult Apply(DamageWorker worker, DamageInfo info, Thing target)
        {
            if (StarPathDamageScope.For(target) != null) return worker.Apply(info, target);
            StarPathService service = LihuatGame.Current?.StarPaths;
            float penetrationFactor = info.Def.harmsHealth && !info.Def.consideredHelpful ? service?.PenetrationFactor(info.Instigator as Pawn) ?? 1f : 1f;
            if (penetrationFactor != 1f && info.ArmorPenetrationInt >= 0f) penetration(ref info) *= penetrationFactor;
            StarPathAttackScope attack = StarPathAttackScope.For(target);
            if (attack == null)
            {
                if (penetrationFactor == 1f) return DamageEffectDispatcher.Ordinary(worker, info, target);
                using (new StarPathDamageScope(target, DamageChannel.Ordinary, info.Amount)) return DamageEffectDispatcher.Ordinary(worker, info, target);
            }
            var result = new DamageWorker.DamageResult();
            if (attack.OrdinaryFraction > 0f && info.Amount > 0f)
            {
                DamageInfo ordinary = info; ordinary.SetAmount(info.Amount * attack.OrdinaryFraction);
                using (new StarPathDamageScope(target, DamageChannel.Ordinary, ordinary.Amount)) result = DamageEffectDispatcher.Ordinary(worker, ordinary, target);
            }
            ApplyChannel(service, attack.Attacker, attack.Target, info, attack.Split.Advanced, DamageChannel.Advanced, result);
            ApplyChannel(service, attack.Attacker, attack.Target, info, attack.Split.Specialized, DamageChannel.Specialized, result);
            ApplyChannel(service, attack.Attacker, attack.Target, info, attack.Split.True, DamageChannel.True, result);
            return result;
        }

        private static void ApplyChannel(StarPathService service, Pawn attacker, Pawn pawn, DamageInfo source,
            float amount, DamageChannel channel, DamageWorker.DamageResult result)
        {
            if (amount <= 0f || pawn.Dead || pawn.Destroyed) return;
            using (new StarPathDamageScope(pawn, channel, amount))
            {
                service.Find(attacker)?.Outgoing?.Invoke(attacker, pawn, channel, ref amount);
                if (amount <= 0f || pawn.Dead || pawn.Destroyed) return;
                float coefficient = service.IncomingFactor(pawn, channel);
                if (coefficient <= 0f) return;
                BodyPartRecord part = source.HitPart ?? result.LastHitPart;
                if (part == null || pawn.health.hediffSet.PartIsMissing(part))
                    part = pawn.health.hediffSet.GetRandomNotMissingPart(source.Def, source.Height, source.Depth);
                if (part == null) return;
                float before = pawn.health.hediffSet.GetPartHealth(part);
                var injury = (Hediff_Injury)HediffMaker.MakeHediff(HediffDefOf.Cut, pawn, part);
                injury.Severity = (float)Math.Min(float.MaxValue, amount * (double)coefficient);
                DamageInfo info = source; info.SetAmount(amount); info.SetHitPart(part);
                pawn.health.AddHediff(injury, part, info, result);
                float added = Math.Max(0f, before - pawn.health.hediffSet.GetPartHealth(part));
                if (added <= 0f) return;
                result.totalDamageDealt += added;
                result.wounded = true; result.deflected = false; result.AddPart(pawn, part); result.AddHediff(injury);
            }
        }

        internal static void Incoming(Hediff_Injury injury, float reduction)
        { if (reduction > 0f) injury.Severity *= 1f - reduction; }

        internal static bool GuaranteedHit(Pawn pawn) => pawn != null && LihuatGame.Current?.StarPaths?.GuaranteedHit(pawn) == true;
    }
}
