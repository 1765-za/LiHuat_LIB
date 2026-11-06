using System;
using LihuatLib.Core;
using LihuatLib.Mechanisms;
using Verse;

namespace LihuatLib.AstralArts
{
    internal static class AstralRepair
    {
        internal const float UncappedCapacity = 750f;
        internal static bool Uncapped(AstralEntry entry) => entry.Capacity > UncappedCapacity;

        internal static void Apply(AstralEntry entry)
        {
            float cost = entry.RepairCost;
            float limit = Uncapped(entry) ? float.PositiveInfinity : entry.RepairLimit;
            Heal(entry, entry.Pawn, limit, cost);
            AstralRegrowth.Apply(entry, entry.Pawn, entry.Account.AvailableEnergy, cost);
        }

        internal static float Heal(AstralEntry payer, Pawn patient, float limit, float cost, Predicate<Hediff_Injury> eligible = null)
        {
            float remaining = Math.Min(limit, payer.Account.AvailableEnergy / cost), total = 0f;
            if (!AstralAccount.PositiveFinite(remaining)) return 0f;
            var hediffs = patient.health.hediffSet.hediffs;
            using var medical = new OperationScope(OperationKind.Medical, patient);
            for (int i = hediffs.Count - 1; i >= 0 && remaining > .0001f; i--)
            {
                if (i >= hediffs.Count || !(hediffs[i] is Hediff_Injury injury) || !AstralAccount.PositiveFinite(injury.Severity)
                    || eligible != null && !eligible(injury)) continue;
                float before = injury.Severity, requested = Math.Min(remaining, before);
                if (!payer.Account.TryReserve(requested * cost, out var payment)) break;
                try { MedicalRestoration.Heal(injury, requested); }
                finally
                {
                    float healed = Math.Max(0f, Math.Min(requested, before - injury.Severity));
                    using (payment) payment.Commit(healed * cost);
                    remaining -= requested;
                    total += healed;
                }
            }
            return total;
        }
    }
}
