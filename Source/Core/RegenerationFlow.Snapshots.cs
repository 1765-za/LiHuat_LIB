using System;
using Verse;

namespace LihuatLib.Core
{
    public sealed partial class RegenerationFlow
    {
        internal sealed class SnapshotFrame
        {
            internal RegenerationFlow Owner;
            internal SnapshotFrame Previous;
            internal Scope Origin;
            internal bool Identified;
            internal bool Denied;
        }
        internal SnapshotFrame Snapshot => cursor.IsValueCreated ? cursor.Value.Snapshot : null;
        private bool SnapshotDenied => Snapshot?.Denied == true;

        internal SnapshotFrame BeginSnapshot()
        {
            Cursor current = cursor.Value;
            var frame = new SnapshotFrame { Owner = this, Previous = current.Snapshot, Denied = current.Snapshot?.Denied == true };
            current.Snapshot = frame;
            return frame;
        }
        internal void IdentifySnapshot(string identity)
        {
            SnapshotFrame frame = Snapshot;
            if (frame == null) return;
            if (frame.Denied) throw new RegenerationDeniedException();
            if (frame.Identified) return;
            frame.Identified = true;
            if (BeginIdentity(identity, RegenerationKind.Replacement, out frame.Origin)) return;
            frame.Denied = true;
            throw new RegenerationDeniedException();
        }
        internal bool EndSnapshot(SnapshotFrame frame, Pawn pawn)
        {
            if (frame == null) return true;
            if (!ReferenceEquals(cursor.Value.Snapshot, frame)) throw new InvalidOperationException("Snapshot scopes must close in reverse order.");
            try
            {
                if (frame.Denied) return false;
                if (frame.Identified) Created(pawn);
                return true;
            }
            finally { frame.Origin.Dispose(); cursor.Value.Snapshot = frame.Previous; }
        }
    }
}
