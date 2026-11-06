using System.Collections.Generic;
using Verse;

namespace LihuatLib.Core
{
    public sealed class StarBloodRecord : IExposable
    {
        public HashSet<string> InspectedSources = new HashSet<string>();
        public long Inspections;
        public long StrippedStructures;
        public long InjectionInspections;
        public long InjectedStructures;
        public long CounterExecutionsBlocked;
        public string DeveloperDeathPawnId;
        public bool DeveloperRemovedMarker;
        public void ExposeData()
        {
            Scribe_Collections.Look(ref InspectedSources, "inspectedSources", LookMode.Value);
            Scribe_Values.Look(ref Inspections, "inspections");
            Scribe_Values.Look(ref StrippedStructures, "strippedStructures");
            Scribe_Values.Look(ref InjectionInspections, "injectionInspections");
            Scribe_Values.Look(ref InjectedStructures, "injectedStructures");
            Scribe_Values.Look(ref CounterExecutionsBlocked, "counterExecutionsBlocked");
            Scribe_Values.Look(ref DeveloperDeathPawnId, "developerDeathPawnId");
            Scribe_Values.Look(ref DeveloperRemovedMarker, "developerRemovedMarker");
        }
    }
}
