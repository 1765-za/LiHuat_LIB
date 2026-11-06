using System;
using System.Collections.Generic;

namespace LihuatLib.Domains
{
    public sealed partial class DomainCoordinator
    {
        private sealed class ClosingHandler
        {
            internal string Id;
            internal int Order;
            internal Action<DomainArea> Prepare;
        }

        private readonly OrderedHandlers<ClosingHandler> closingHandlers = new(h => h.Id, h => h.Order);

        /// <summary>
        /// Runs before return contents are collected. A failed pass is retried, so handlers must be idempotent.
        /// A completed pass is persisted with the area and is not repeated during transfer retries.
        /// </summary>
        public void RegisterClosingHandler(string id, int order, Action<DomainArea> prepare)
        {
            LibraryCapabilities.Require(LibraryCapability.Domains);
            if (string.IsNullOrWhiteSpace(id) || prepare == null)
                throw new ArgumentException("A closing handler requires an id and a preparation action.");
            closingHandlers.Register(new ClosingHandler { Id = id, Order = order, Prepare = prepare });
        }
        internal void UnregisterClosingHandler(string id, Action<DomainArea> prepare)
        {
            foreach (var handler in closingHandlers.Items)
                if (handler.Id == id && ReferenceEquals(handler.Prepare, prepare)) { closingHandlers.Remove(handler); break; }
        }

        private void PrepareClosure(DomainArea area)
        {
            if (area.ClosurePrepared) return;
            foreach (ClosingHandler handler in closingHandlers.Items) handler.Prepare(area);
            area.ClosurePrepared = true;
        }
    }
}
