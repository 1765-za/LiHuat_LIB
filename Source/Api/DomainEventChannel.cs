using System;
using LihuatLib.Domains;
using Verse;

namespace LihuatLib.Api
{
    public sealed class DomainCallbacks
    {
        internal string Id;
        public int Order;
        public Action<DomainArea> Opened;
        public Action<DomainArea> Closed;
        public Action<Pawn> Captured;
        public Action<Pawn> PressureChanged;
        internal DomainCallbacks Copy(string id)
        { var copy = (DomainCallbacks)MemberwiseClone(); copy.Id = id; return copy; }
    }

    internal sealed class DomainEventChannel(DomainCoordinator domains)
    {
        private readonly OrderedHandlers<DomainCallbacks> handlers = new(h => h.Id, h => h.Order);
        internal void Register(DomainCallbacks handler)
        {
            if (handlers.Count == 0)
            {
                domains.Opened += Opened; domains.Closed += Closed;
                domains.Captured += Captured; domains.PressureChanged += PressureChanged;
            }
            handlers.Register(handler);
        }
        internal void Unregister(DomainCallbacks handler)
        {
            handlers.Remove(handler);
            if (handlers.Count != 0) return;
            domains.Opened -= Opened; domains.Closed -= Closed;
            domains.Captured -= Captured; domains.PressureChanged -= PressureChanged;
        }
        private void Opened(DomainArea area) => Dispatch(area, static h => h.Opened);
        private void Closed(DomainArea area) => Dispatch(area, static h => h.Closed);
        private void Captured(Pawn pawn) => Dispatch(pawn, static h => h.Captured);
        private void PressureChanged(Pawn pawn) => Dispatch(pawn, static h => h.PressureChanged);
        private void Dispatch<T>(T value, Func<DomainCallbacks, Action<T>> callback)
        {
            foreach (var handler in handlers.Items)
            {
                try { callback(handler)?.Invoke(value); }
                catch (Exception error)
                { Log.ErrorOnce("[LihuatLib.Api] Domain observer failed: " + handler.Id + "\n" + error, GenText.StableStringHash(handler.Id)); }
            }
        }
    }
}
