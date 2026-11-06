using System;
using System.Collections.Generic;
using Verse;

namespace LihuatLib.AstralArts
{
    internal sealed class AstralSpellRuntime : IExposable
    {
        private List<AstralTimedEffect> effects = new();
        private readonly Dictionary<(Pawn, AstralSpellKind), AstralTimedEffect> byPawn = new();
        private readonly Dictionary<string, int> byCaster = new(StringComparer.Ordinal);
        private readonly PriorityQueue<(AstralTimedEffect Effect, int Revision), int> events = new();
        internal bool HasEffects(Pawn caster) => byCaster.ContainsKey(caster.GetUniqueLoadID());
        internal AstralTimedEffect Active(Pawn pawn, AstralSpellKind kind)
            => Configuration.HighMechanismPolicy.AstralArts && pawn != null && byPawn.TryGetValue((pawn, kind), out var effect)
                && effect.Until > Find.TickManager.TicksGame ? effect : null;
        internal float Reduction(Pawn pawn) => Active(pawn, AstralSpellKind.Bastion)?.Reduction ?? 0f;
        internal float DamageFactor(Pawn pawn) => Active(pawn, AstralSpellKind.Rally)?.DamageFactor ?? 1f;

        internal void Add(Pawn caster, Pawn pawn, AstralSpellKind kind, AstralSpellRank rank, int now)
        {
            string casterId = caster.GetUniqueLoadID();
            if (byPawn.TryGetValue((pawn, kind), out var effect) && effect.Until <= now) { Remove(effect); effect = null; }
            if (effect == null)
            {
                effect = new AstralTimedEffect { Pawn = pawn, CasterId = casterId, Kind = kind };
                effects.Add(effect); byPawn.Add((pawn, kind), effect); AddCaster(casterId);
            }
            else if (effect.CasterId != casterId) { RemoveCaster(effect.CasterId); effect.CasterId = casterId; AddCaster(casterId); }
            effect.Power = Math.Max(effect.Power, rank.power); effect.MoveFactor = Math.Max(effect.MoveFactor, rank.moveFactor);
            effect.DamageFactor = Math.Max(effect.DamageFactor, rank.damageFactor); effect.AimFactor = Math.Min(effect.AimFactor, rank.aimFactor);
            effect.Reduction = Math.Max(effect.Reduction, rank.reduction);
            effect.Until = Math.Max(effect.Until, checked(now + (int)Math.Ceiling(rank.duration * 60f)));
            if (kind == AstralSpellKind.Rejuvenate && effect.NextPulse <= now) effect.NextPulse = now + 60;
            effect.Revision++; Schedule(effect); AstralEnhancementStats.Invalidate(pawn);
        }
        private void AddCaster(string caster)
        { byCaster.TryGetValue(caster, out int count); byCaster[caster] = count + 1; }
        private void RemoveCaster(string caster) { if (--byCaster[caster] == 0) byCaster.Remove(caster); }
        private void Schedule(AstralTimedEffect effect)
            => events.Enqueue((effect, effect.Revision), effect.Kind == AstralSpellKind.Rejuvenate ? Math.Min(effect.NextPulse, effect.Until) : effect.Until);

        internal void Tick(int now)
        {
            while (events.TryPeek(out _, out int at) && at <= now)
            {
                var (effect, revision) = events.Dequeue();
                if (effect.Revision != revision || !byPawn.TryGetValue((effect.Pawn, effect.Kind), out var current) || current != effect) continue;
                if (effect.Kind == AstralSpellKind.Rejuvenate && effect.NextPulse <= effect.Until && effect.NextPulse <= now
                    && !effect.Pawn.Dead && !effect.Pawn.Destroyed)
                {
                    AstralSpellHealing.Heal(effect.Pawn, effect.Power);
                    Presentation.AstralSpellEffects.Heal(effect.Pawn);
                    effect.NextPulse += 60;
                }
                if (effect.Until <= now || effect.Pawn.Dead || effect.Pawn.Destroyed) Remove(effect);
                else Schedule(effect);
            }
        }
        internal float Absorb(Pawn pawn, float damage)
        {
            AstralTimedEffect effect = Active(pawn, AstralSpellKind.Barrier);
            if (effect == null) return damage;
            float absorbed = Math.Min(damage, effect.Power); effect.Power -= absorbed;
            Presentation.AstralSpellEffects.Absorb(pawn, effect.Power <= 0f);
            if (effect.Power <= 0f) Remove(effect);
            return damage - absorbed;
        }
        private void Remove(AstralTimedEffect effect)
        {
            effects.Remove(effect); byPawn.Remove((effect.Pawn, effect.Kind)); effect.Revision++;
            RemoveCaster(effect.CasterId);
            AstralEnhancementStats.Invalidate(effect.Pawn);
        }
        public void ExposeData()
        {
            Scribe_Collections.Look(ref effects, "astralSpellEffects", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.LoadingVars) effects ??= new();
            if (Scribe.mode != LoadSaveMode.PostLoadInit) return;
            byPawn.Clear(); byCaster.Clear(); events.Clear();
            effects.RemoveAll(effect => effect.Pawn == null || effect.Pawn.Dead || effect.Pawn.Destroyed || effect.Until <= Find.TickManager.TicksGame);
            foreach (AstralTimedEffect effect in effects)
            { byPawn.Add((effect.Pawn, effect.Kind), effect); AddCaster(effect.CasterId); Schedule(effect); }
        }
    }
}
