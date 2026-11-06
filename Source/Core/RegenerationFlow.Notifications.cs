using System;
using System.Threading;
using Verse;

namespace LihuatLib.Core
{
    public sealed partial class RegenerationFlow
    {
        private sealed class NotificationOrigin
        {
            internal readonly RegenerationOrigin Origin;
            internal readonly NotificationOrigin Previous;
            internal NotificationOrigin(RegenerationOrigin origin, NotificationOrigin previous)
            { Origin = origin; Previous = previous; }
        }

        internal readonly struct NotificationScope : IDisposable
        {
            private readonly RegenerationFlow owner;
            private readonly NotificationOrigin previous;
            private NotificationScope(RegenerationFlow owner, NotificationOrigin previous)
            { this.owner = owner; this.previous = previous; }
            internal static NotificationScope Enter(RegenerationFlow owner, RegenerationOrigin origin)
            {
                NotificationOrigin previous = owner.notification.Value;
                NotificationOrigin inherited = previous;
                if (owner.HasScope)
                {
                    Cursor stack = owner.cursor.Value;
                    for (int i = 0; i < stack.Count; i++) inherited = new NotificationOrigin(stack.Frames[i], inherited);
                }
                owner.notification.Value = new NotificationOrigin(origin, inherited);
                return new NotificationScope(owner, previous);
            }
            public void Dispose() { if (owner != null) owner.notification.Value = previous; }
        }

        private readonly AsyncLocal<NotificationOrigin> notification = new();
        private bool HasOrigin => HasScope || notification.Value != null;

        internal NotificationScope ObserveRemoval(Pawn source)
        {
            if (source == null) return default;
            RegenerationOrigin origin = new RegenerationOrigin(source, RootFor(source) ?? source.ThingID, RegenerationKind.Replacement);
            return NotificationScope.Enter(this, origin);
        }

        private bool AllowsNotification(Pawn candidate)
        {
            for (NotificationOrigin frame = notification.Value; frame != null; frame = frame.Previous)
                if (!channel.AllowsRegeneration(candidate == null ? frame.Origin : frame.Origin.ForBody(candidate))) return false;
            return true;
        }
    }
}
