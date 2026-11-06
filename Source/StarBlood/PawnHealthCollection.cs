using System.Collections.Generic;
using System.Runtime.InteropServices;
using Verse;

namespace LihuatLib.StarBlood
{
    internal sealed class PawnHealthCollection
    {
        private readonly Pawn pawn;
        private readonly HediffSet set;
        private List<Hediff> retained;
        internal PawnHealthCollection(Pawn pawn, HediffSet set) { this.pawn = pawn; this.set = set; retained = set.hediffs; }
        internal bool Intact
        {
            get
            {
                if (set.hediffs == null) return false;
                foreach (Hediff hediff in CollectionsMarshal.AsSpan(set.hediffs))
                {
                    if (hediff == null || hediff.pawn != pawn) return false;
                    if (hediff is HediffWithComps body && body.comps != null)
                        foreach (HediffComp comp in CollectionsMarshal.AsSpan(body.comps)) if (comp == null || comp.parent != body) return false;
                }
                return true;
            }
        }
        internal bool Restore()
        {
            bool changed = false;
            if (set.hediffs == null) { set.hediffs = retained; changed = true; }
            else retained = set.hediffs;
            for (int i = retained.Count - 1; i >= 0; i--)
            {
                Hediff hediff = retained[i];
                if (hediff == null) { retained.RemoveAt(i); changed = true; continue; }
                if (hediff.pawn != pawn) { hediff.pawn = pawn; changed = true; }
                if (!(hediff is HediffWithComps body) || body.comps == null) continue;
                for (int j = body.comps.Count - 1; j >= 0; j--)
                    if (body.comps[j] == null) { body.comps.RemoveAt(j); changed = true; }
                    else if (body.comps[j].parent != body) { body.comps[j].parent = body; changed = true; }
            }
            return changed;
        }
#if MAINTENANCE_LAB
        internal void AcceptCurrent() => retained = set.hediffs;
#endif
    }
}
