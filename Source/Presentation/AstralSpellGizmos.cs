using System.Collections.Generic;
using LihuatLib.AstralArts;
using RimWorld;
using UnityEngine;
using Verse;

namespace LihuatLib.Presentation
{
    [StaticConstructorOnStartup]
    internal static class AstralSpellGizmos
    {
        private static readonly Dictionary<AstralSpellDef, Texture2D> icons = BuildIcons();
        private static Dictionary<AstralSpellDef, Texture2D> BuildIcons()
        {
            var result = new Dictionary<AstralSpellDef, Texture2D>();
            foreach (AstralSpellDef spell in DefDatabase<AstralSpellDef>.AllDefsListForReading)
                result.Add(spell, ContentFinder<Texture2D>.Get(spell.iconPath));
            return result;
        }
        internal static IEnumerable<Gizmo> Add(AstralArtsService service, AstralEntry entry)
        {
            foreach (AstralSpellDef spell in service.LearnedSpells(entry))
            {
                var command = new Command_Action
                {
                    defaultLabel = spell.Title, defaultDesc = AstralSpellDescriptions.Command(entry, spell), icon = icons[spell],
                    Order = -97.5f + (int)spell.kind * .01f, groupable = false,
                    action = () => { if (spell.target == AstralSpellTarget.Self) Cast(entry, spell, entry.Pawn); else AstralSpellSelection.Begin(entry, spell); }
                };
                string failure = service.SpellReadyFailure(entry, spell);
                if (failure != null) command.Disable(failure.Translate());
                yield return command;
            }
        }
        internal static void Cast(AstralEntry entry, AstralSpellDef spell, LocalTargetInfo target)
        {
            AstralArtsService service = LihuatGame.Current.AstralArts;
            string failure = service.SpellCastFailure(entry, spell, target);
            if (failure != null) Messages.Message(failure.Translate(), MessageTypeDefOf.RejectInput, false);
            else service.TryCastSpell(entry, spell, target);
        }
    }
}
