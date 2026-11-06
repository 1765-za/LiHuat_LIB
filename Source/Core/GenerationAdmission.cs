using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using LihuatLib.Mechanisms;
using Verse;

namespace LihuatLib.Core
{
    public sealed class GenerationAdmission
    {
        private sealed class Identity { internal ulong Value; }
        public sealed class RecoveryPermit : IDisposable
        {
            private readonly GenerationAdmission owner;
            private readonly ulong ticket;
            private bool disposed;
            internal RecoveryPermit(GenerationAdmission owner, ulong ticket) { this.owner = owner; this.ticket = ticket; }
            [MethodImpl(MethodImplOptions.NoInlining)]
            public void Dispose()
            {
                if (disposed) return;
                NativeGenerationAdmission.EndRecovery(owner.session, ticket);
                disposed = true;
            }
        }
        private readonly ulong session = NativeGenerationAdmission.Open();
        private readonly ConditionalWeakTable<Pawn, Identity> identities = new();
        private readonly Dictionary<string, bool> prohibitions = new();
        private readonly object publications = new();
        private long nextIdentity;
        private ulong revision;
        private bool published;
        internal bool Ready { get; private set; }
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal GenerationAdmission() { }
        ~GenerationAdmission() { NativeGenerationAdmission.Close(session); }
        private ulong Find(Pawn pawn) => pawn != null && identities.TryGetValue(pawn, out Identity value) ? value.Value : 0;
        private ulong Identify(Pawn pawn) => identities.GetValue(pawn,
            _ => new Identity { Value = (ulong)Interlocked.Increment(ref nextIdentity) }).Value;
        public bool AllowsGeneration => NativeGenerationAdmission.Check(session, 0, 0);
        public bool AllowsArrival(Pawn pawn) => pawn == null || NativeGenerationAdmission.Check(session, Find(pawn), 1);
        public bool AllowsRestore(Pawn pawn) => pawn == null || NativeGenerationAdmission.Check(session, Find(pawn), 2);
        public bool Contains(Pawn pawn) => pawn != null && NativeGenerationAdmission.Check(session, Find(pawn), 3);

        [MethodImpl(MethodImplOptions.NoInlining)]
        public void Publish(string owner, bool blocked)
        {
            RequireCaller(Assembly.GetCallingAssembly());
            if (string.IsNullOrWhiteSpace(owner)) throw new ArgumentException("A prohibition owner is required.", nameof(owner));
            lock (publications)
            {
                prohibitions[owner] = blocked;
                PublishCurrent();
            }
        }
        private void PublishCurrent()
        {
            if (!Ready) return;
            bool blocked = prohibitions.ContainsValue(true);
            if (revision != 0 && published == blocked) return;
            NativeGenerationAdmission.Publish(session, revision + 1, blocked);
            revision++;
            published = blocked;
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal void CompleteLoad()
        {
            RequireCaller(Assembly.GetCallingAssembly());
            lock (publications) { Ready = true; PublishCurrent(); }
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal void Registered(Pawn pawn)
        {
            if (pawn?.Spawned != true || PawnLife.Dead(pawn)) return;
            if (DeveloperExecution.Active)
            {
                using var permit = Recover(pawn);
                Remember(pawn);
            }
            else Remember(pawn);
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal void Remember(Pawn pawn)
        {
            RequireCaller(Assembly.GetCallingAssembly());
            if (pawn == null || PawnLife.Dead(pawn)) return;
            if (!NativeGenerationAdmission.Remember(session, Identify(pawn), true)) return;
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal void Forget(Pawn pawn)
        {
            RequireCaller(Assembly.GetCallingAssembly());
            ulong identity = Find(pawn);
            if (identity != 0) NativeGenerationAdmission.Remember(session, identity, false);
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        public RecoveryPermit Recover(Pawn pawn)
        {
            RequireCaller(Assembly.GetCallingAssembly());
            if (pawn == null) throw new ArgumentNullException(nameof(pawn));
            return new RecoveryPermit(this, NativeGenerationAdmission.Recover(session, Identify(pawn)));
        }
        private void RequireCaller(Assembly caller)
        {
            if (!ReferenceEquals(LihuatGame.Current?.Admission, this)
                || !InfrastructureAssemblies.Contains(caller))
                throw new InvalidOperationException("Admission changes require the current game's infrastructure authority.");
        }
    }
}
