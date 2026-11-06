using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using Verse;

namespace LihuatLib.Core
{
    public sealed partial class RegenerationFlow : IExposable
    {
        internal sealed class Cursor { public RegenerationOrigin[] Frames = new RegenerationOrigin[8]; public int Count; public SnapshotFrame Snapshot; }
        public readonly struct Scope : IDisposable
        {
            private readonly RegenerationFlow owner;
            private readonly Cursor cursor;
            private readonly int slot;
            internal Scope(RegenerationFlow owner, Cursor cursor, int slot) { this.owner = owner; this.cursor = cursor; this.slot = slot; }
            public void Dispose() => owner?.Leave(cursor, slot);
        }
        private readonly LifecycleChannel channel;
        private readonly ThreadLocal<Cursor> cursor = new ThreadLocal<Cursor>(() => new Cursor());
        private readonly RegenerationHistory history = new RegenerationHistory();
        private List<RegenerationLink> links = new List<RegenerationLink>();
        private ConditionalWeakTable<Pawn, RegenerationLink> byPawn = new ConditionalWeakTable<Pawn, RegenerationLink>();
        public long DeniedRequests { get; private set; }
        public long DeniedAdmissions { get; private set; }
        public long DeniedGenerations { get; private set; }
        public int RecordedBodies => links.Count;
        public bool HasScope => cursor.IsValueCreated && cursor.Value.Count > 0;
        internal RegenerationFlow(LifecycleChannel channel) { this.channel = channel; }
        public string RootFor(Pawn pawn) => pawn != null && byPawn.TryGetValue(pawn, out RegenerationLink link) ? link.RootPawnId : null;
        public int CountFor(string root) => history.Count(root);
        public bool WasActivated(string root, Pawn pawn) => pawn != null
            && byPawn.TryGetValue(pawn, out RegenerationLink link) && link.RootPawnId == root && link.Activated;

        public bool Begin(Pawn source, RegenerationKind kind, out Scope scope)
        {
            LibraryCapabilities.Require(LibraryCapability.Regeneration);
            scope = default;
            if (source == null) return true;
            string root = RootFor(source) ?? source.ThingID;
            var origin = new RegenerationOrigin(source, root, kind);
            return Enter(origin, out scope);
        }
        internal bool BeginIdentity(string identity, RegenerationKind kind, out Scope scope)
        {
            scope = default;
            if (string.IsNullOrEmpty(identity)) return true;
            return Enter(new RegenerationOrigin(null, history.RootForBody(identity) ?? identity, kind), out scope);
        }
        private bool Enter(RegenerationOrigin origin, out Scope scope)
        {
            scope = default;
            if (!AllowsContext() || !channel.AllowsRegeneration(origin)) { DeniedRequests++; return false; }
            if (origin.Source != null && !byPawn.TryGetValue(origin.Source, out _)) Link(origin.Source, origin.RootPawnId).Activated = true;
            Cursor stack = cursor.Value;
            if (stack.Count == stack.Frames.Length) Array.Resize(ref stack.Frames, stack.Frames.Length * 2);
            int slot = stack.Count++;
            stack.Frames[slot] = origin;
            scope = new Scope(this, stack, slot);
            return true;
        }
        internal bool BeginPreparation(out Scope scope)
        {
            scope = default;
            if (!HasScope || Current.Kind == RegenerationKind.Preparation) return true;
            return Enter(new RegenerationOrigin(Current.Source, Current.RootPawnId, RegenerationKind.Preparation), out scope);
        }
        private RegenerationOrigin Current => cursor.Value.Frames[cursor.Value.Count - 1];
        private void Leave(Cursor stack, int slot)
        {
            if (slot != stack.Count - 1) throw new InvalidOperationException("Regeneration scopes must close in reverse order.");
            stack.Frames[slot] = default; stack.Count--;
        }
        private RegenerationLink Link(Pawn pawn, string root)
        {
            if (!byPawn.TryGetValue(pawn, out RegenerationLink link))
            { link = new RegenerationLink { Body = pawn, RootPawnId = root }; links.Add(link); byPawn.Add(pawn, link); }
            return link;
        }
        internal void Created(Pawn pawn)
        {
            if (pawn == null || !HasOrigin || HasScope && Current.Kind == RegenerationKind.Preparation
                || ReferenceEquals(pawn, CausalOrigin.Source)) return;
            Link(pawn, CausalOrigin.RootPawnId);
        }
        internal void Registered(Pawn pawn)
        {
            if (pawn?.Spawned != true) return;
            RegenerationLink link;
            if (HasOrigin)
            {
                if (HasScope && Current.Kind == RegenerationKind.Preparation) return;
                RegenerationOrigin origin = CausalOrigin;
                if (ReferenceEquals(pawn, origin.Source))
                { if (!byPawn.TryGetValue(pawn, out link) || link.Activated) return; }
                else link = Link(pawn, origin.RootPawnId);
            }
            else if (!byPawn.TryGetValue(pawn, out link)) return;
            if (link.Activated) return;
            history.Record(link.RootPawnId, pawn);
            link.Activated = true;
        }
        internal void BeforeGeneration()
        {
            if (!TryAdmitGeneration()) throw new RegenerationDeniedException();
        }
        internal bool TryAdmitGeneration()
        {
            if (channel.AllowsGeneration() && !SnapshotDenied && AllowsContext()) return true;
            DeniedGenerations++;
            return false;
        }
        internal bool Admit(Pawn pawn)
        {
            if (pawn == null) return true;
            if (SnapshotDenied) { DeniedAdmissions++; return false; }
            if (!AllowsContext(pawn)) { DeniedAdmissions++; return false; }
            if (!byPawn.TryGetValue(pawn, out RegenerationLink link)) return true;
            if (channel.AllowsRegeneration(new RegenerationOrigin(pawn, link.RootPawnId, RegenerationKind.Resurrection).ForBody(pawn))) return true;
            DeniedAdmissions++; return false;
        }
        public void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                List<RegenerationLink> saved = links.FindAll(l => PawnSaveReference.HasOwner(l.Body));
                Scribe_Collections.Look(ref saved, "regenerationLinks", LookMode.Deep);
            }
            else Scribe_Collections.Look(ref links, "regenerationLinks", LookMode.Deep);
            history.ExposeData();
            if (links == null) links = new List<RegenerationLink>();
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                byPawn = new ConditionalWeakTable<Pawn, RegenerationLink>();
                links.RemoveAll(l => l.Body == null || string.IsNullOrEmpty(l.RootPawnId));
                foreach (RegenerationLink link in links) { byPawn.Remove(link.Body); byPawn.Add(link.Body, link); }
            }
        }
    }
}
