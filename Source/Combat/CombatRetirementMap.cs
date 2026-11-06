using System;
using System.Collections.Generic;
using Verse;

namespace LihuatLib.Combat
{
    public sealed class CombatRetirementMap(Map map) : MapComponent(map)
    {
        private HashSet<string> identities = new();
        private readonly HashSet<string> sources = new();
        private static string Key(string source, string identity) => source.Length + ":" + source + identity;
        internal bool Contains(string source, string identity) => identities.Contains(Key(source, identity));
        internal bool HasSource(string source) => sources.Contains(source);
        internal void Record(string source, string identity) { identities.Add(Key(source, identity)); sources.Add(source); }
        public int Count => identities.Count;
        public override void FinalizeInit()
        {
            if (identities.Count > 0) LongEventHandler.ExecuteWhenFinished(() => ManagedCombatDiscovery.RestoreRetirements(map));
        }
        public override void ExposeData()
        {
            Scribe_Collections.Look(ref identities, "retiredCombatIdentities", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                identities ??= new(); sources.Clear();
                foreach (string key in identities)
                {
                    int separator = key.IndexOf(':');
                    int length = int.Parse(key.AsSpan(0, separator));
                    sources.Add(key.Substring(separator + 1, length));
                }
            }
        }
    }
}
