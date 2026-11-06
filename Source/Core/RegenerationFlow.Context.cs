using Verse;

namespace LihuatLib.Core
{
    public sealed partial class RegenerationFlow
    {
        private bool AllowsContext(Pawn candidate = null)
        {
            if (!AllowsNotification(candidate)) return false;
            if (!HasScope) return true;
            Cursor stack = cursor.Value;
            for (int i = 0; i < stack.Count; i++)
            {
                RegenerationOrigin origin = stack.Frames[i];
                if (!channel.AllowsRegeneration(candidate == null ? origin : origin.ForBody(candidate))) return false;
            }
            return true;
        }

        private RegenerationOrigin CausalOrigin
        {
            get
            {
                NotificationOrigin frame = notification.Value;
                if (frame != null)
                {
                    while (frame.Previous != null) frame = frame.Previous;
                    return frame.Origin;
                }
                Cursor stack = cursor.Value;
                for (int i = 0; i < stack.Count; i++)
                    if (stack.Frames[i].Kind != RegenerationKind.Preparation) return stack.Frames[i];
                return Current;
            }
        }
    }
}
