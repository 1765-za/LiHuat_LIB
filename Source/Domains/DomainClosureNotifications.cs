using System;

namespace LihuatLib.Domains
{
    public sealed partial class DomainCoordinator
    {
        public long CloseNotificationFailures { get; private set; }
        public string LastCloseNotificationFailure { get; private set; }

        private void CompleteClosures(DomainArea[] ending)
        {
            foreach (DomainArea area in ending) { area.Closed = true; external.Remove(area); }
            Rebuild();
            foreach (DomainArea area in ending) NotifyClosed(area);
            RefreshAll();
        }

        private void NotifyClosed(DomainArea area)
        {
            Action<DomainArea> subscribers = Closed;
            if (subscribers == null) return;
            foreach (Action<DomainArea> subscriber in subscribers.GetInvocationList())
                try { subscriber(area); }
                catch (Exception error)
                {
                    CloseNotificationFailures++;
                    LastCloseNotificationFailure = subscriber.Method.DeclaringType?.FullName + "." + subscriber.Method.Name + ": " + error;
                    Verse.Log.ErrorOnce("[LihuatLib.Domain] Close notification failed: " + LastCloseNotificationFailure, 14581573);
                }
        }
    }
}
