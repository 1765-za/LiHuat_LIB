using System;
using System.Collections.Generic;
using Verse;

namespace LihuatLib.Domains
{
    public sealed class DomainPressureMemory
    {
        public readonly string SourceId;
        public readonly DomainProfileDef Profile;
        public readonly DomainPressure Pressure;
        public DomainPressureMemory(string sourceId, DomainProfileDef profile, DomainPressure pressure)
        {
            SourceId = sourceId ?? throw new ArgumentNullException(nameof(sourceId));
            Profile = profile ?? throw new ArgumentNullException(nameof(profile));
            Pressure = pressure;
        }
    }

    public sealed partial class DomainCoordinator
    {
        private readonly Dictionary<Pawn, List<DomainPressureMemory>> pressureMemory = new Dictionary<Pawn, List<DomainPressureMemory>>(Core.ReferenceIdentity<Pawn>.Instance);
        public event Action<Pawn> PositionChanged;

        public void RetainPressure(Pawn pawn, DomainPressureMemory memory)
        {
            if (pawn == null || memory == null) return;
            if (!pressureMemory.TryGetValue(pawn, out List<DomainPressureMemory> entries))
                pressureMemory.Add(pawn, entries = new List<DomainPressureMemory>(1));
            for (int i = entries.Count - 1; i >= 0; i--)
                if (entries[i].SourceId == memory.SourceId) entries.RemoveAt(i);
            entries.Add(memory);
            Refresh(pawn, false);
        }

        public void ReleasePressure(Pawn pawn, DomainPressureMemory memory)
        {
            if (pawn == null || memory == null || !pressureMemory.TryGetValue(pawn, out List<DomainPressureMemory> entries)
                || !entries.Remove(memory)) return;
            if (entries.Count == 0) pressureMemory.Remove(pawn);
            Refresh(pawn, false);
        }

        private float RememberedStrength(DomainPressureMemory memory, ref PressureQuery query)
        {
            if (memory.Profile.kind == DomainKind.Closed && query.IsAmplifying()) return 0f;
            DomainArea own = query.GetCoveredOwner(this);
            float resistance = 0f;
            if (own != null && own.Id != memory.SourceId)
                resistance = own.Profile.Formal && memory.Profile.Formal ? 1f
                    : Math.Min(1f, own.Profile.internalStrength / memory.Profile.internalStrength);
            return memory.Profile.suppression * (1f - resistance);
        }

        private struct RememberedPressure
        {
            internal float Strength;
            internal int Count;
            internal DomainPressure Mask;
        }

        private RememberedPressure ReadRemembered(ref PressureQuery query, AreaCandidates candidates, DomainPressure rule)
        {
            RememberedPressure result = default;
            Pawn pawn = query.Pawn;
            if (pawn == null || pressureMemory.Count == 0 || !pressureMemory.TryGetValue(pawn, out List<DomainPressureMemory> entries)) return result;
            foreach (DomainPressureMemory memory in entries)
            {
                DomainArea live = GetArea(memory.SourceId);
                if (live != null && !live.Ending && !live.Closed && candidates.Contains(live) && Covers(live, pawn)) continue;
                float strength = RememberedStrength(memory, ref query);
                if (strength <= 0f) continue;
                result.Count++;
                result.Mask |= memory.Pressure;
                if ((memory.Pressure & rule) != 0) result.Strength = 1f - (1f - result.Strength) * (1f - strength);
            }
            return result;
        }
    }
}
