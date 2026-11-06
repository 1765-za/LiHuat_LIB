using System.Runtime.CompilerServices;
using System.Collections.Generic;
using LihuatLib.Core;

namespace LihuatLib.StarBlood
{
    internal sealed class StarBloodOwners
    {
        private sealed class Membership
        {
            internal readonly ConditionalWeakTable<object, StarBloodRules> Index = new();
            internal readonly List<StarBloodRules> Engines = new();
        }
        private readonly RetainedState<Membership> membership = new(new Membership());
        private ConditionalWeakTable<object, StarBloodRules> Index => membership.Value.Index;
        internal List<StarBloodRules> Engines => membership.Value.Engines;

        internal bool TryGetValue(object key, out StarBloodRules value) => Index.TryGetValue(key, out value);
        internal void Add(object key, StarBloodRules value) => Index.Add(key, value);
        internal void Remove(object key) => Index.Remove(key);
    }
}
