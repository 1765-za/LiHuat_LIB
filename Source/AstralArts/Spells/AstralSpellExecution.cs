using System.Linq;
using Verse;

namespace LihuatLib.AstralArts
{
    internal static class AstralSpellExecution
    {
        internal static void Apply(AstralArtsService service, AstralEntry entry, AstralSpellDef spell, AstralSpellRank rank, LocalTargetInfo target)
        {
            Pawn caster = entry.Pawn;
            int now = Find.TickManager.TicksGame;
            switch (spell.kind)
            {
                case AstralSpellKind.Warp:
                    Presentation.AstralSpellEffects.Depart(caster);
                    caster.Position = target.Cell; caster.Notify_Teleported(); caster.stances.CancelBusyStanceHard();
                    break;
                case AstralSpellKind.Haste:
                case AstralSpellKind.Bastion:
                    service.SpellRuntime.Add(caster, caster, spell.kind, rank, now); break;
                case AstralSpellKind.Rally:
                    foreach (Pawn pawn in AstralSpellTargeting.Pawns(caster, caster.Position, rank.radius, false).ToArray())
                    {
                        service.SpellRuntime.Add(caster, pawn, spell.kind, rank, now);
                        Presentation.AstralSpellEffects.Bless(pawn, spell.kind, rank.duration);
                    }
                    break;
                case AstralSpellKind.Barrier:
                case AstralSpellKind.Rejuvenate:
                    service.SpellRuntime.Add(caster, target.Pawn, spell.kind, rank, now); break;
                case AstralSpellKind.Bolt:
                case AstralSpellKind.Nova:
                case AstralSpellKind.Lance:
                    AstralSpellOffense.Apply(service, caster, spell.kind, rank, target); break;
                case AstralSpellKind.Stasis:
                    foreach (Pawn pawn in AstralSpellTargeting.Pawns(caster, target.Cell, rank.radius, true).ToArray())
                        pawn.stances?.stunner.StunFor((int)(rank.duration * 60f), caster);
                    break;
                case AstralSpellKind.Mend:
                    AstralSpellHealing.Heal(caster, rank.power); break;
                case AstralSpellKind.Rain:
                    foreach (Pawn pawn in AstralSpellTargeting.Pawns(caster, target.Cell, rank.radius, false).ToArray())
                        if (AstralSpellHealing.Heal(pawn, rank.power) > 0f) Presentation.AstralSpellEffects.Heal(pawn);
                    break;
            }
        }
    }
}
