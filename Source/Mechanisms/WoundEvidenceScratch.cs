using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Verse;

namespace LihuatLib.Mechanisms
{
    internal sealed class WoundEvidenceScratch : IDisposable
    {
        private struct InjuryChain { internal int First, Last; }
        [ThreadStatic] private static WoundEvidenceScratch available;
        internal readonly List<Hediff_Injury> Injuries = new();
        internal readonly HashSet<BodyPartRecord> Missing = new();
        internal readonly HashSet<BodyPartTagDef> Tags = new();
        private readonly List<int> nextInjury = new();
        private readonly Dictionary<BodyPartRecord, InjuryChain> byPart = new();
        private bool indexed;

        internal void IndexInjuries()
        {
            indexed = Injuries.Count >= 8;
            if (!indexed) return;
            for (int i = 0; i < Injuries.Count; i++)
            {
                nextInjury.Add(-1);
                BodyPartRecord part = Injuries[i].Part;
                if (part == null) continue;
                ref InjuryChain chain = ref CollectionsMarshal.GetValueRefOrAddDefault(byPart, part, out bool exists);
                if (exists) nextInjury[chain.Last] = i;
                else chain.First = i;
                chain.Last = i;
            }
        }

        internal int FirstInjury(BodyPartRecord part) => indexed
            ? byPart.TryGetValue(part, out InjuryChain chain) ? chain.First : -1 : FindInjury(part, 0);

        internal int FollowingInjury(BodyPartRecord part, int index) => indexed ? nextInjury[index] : FindInjury(part, index + 1);

        private int FindInjury(BodyPartRecord part, int start)
        {
            for (int i = start; i < Injuries.Count; i++) if (Injuries[i].Part == part) return i;
            return -1;
        }

        internal static WoundEvidenceScratch Rent()
        {
            WoundEvidenceScratch result = available ?? new WoundEvidenceScratch();
            available = null;
            return result;
        }

        public void Dispose()
        {
            bool retain = Injuries.Capacity <= 512 && nextInjury.Capacity <= 512 && byPart.Count <= 512
                && Missing.Count <= 512 && Tags.Count <= 128;
            Injuries.Clear(); nextInjury.Clear(); byPart.Clear(); Missing.Clear(); Tags.Clear(); indexed = false;
            if (retain && available == null) available = this;
        }
    }
}
