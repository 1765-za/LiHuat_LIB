using System;
using System.Collections.Generic;
using LihuatLib.Mechanisms;
using Verse;

namespace LihuatLib.Domains
{
    public sealed partial class DomainCoordinator
    {
        private readonly Dictionary<string, Func<bool>> spawnPolicies = new Dictionary<string, Func<bool>>();

        public void RegisterSpawnPolicy(string provider, Func<bool> allowsSpawn)
        {
            LibraryCapabilities.Require(LibraryCapability.Domains);
            if (string.IsNullOrWhiteSpace(provider) || allowsSpawn == null) throw new ArgumentException("A domain spawn policy requires a provider and a predicate.");
            spawnPolicies[provider] = allowsSpawn;
        }
        internal void UnregisterSpawnPolicy(string provider, Func<bool> policy)
        {
            if (spawnPolicies.TryGetValue(provider, out var current) && ReferenceEquals(current, policy)) spawnPolicies.Remove(provider);
        }

        public bool AllowsSpawn(Pawn pawn, Map map)
        {
            if (DeveloperExecution.Active || Scribe.mode != LoadSaveMode.Inactive && Scribe.mode != LoadSaveMode.Saving) return true;
            if (pawn != null && byPawn.TryGetValue(pawn, out Encounter group))
                return map == group.Map || MapTransferIntent.AuthorizedDestination(pawn, map);
            if (map == null || !byMap.TryGetValue(map, out group)) return true;
            foreach (DomainArea area in group.Areas)
                if (area.Ending || !spawnPolicies.TryGetValue(area.ProviderId, out Func<bool> allows) || !allows()) return false;
            return true;
        }

        public void Spawned(Thing thing)
        {
            if (thing is Pawn pawn && pawn.Spawned && !byPawn.ContainsKey(pawn) && byMap.TryGetValue(pawn.Map, out Encounter group))
            {
                DomainArea area = group.Areas[0];
                DomainMember member = DomainTransit.Capture(pawn);
                member.Position = DomainCoordinates.OnSource(group.Areas, group.Map, member.Source, member.Position);
                member.Source = area.Source;
                member.Container = null;
                member.ContainerPath = null;
                member.Transferred = true;
                area.Members.Add(member);
                group.Members.Add(pawn);
                byPawn.Add(pawn, group);
                confinement.Add(pawn); RememberContained(pawn);
                foreach (DomainArea participant in group.Areas) participant.MemberSet.Add(pawn);
                Captured?.Invoke(pawn);
            }
            Moved(thing);
        }
    }
}
