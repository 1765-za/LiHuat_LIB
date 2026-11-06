using System;
using System.Collections.Generic;
using Verse;

namespace LihuatLib.Core
{
    public sealed class DamagePreparationHandler
    {
        public string Id;
        public int Order;
        public Func<Thing, DamageInfo, float?> Amount;
    }
    public sealed class DamagePreparationChannel
    {
        private readonly OrderedHandlers<DamagePreparationHandler> handlers = new(h => h.Id, h => h.Order);
        private Frame current;
        public void Register(DamagePreparationHandler handler)
        {
            LibraryCapabilities.RequirePublicationThread();
            if (handler == null || string.IsNullOrWhiteSpace(handler.Id) || handler.Amount == null) throw new ArgumentException("Damage preparation needs an identity and query.");
            handlers.Register(handler);
        }
        public void Unregister(string id) { LibraryCapabilities.RequirePublicationThread(); handlers.Remove(id); }
        internal void Unregister(DamagePreparationHandler handler) { LibraryCapabilities.RequirePublicationThread(); handlers.Remove(handler); }
        private bool Covered(Thing target)
        {
            for (var frame = current; frame != null; frame = frame.Previous) if (frame.Target == target) return true;
            return false;
        }
        private void Prepare(Thing target, ref DamageInfo info)
        {
            if (OperationScope.MedicalFor(target) || OperationScope.ControlsDamage(target)) return;
            foreach (var handler in handlers.Items)
            {
                float? amount = handler.Amount(target, info);
                if (!amount.HasValue) continue;
                if (!float.IsFinite(amount.Value) || amount.Value < 0) throw new InvalidOperationException("Damage preparation must return a finite, nonnegative amount.");
                info.SetAmount(amount.Value);
            }
        }
        internal IDisposable Enter(Thing target, ref DamageInfo info, bool primary)
        {
            if (handlers.Count == 0 || !primary && Covered(target)) return null;
            float before = info.Amount; Prepare(target, ref info);
            return info.Amount == before ? null : new Frame(this, target);
        }
        internal bool DirectInjury(Pawn target, Hediff_Injury injury, DamageInfo? evidence)
        {
            if (handlers.Count == 0 || Covered(target) || !evidence.HasValue || evidence.Value.Amount <= 0) return false;
            var info = evidence.Value; float before = info.Amount; Prepare(target, ref info);
            if (info.Amount == before) return false;
            injury.Severity *= info.Amount / before; return true;
        }
        private sealed class Frame : IDisposable
        {
            private DamagePreparationChannel owner;
            internal readonly Frame Previous;
            internal readonly Thing Target;
            internal Frame(DamagePreparationChannel owner, Thing target)
            { this.owner = owner; Target = target; Previous = owner.current; owner.current = this; }
            public void Dispose()
            {
                if (owner == null) return;
                if (owner.current != this) throw new InvalidOperationException("Damage frames must close in reverse order.");
                owner.current = Previous; owner = null;
            }
        }
    }
}
