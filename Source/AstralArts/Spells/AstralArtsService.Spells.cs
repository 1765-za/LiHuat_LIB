using System;
using System.Collections.Generic;
using LihuatLib.Core;
using RimWorld;
using Verse;

namespace LihuatLib.AstralArts
{
    public sealed partial class AstralArtsService
    {
        internal readonly AstralSpellRuntime SpellRuntime = new();
        public IReadOnlyList<AstralSpellDef> LearnedSpells(AstralEntry entry) => entry.SkillEffects.Spells;
        public float SpellReduction(Pawn pawn) => SpellRuntime.Reduction(pawn);
        public float SpellShield(Pawn pawn) => SpellRuntime.Active(pawn, AstralSpellKind.Barrier)?.Power ?? 0f;
        public void UseProviderSpellReduction(AstralEntry entry)
        { if (entry != null && Find(entry.Pawn) == entry) entry.ProviderSettlesSpells = true; }

        public string SpellReadyFailure(AstralEntry entry, AstralSpellDef spell)
        {
            if (entry == null || spell == null || !Available(entry) || !entry.Pawn.Spawned
                || entry.Pawn.Downed || entry.Pawn.InMentalState || entry.Pawn.jobs == null) return "Lihuat_AstralSpellUnavailable";
            AstralSkillNode skill = AstralSkillDefs.Lihuat_AstralSkills.Find(spell.node);
            AstralSpellRank rank = spell.Learned(entry);
            if (skill?.spell != spell || rank == null || !entry.Allows(skill.art)) return "Lihuat_AstralSpellUnlearned";
            if (entry.Spells.ReadyAt(spell) > Verse.Find.TickManager.TicksGame) return "Lihuat_AstralSpellCooling";
            return entry.Account.AvailableEnergy < rank.cost ? "Lihuat_AstralWaitEnergy" : null;
        }
        public string SpellCastFailure(AstralEntry entry, AstralSpellDef spell, LocalTargetInfo target)
        {
            string failure = SpellReadyFailure(entry, spell);
            return failure ?? AstralSpellTargeting.Failure(entry, spell, target);
        }
        public bool TryCastSpell(AstralEntry entry, AstralSpellDef spell, LocalTargetInfo target)
        {
            LibraryCapabilities.RequirePublicationThread();
            if (SpellCastFailure(entry, spell, target) != null) return false;
            if (spell.target == AstralSpellTarget.Self) target = entry.Pawn;
            AstralSpellRank rank = spell.Learned(entry);
            if (!entry.Account.TrySpend(rank.cost)) return false;
            entry.Spells.Start(spell, Verse.Find.TickManager.TicksGame, rank.cooldown);
            AstralSpellExecution.Apply(this, entry, spell, rank, target);
            Presentation.AstralSpellEffects.Cast(entry.Pawn, spell, rank, target);
            return true;
        }
        public string RelearnFailure(AstralEntry entry)
        {
            if (entry == null || !Available(entry) || entry.Pawn.Faction != Faction.OfPlayer || !entry.Pawn.Spawned
                || entry.Pawn.Downed || entry.Pawn.InMentalState) return "Lihuat_AstralSpellUnavailable";
            if (entry.Pawn.Drafted || entry.Spells.Cooling(Verse.Find.TickManager.TicksGame) || SpellRuntime.HasEffects(entry.Pawn))
                return "Lihuat_AstralRelearnBusy";
            return entry.Skills.SpentPoints == 0 ? "Lihuat_AstralRelearnEmpty" : null;
        }
        public bool RelearnSkills(AstralEntry entry)
        {
            RequireSkillEntry(entry);
            if (RelearnFailure(entry) != null) return false;
            entry.Skills.ClearInvestments(); RebuildSkills(entry); RefreshEnhancement(entry);
            return true;
        }
    }
}
