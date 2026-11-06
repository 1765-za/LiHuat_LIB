using System.Collections.Generic;
using Verse;

namespace LihuatLib.StarBlood
{
    internal sealed class StarBloodRoster
    {
        private readonly StarBloodOwners owners = new StarBloodOwners();
        private readonly List<StarBloodRules> engines = new List<StarBloodRules>();
        internal bool HasSubjects => engines.Count != 0;
        internal StarBloodRules Find(object target) => target != null && HasSubjects && owners.TryGetValue(target, out StarBloodRules engine) ? engine : null;

        internal void Register(StarBloodRules engine)
        {
            Pawn pawn = engine.Subject.Pawn;
            StarBloodRules previous = Find(pawn);
            if (previous != null) Remove(previous);
            engines.Add(engine);
            owners.Add(pawn, engine);
            owners.Add(engine.Anchor.Health, engine);
            owners.Add(engine.Anchor.Hediffs, engine);
            foreach (object owned in engine.Anchor.OwnedStructures)
                if (!owners.TryGetValue(owned, out _)) owners.Add(owned, engine);
        }
        internal void Remove(StarBloodRules engine)
        {
            if (!engines.Remove(engine)) return;
            owners.Remove(engine.Subject.Pawn);
            owners.Remove(engine.Anchor.Health);
            owners.Remove(engine.Anchor.Hediffs);
            foreach (object owned in engine.Anchor.OwnedStructures) owners.Remove(owned);
        }
        internal void Update()
        {
            for (int i = engines.Count - 1; i >= 0; i--) engines[i].Update();
        }
        internal void AcceptTransfers()
        {
            foreach (StarBloodRules engine in engines) engine.Anchor.AcceptLocation();
        }
        internal void AcceptDeveloperChanges()
        {
            foreach (StarBloodRules engine in engines.ToArray())
            {
                StarBloodSubject subject = engine.Subject;
                if (subject == null) continue;
                Pawn pawn = subject.Pawn;
                if (PawnLife.Dead(pawn) || pawn.Destroyed || pawn.health?.hediffSet == null)
                { subject.State.DeveloperDeathPawnId = pawn.ThingID; continue; }
                subject.State.DeveloperRemovedMarker = !pawn.health.hediffSet.HasHediff(subject.MarkerDef);
                Mechanisms.PinnedHediffs.AcceptDeveloperChanges(pawn);
                engine.AcceptRestoration();
            }
        }
    }
}
