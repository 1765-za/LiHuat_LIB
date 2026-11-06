using System;

namespace LihuatLib.AstralArts
{
    public sealed partial class AstralAccount
    {
        private AstralPayments payments;
        private AstralEntry owner;
        public float AvailableEnergy => payments?.Available(owner) ?? energy;
        internal void BindPayments(AstralPayments channel, AstralEntry entry) { payments = channel; owner = entry; }
        public bool TryReserve(float amount, out AstralPayment payment)
        {
            if (payments != null) return payments.TryReserve(owner, amount, out payment);
            payment = null;
            if (!TryReserveNative(amount, out var native)) return false;
            payment = new AstralPayment(); payment.Add(native, amount); return true;
        }
        internal bool TryReserveNative(float amount, out IAstralPaymentReservation reservation)
        {
            reservation = null;
            if (!PositiveFinite(amount) || energy < amount) return false;
            energy -= amount; reservation = new NativeReservation(this, amount); return true;
        }
        private sealed class NativeReservation : IAstralPaymentReservation
        {
            private AstralAccount account;
            private readonly float reserved;
            private float committed;
            private bool settled;
            internal NativeReservation(AstralAccount account, float amount) { this.account = account; reserved = amount; }
            public void Commit(float amount)
            {
                if (settled || amount < 0 || amount > reserved || float.IsNaN(amount)) throw new InvalidOperationException("Invalid native payment settlement.");
                settled = true; committed = amount;
            }
            public void Dispose()
            {
                if (account == null) return;
                account.energy += reserved - committed; account = null;
            }
        }
    }
}
