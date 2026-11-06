using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace LihuatLib.Core
{
    public sealed class StatLimitHandler
    {
        public string Id;
        public int Order;
        public Func<StatDef, Pawn, float?> Maximum;
    }
    public sealed class StatLimitChannel
    {
        private readonly OrderedHandlers<StatLimitHandler> handlers = new(h => h.Id, h => h.Order);
        public void Register(StatLimitHandler handler)
        {
            LibraryCapabilities.RequirePublicationThread();
            if (handler == null || string.IsNullOrWhiteSpace(handler.Id) || handler.Maximum == null) throw new ArgumentException("Stat limit registration requires an identity and query.");
            handlers.Register(handler);
        }
        public void Unregister(string id) { LibraryCapabilities.RequirePublicationThread(); handlers.Remove(id); }
        internal void Unregister(StatLimitHandler handler) { LibraryCapabilities.RequirePublicationThread(); handlers.Remove(handler); }
        public void Apply(StatDef stat, Pawn pawn, ref float value)
        {
            if (pawn == null || DeveloperExecution.Active) return;
            foreach (var handler in handlers.Items)
            {
                float? limit = handler.Maximum(stat, pawn);
                if (limit.HasValue && !float.IsNaN(limit.Value)) value = Math.Min(value, limit.Value);
            }
        }
    }
}
