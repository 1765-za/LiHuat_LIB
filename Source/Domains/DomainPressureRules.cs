using System;
using System.Linq;
using Verse;

namespace LihuatLib.Domains
{
    public sealed partial class DomainCoordinator
    {
        public bool IsImmune(Pawn pawn)
        {
            DomainArea own = OwnedBy(pawn);
            if (own == null || !Covers(own, pawn) || !own.Profile.Formal) return false;
            foreach (DomainArea area in Candidates(pawn)) if (area != own && !area.Ending && Covers(area, pawn)) return true;
            return false;
        }

        private float Strength(DomainArea area, ref PressureQuery query)
        {
            Pawn pawn = query.Pawn;
            if (area == null || area.Pressure == DomainPressure.None || area.Ending || area.Closed || !Covers(area, pawn)
                || area.Owner == pawn && !area.AffectsOwner) return 0f;
            if (area.Enclosed && query.IsAmplifying()) return 0f;
            DomainArea own = query.GetCoveredOwner(this);
            float resistance = 0f;
            if (own != null && own != area)
                resistance = own.Profile.Formal && area.Profile.Formal ? 1f
                    : Math.Min(1f, own.Profile.internalStrength / area.Profile.internalStrength);
            return area.Profile.suppression * (1f - resistance);
        }
        public bool ShouldSuppress(string id, Pawn pawn)
        {
            var query = new PressureQuery(pawn);
            if (Strength(GetArea(id), ref query) > 0f) return true;
            if (pawn != null && pressureMemory.TryGetValue(pawn, out var memories))
                foreach (DomainPressureMemory memory in memories)
                    if (memory.SourceId == id && RememberedStrength(memory, ref query) > 0f) return true;
            return false;
        }
        public int PressureCount(Pawn pawn)
        {
            if (pawn == null || byId.Count == 0 && pressureMemory.Count == 0) return 0;
            return ReadPressureCount(pawn);
        }
        private int ReadPressureCount(Pawn pawn)
        {
            var query = new PressureQuery(pawn);
            AreaCandidates candidates = Candidates(pawn);
            int count = 0;
            foreach (DomainArea area in candidates) if (Strength(area, ref query) > 0f) count++;
            return pressureMemory.Count == 0 ? count : count + ReadRemembered(ref query, candidates, DomainPressure.All).Count;
        }
        public float PressureStrength(Pawn pawn, DomainPressure rule)
        {
            if (pawn == null || byId.Count == 0 && pressureMemory.Count == 0) return 0f;
            return ReadPressureStrength(pawn, rule);
        }
        private float ReadPressureStrength(Pawn pawn, DomainPressure rule)
        {
            var query = new PressureQuery(pawn);
            AreaCandidates candidates = Candidates(pawn);
            float remaining = 1f;
            foreach (DomainArea area in candidates)
                if ((area.Pressure & rule) != 0) remaining *= 1f - Strength(area, ref query);
            return pressureMemory.Count == 0 ? 1f - remaining
                : 1f - remaining * (1f - ReadRemembered(ref query, candidates, rule).Strength);
        }
        public DomainPressure PressureOn(Pawn pawn)
        {
            if (pawn == null || byId.Count == 0 && pressureMemory.Count == 0) return DomainPressure.None;
            return ReadPressureMask(pawn);
        }
        private DomainPressure ReadPressureMask(Pawn pawn)
        {
            DomainPressure result = DomainPressure.None;
            var query = new PressureQuery(pawn);
            AreaCandidates candidates = Candidates(pawn);
            foreach (DomainArea area in candidates) if (Strength(area, ref query) > 0f) result |= area.Pressure;
            return pressureMemory.Count == 0 ? result : result | ReadRemembered(ref query, candidates, DomainPressure.All).Mask;
        }
        private PressureState ReadPressure(Pawn pawn)
        {
            PressureState result = default;
            if (byId.Count == 0 && pressureMemory.Count == 0) return result;
            var query = new PressureQuery(pawn);
            AreaCandidates candidates = Candidates(pawn);
            float remaining = 1f;
            foreach (DomainArea area in candidates)
            {
                float strength = Strength(area, ref query);
                if (strength > 0f) result.Mask |= area.Pressure;
                if ((area.Pressure & DomainPressure.Benefits) != 0) remaining *= 1f - strength;
            }
            RememberedPressure remembered = pressureMemory.Count == 0 ? default
                : ReadRemembered(ref query, candidates, DomainPressure.Benefits);
            result.Mask |= remembered.Mask;
            result.Strength = 1f - remaining * (1f - remembered.Strength);
            return result;
        }
        public bool Terminal(Pawn pawn)
        {
            foreach (DomainArea area in Candidates(pawn)) if (area.TerminalDeaths && Covers(area, pawn)) return true;
            return false;
        }
        public void Moved(Thing thing)
        {
            if (OperationScope.Transferring || !(thing is Pawn pawn)) return;
            RememberContained(pawn);
            DomainShellRules.Repel(pawn);
            DomainArea own = OwnedBy(pawn);
            if (own?.Profile.Movable == true)
            {
                MoveMobile(own);
            }
            PositionChanged?.Invoke(pawn);
            Refresh(pawn, false);
            if (pawn.carryTracker?.CarriedThing is Pawn carried)
            { PositionChanged?.Invoke(carried); Refresh(carried, false); }
        }
        public void RefreshAll()
        {
            foreach (Pawn pawn in AffectedPawns().Distinct(Core.ReferenceIdentity<Pawn>.Instance).ToArray()) Refresh(pawn, true);
        }
        public void RefreshPressure(Pawn pawn) => Refresh(pawn, false);
        private void Refresh(Pawn pawn, bool force)
        {
            if (pawn?.health == null) return;
            PressureState current = ReadPressure(pawn);
            observedPressure.TryGetValue(pawn, out DomainPressure previous);
            observedStrength.TryGetValue(pawn, out float priorStrength);
            if (!force && current.Mask == previous && current.Strength == priorStrength) return;
            if (current.Mask != DomainPressure.None) observedPressure[pawn] = current.Mask; else observedPressure.Remove(pawn);
            if (current.Strength > 0f) observedStrength[pawn] = current.Strength; else observedStrength.Remove(pawn);
            PressureChanged?.Invoke(pawn);
            pawn.health.hediffSet.DirtyCache();
            foreach (RimWorld.StatDef stat in DefDatabase<RimWorld.StatDef>.AllDefsListForReading) stat.Worker.ClearCacheForThing(pawn);
        }
    }
}
