using System;
using Verse;

namespace LihuatLib.AstralArts
{
    public sealed partial class AstralAccount : IExposable
    {
        public const int Interval = 60, MaximumMultiplier = 5;
        public const float MaximumReduction = .32f;
        private float energy;
        private bool initialized, repairEnabled, isolationEnabled;
        private int multiplier = 1, paidUntil;
        private int healingBudget = 25;
        private float isolationBonus;
        public float Energy => energy;
        public bool RepairEnabled => repairEnabled;
        public bool IsolationEnabled => isolationEnabled;
        public int Multiplier => multiplier;
        public int HealingBudget => healingBudget;
        public bool Powered(int now) => isolationEnabled && now < paidUntil;
        public float Reduction(int now) => Powered(now) ? MaximumReduction * multiplier / MaximumMultiplier + isolationBonus : 0f;
        internal void SetIsolationBonus(float value) => isolationBonus = value;
        internal void Initialize(float capacity)
        {
            if (initialized) { energy = Math.Min(energy, capacity); return; }
            initialized = true; energy = capacity;
        }
        internal void Advance(AstralEntry entry, int now, bool resting, bool permitted)
        {
            Restore(entry.Capacity, entry.Recovery(resting));
            if (!permitted) paidUntil = 0;
            else if (isolationEnabled && now >= paidUntil) PayIsolation(entry.Profile, now);
            AdvanceEnhancement(entry, now, permitted);
        }
        internal void SetRepair(bool enabled) => repairEnabled = enabled;
        internal void SetHealingBudget(int value) => healingBudget = value;
        internal void SetIsolation(bool enabled, AstralArtsProfileDef profile, int now)
        {
            if (isolationEnabled == enabled) return;
            isolationEnabled = enabled; paidUntil = 0;
            if (enabled) PayIsolation(profile, now);
        }
        internal void SetMultiplier(int value, AstralArtsProfileDef profile, int now)
        {
            value = Math.Max(1, Math.Min(MaximumMultiplier, value));
            if (multiplier == value) return;
            multiplier = value; paidUntil = 0;
            if (isolationEnabled) PayIsolation(profile, now);
        }
        private void PayIsolation(AstralArtsProfileDef profile, int now)
        {
            // Partial first intervals end on the service's shared one-second boundary.
            int until = now - now % Interval + Interval;
            float cost = profile.isolationUpkeep * multiplier * (until - now) / Interval;
            paidUntil = TrySpend(cost) ? until : 0;
        }
        internal bool TryBlock(AstralEntry entry, int now, float damage)
        {
            if (!Powered(now) || !PositiveFinite(damage) || !PositiveFinite(AvailableEnergy)) return false;
            float cost = AstralIsolation.BlockCost(entry, damage);
            return TrySpend(Math.Min(AvailableEnergy, cost));
        }
        public bool TrySpend(float amount)
        {
            if (payments == null) return TrySpendNative(amount);
            return payments.TrySpend(owner, amount);
        }
        internal bool TrySpendNative(float amount)
        {
            if (!PositiveFinite(amount) || energy < amount) return false;
            energy -= amount; return true;
        }
        internal bool SpendReserved(float amount)
        {
            if (!TryReserve(amount, out var payment)) return false;
            using (payment) payment.Commit(amount);
            return true;
        }
        internal void Restore(float capacity, float amount)
        { if (PositiveFinite(amount)) energy = (float)Math.Min(capacity, (double)energy + amount); }
        public static bool PositiveFinite(float value) => value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);
        public void ExposeData()
        {
            Scribe_Values.Look(ref initialized, "initialized");
            Scribe_Values.Look(ref energy, "energy");
            Scribe_Values.Look(ref repairEnabled, "repairEnabled");
            Scribe_Values.Look(ref isolationEnabled, "isolationEnabled");
            Scribe_Values.Look(ref multiplier, "multiplier", 1);
            Scribe_Values.Look(ref healingBudget, "healingBudget", 25);
            Scribe_Values.Look(ref paidUntil, "paidUntil");
            Scribe_Values.Look(ref enhanceEnabled, "enhanceEnabled");
            Scribe_Values.Look(ref enhancedUntil, "enhancedUntil");
        }
    }
}
