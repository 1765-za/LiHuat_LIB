using System;
using Verse;

namespace LihuatLib.Mechanisms
{
    internal sealed class MapTransferIntent : IDisposable
    {
        [ThreadStatic] private static MapTransferIntent current;
        private readonly MapTransferIntent previous;
        private readonly Thing thing;
        private readonly Map destination;
        internal readonly ThingOwner DestinationOwner;
        internal Map Destination => destination;
        private readonly bool domainAuthority;
        private readonly bool handoff;
        internal MapTransferIntent(Thing thing, Map destination, bool domainAuthority = false, bool handoff = false, ThingOwner destinationOwner = null)
        { this.thing = thing; this.destination = destination; this.domainAuthority = domainAuthority; this.handoff = handoff; DestinationOwner = destinationOwner; previous = current; current = this; }
        internal static MapTransferIntent Handoff(Thing target)
        {
            for (MapTransferIntent item = current; item != null; item = item.previous)
                if (item.handoff && ReferenceEquals(target, item.thing)) return item;
            return null;
        }
        internal static Map HandoffDestination(Thing target)
        {
            return Handoff(target)?.destination;
        }
        internal static bool BelongsTo(Thing target, Thing root)
        {
            if (ReferenceEquals(target, root)) return true;
            IThingHolder holder = target.ParentHolder;
            for (int depth = 0; holder != null && depth < 64; depth++, holder = holder.ParentHolder)
                if (ReferenceEquals(holder, root) || holder is ThingComp comp && ReferenceEquals(comp.parent, root)) return true;
            return false;
        }
        internal static bool AuthorizedDestination(Thing target, Map map)
        {
            for (MapTransferIntent item = current; item != null; item = item.previous)
                if (item.domainAuthority && item.destination == map && BelongsTo(target, item.thing)) return true;
            return false;
        }
        internal static bool AllowsDeparture(Thing target, Map expected)
        {
            for (MapTransferIntent item = current; item != null; item = item.previous)
                if ((item.domainAuthority || item.destination == expected) && BelongsTo(target, item.thing)) return true;
            return false;
        }
        public void Dispose() => current = previous;
    }
}
