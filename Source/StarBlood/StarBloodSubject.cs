using System;
using LihuatLib.Core;
using Verse;

namespace LihuatLib.StarBlood
{
    public sealed class StarBloodSubject
    {
        public Pawn Pawn { get; }
        public StarBloodRecord State { get; }
        public HediffDef MarkerDef { get; }
        public bool AutomaticObservation { get; }
        public Func<Hediff, bool> AcceptsWound { get; }
        public Action SettleWounds { get; }
        public Func<float> DamageThreshold { get; private set; }
        public Func<Hediff, bool> MissingPartAdmission { get; private set; }
        public StarBloodSubject WithMissingPartAdmission(Func<Hediff, bool> admission)
        {
            if (admission == null) throw new ArgumentNullException(nameof(admission));
            if (MissingPartAdmission != null || StarBloodRegistry.For(Pawn)?.Subject == this)
                throw new InvalidOperationException("Missing-part admission must be configured before registration.");
            MissingPartAdmission = admission;
            return this;
        }
        private readonly Func<bool> enabled;
        private readonly Func<int> clock;
        private readonly Func<bool> continuity;
        private bool DeveloperDeath => State.DeveloperDeathPawnId != null && State.DeveloperDeathPawnId == Pawn.ThingID;
        public bool Enabled => Configuration.HighMechanismPolicy.Maintenance && !DeveloperExecution.Active && !State.DeveloperRemovedMarker && !DeveloperDeath && enabled();
        public bool ContinuityEnabled => Configuration.HighMechanismPolicy.Maintenance && !DeveloperExecution.Active && !State.DeveloperRemovedMarker && !DeveloperDeath && continuity();
        public int Now => clock();

        public StarBloodSubject WithDamageThreshold(Func<float> threshold)
        {
            if (threshold == null) throw new ArgumentNullException(nameof(threshold));
            if (DamageThreshold != null || StarBloodRegistry.For(Pawn)?.Subject == this)
                throw new InvalidOperationException("Damage threshold must be configured once before registration.");
            DamageThreshold = threshold;
            return this;
        }

        public StarBloodSubject(Pawn pawn, StarBloodRecord state, HediffDef markerDef, Func<bool> enabled, Func<int> clock, bool automaticObservation = true, Func<bool> continuity = null, Func<Hediff, bool> acceptsWound = null, Action settleWounds = null)
        {
            Pawn = pawn ?? throw new ArgumentNullException(nameof(pawn));
            State = state ?? throw new ArgumentNullException(nameof(state));
            MarkerDef = markerDef ?? throw new ArgumentNullException(nameof(markerDef));
            this.enabled = enabled ?? throw new ArgumentNullException(nameof(enabled));
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            this.continuity = continuity ?? enabled;
            AutomaticObservation = automaticObservation;
            AcceptsWound = acceptsWound;
            SettleWounds = settleWounds;
        }
    }
}
