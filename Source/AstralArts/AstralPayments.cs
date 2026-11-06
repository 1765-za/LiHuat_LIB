using System;
using System.Collections.Generic;
using LihuatLib.Core;

namespace LihuatLib.AstralArts
{
    public interface IAstralPaymentSource
    {
        float Available(AstralEntry entry);
        bool TryReserve(AstralEntry entry, float amount, out IAstralPaymentReservation reservation);
    }
    public interface IAstralPaymentReservation : IDisposable
    {
        void Commit(float amount);
    }
    public sealed class AstralPayment : IDisposable
    {
        private readonly List<(IAstralPaymentReservation Reservation, float Amount)> parts = new();
        private float total;
        private bool settled;
        internal void Add(IAstralPaymentReservation reservation, float amount) { parts.Add((reservation, amount)); total += amount; }
        public void Commit(float amount)
        {
            if (settled || float.IsNaN(amount) || amount < 0 || amount > total) throw new InvalidOperationException("Payment can only settle once, within its reservation.");
            settled = true;
            foreach (var part in parts)
            {
                float used = Math.Min(amount, part.Amount);
                part.Reservation.Commit(used); amount -= used;
            }
        }
        public void Dispose()
        {
            settled = true;
            foreach (var part in parts) part.Reservation.Dispose();
            parts.Clear();
        }
    }
    public sealed class AstralPayments
    {
        private sealed record Source(string Id, int Order, IAstralPaymentSource Provider);
        private readonly List<Source> sources = new();
        public void Register(string providerId, int order, IAstralPaymentSource source)
        {
            LibraryCapabilities.Require(LibraryCapability.AstralArts); LibraryCapabilities.RequirePublicationThread();
            if (string.IsNullOrWhiteSpace(providerId) || source == null) throw new ArgumentException("Payment sources require an identity and provider.");
            Unregister(providerId); sources.Add(new Source(providerId, order, source));
            sources.Sort((a, b) => a.Order != b.Order ? a.Order.CompareTo(b.Order) : string.CompareOrdinal(a.Id, b.Id));
        }
        public void Unregister(string providerId) { LibraryCapabilities.RequirePublicationThread(); sources.RemoveAll(s => s.Id == providerId); }
        public float Available(AstralEntry entry)
        {
            double total = entry.Account.Energy;
            foreach (var source in sources)
            {
                float amount = source.Provider.Available(entry);
                if (AstralAccount.PositiveFinite(amount)) total += amount;
            }
            return (float)Math.Min(float.MaxValue, total);
        }
        internal bool TrySpend(AstralEntry entry, float amount)
        {
            if (!AstralAccount.PositiveFinite(amount)) return false;
            if (entry.Account.Energy >= amount)
            {
                bool earlier = false;
                foreach (var source in sources)
                {
                    if (source.Order >= 0) break;
                    if (AstralAccount.PositiveFinite(source.Provider.Available(entry))) { earlier = true; break; }
                }
                if (!earlier) return entry.Account.TrySpendNative(amount);
            }
            return entry.Account.SpendReserved(amount);
        }
        internal bool TryReserve(AstralEntry entry, float amount, out AstralPayment payment)
        {
            payment = null;
            if (!AstralAccount.PositiveFinite(amount) || Available(entry) < amount) return false;
            var pending = new AstralPayment();
            bool nativeUsed = false;
            float remaining = amount;
            try
            {
                foreach (var source in sources)
                {
                    if (!nativeUsed && source.Order >= 0) { ReserveNative(); nativeUsed = true; }
                    if (remaining <= 0) break;
                    float available = source.Provider.Available(entry);
                    if (!AstralAccount.PositiveFinite(available)) continue;
                    float take = Math.Min(remaining, available);
                    if (!source.Provider.TryReserve(entry, take, out var part) || part == null) { pending.Dispose(); return false; }
                    pending.Add(part, take); remaining -= take;
                }
                if (!nativeUsed && remaining > 0) ReserveNative();
                if (remaining > 0) { pending.Dispose(); return false; }
                payment = pending; return true;
            }
            catch { pending.Dispose(); throw; }
            void ReserveNative()
            {
                float take = Math.Min(remaining, entry.Account.Energy);
                if (take <= 0) return;
                if (!entry.Account.TryReserveNative(take, out var part)) return;
                pending.Add(part, take); remaining -= take;
            }
        }
    }
}
