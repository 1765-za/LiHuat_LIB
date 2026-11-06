using System.Collections.Generic;
using LihuatLib.StarPaths;
using RimWorld;
using UnityEngine;
using Verse;

namespace LihuatLib.Presentation
{
    [StaticConstructorOnStartup]
    internal static class StarPathGizmos
    {
        private static readonly Texture2D icon = ContentFinder<Texture2D>.Get("Lihuat/AstralArts/Arts");
        internal static IEnumerable<Gizmo> Add(Pawn pawn, IEnumerable<Gizmo> original)
        {
            if (original != null) foreach (Gizmo gizmo in original) yield return gizmo;
            StarPathSubject subject = LihuatGame.Current?.StarPaths.Find(pawn);
            if (subject == null || pawn.Dead || pawn.Faction != Faction.OfPlayer) yield break;
            yield return new Command_Action
            {
                defaultLabel = "Lihuat_StarPathTitle".Translate(), icon = icon, Order = -98.5f, groupable = false,
                defaultDesc = "Lihuat_StarPathCommand".Translate(subject.Account.Level, subject.Account.AvailablePoints),
                action = () => Find.WindowStack.Add(new StarPathWindow(subject))
            };
        }
    }
}
