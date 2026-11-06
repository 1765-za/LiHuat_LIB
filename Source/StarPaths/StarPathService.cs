using System;
using System.Collections.Generic;
using Verse;
using LihuatLib.Configuration;

namespace LihuatLib.StarPaths
{
    public sealed class StarPathService : IExposable
    {
        private List<StarPathSubject> subjects = new List<StarPathSubject>();
        private readonly Dictionary<string, StarPathSubject> byIdentity = new Dictionary<string, StarPathSubject>(StringComparer.Ordinal);
        private readonly Dictionary<Pawn, StarPathSubject> byPawn = new Dictionary<Pawn, StarPathSubject>();
        public StarPathSubject Find(Pawn pawn) => pawn != null && byPawn.TryGetValue(pawn, out StarPathSubject subject) ? subject : null;
        public bool Active(StarPathSubject subject) => subject != null && Find(subject.Pawn) == subject && !subject.Pawn.Dead
            && !subject.Pawn.Destroyed && (subject.Permission == null || subject.Permission(subject.Pawn));

        public StarPathSubject Register(string provider, string identity, Pawn pawn, int level,
            Func<Pawn, bool> permission = null, Func<DamageChannel, float> incoming = null, StarPathTreeDef tree = null,
            StarPathOutgoing outgoing = null, Func<Hediff, bool> injuryCost = null)
        {
            LibraryCapabilities.Require(LibraryCapability.StarPaths);
            LibraryCapabilities.RequirePublicationThread();
            if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(identity) || pawn == null || level < 0)
                throw new ArgumentException("Star path registration requires an identity, pawn and nonnegative level.");
            string key = provider + "\n" + identity;
            if (byPawn.TryGetValue(pawn, out StarPathSubject owner) && owner.Key != key)
                throw new InvalidOperationException("Pawn already has a star path identity.");
            if (!byIdentity.TryGetValue(key, out StarPathSubject subject))
            { subject = new StarPathSubject(provider, identity); subjects.Add(subject); byIdentity.Add(key, subject); }
            if (subject.Tree != null && subject.Tree != (tree ?? StarPathDefs.Lihuat_StarPath))
                throw new InvalidOperationException("An existing star path account cannot change its skill tree.");
            Pawn previous = subject.Pawn;
            if (previous != null && previous != pawn) { byPawn.Remove(previous); StarPathStats.Invalidate(previous, subject.Effects); }
            subject.Bind(pawn, tree ?? StarPathDefs.Lihuat_StarPath, permission, incoming, outgoing, injuryCost);
            byPawn[pawn] = subject;
            SetLevel(subject, level);
            Rebuild(subject);
            return subject;
        }

        public void Unregister(string provider, string identity)
        {
            LibraryCapabilities.RequirePublicationThread();
            string key = provider + "\n" + identity;
            if (!byIdentity.Remove(key, out StarPathSubject subject)) return;
            if (subject.Pawn != null) { byPawn.Remove(subject.Pawn); StarPathStats.Invalidate(subject.Pawn, subject.Effects); }
            subjects.Remove(subject);
        }

        public void SetLevel(StarPathSubject subject, int level)
        {
            RequireSubject(subject);
            if (subject.Account.Level == level) return;
            subject.Account.SetLevel(level); subject.Revision++;
        }

        public string UpgradeFailure(StarPathSubject subject, StarPathNodeDef node) => StarPathProgression.Failure(this, subject, node);
        public bool TryUpgrade(StarPathSubject subject, StarPathNodeDef node)
        {
            RequireSubject(subject);
            if (UpgradeFailure(subject, node) != null) return false;
            int cost = node.ranks[subject.Account.Rank(node)].cost;
            subject.Account.Purchase(node, cost); Rebuild(subject);
            return true;
        }

        public StarPathDamageSplit Split(Pawn pawn, float amount)
        {
            if (!float.IsFinite(amount) || amount < 0f) throw new ArgumentOutOfRangeException(nameof(amount));
            StarPathSubject subject = Find(pawn);
            StarPathEffects effects = Active(subject) ? subject.Effects : StarPathEffects.Empty;
            return new StarPathDamageSplit(amount, effects.Advanced, effects.Specialized, effects.True);
        }

        public bool Converts(Pawn pawn)
        { StarPathSubject subject = Find(pawn); return Active(subject) && subject.Effects.Converted > 0f; }
        public bool GuaranteedHit(Pawn pawn)
        {
            if (!LibraryMod.Mechanics.Allows(GameplayMechanic.GuaranteedHit)) return false;
            StarPathSubject subject = Find(pawn); return Active(subject) && (subject.GuaranteedHit || subject.Effects.GuaranteedHit);
        }
        public float PenetrationFactor(Pawn pawn)
        { StarPathSubject subject = Find(pawn); return Active(subject) ? subject.Effects.PenetrationFactor : 1f; }
        public float NodeIncomingFactor(Pawn pawn, DamageChannel channel)
        {
            StarPathSubject subject = Find(pawn);
            if (!Active(subject)) return 1f;
            return channel == DamageChannel.Advanced ? subject.Effects.AdvancedIncoming
                : channel == DamageChannel.Specialized ? subject.Effects.SpecializedIncoming : 1f;
        }
        public float IncomingFactor(Pawn pawn, DamageChannel channel)
        {
            if (channel == DamageChannel.True) return 1f;
            StarPathSubject subject = Find(pawn);
            float factor = Active(subject) ? subject.Incoming?.Invoke(channel) ?? 1f : 1f;
            if (!float.IsFinite(factor) || factor < 0f) throw new InvalidOperationException("Star path incoming coefficient is invalid.");
            return factor * NodeIncomingFactor(pawn, channel);
        }
        public float GlobalReduction(Pawn pawn, Hediff injury = null)
        {
            StarPathSubject subject = Find(pawn);
            return Active(subject) && (injury == null || subject.InjuryCost?.Invoke(injury) != true) ? subject.Effects.GlobalReduction : 0f;
        }
        public void UseProviderGlobalReduction(StarPathSubject subject)
        { RequireSubject(subject); subject.ProviderSettlesGlobal = true; }
        internal float InjuryReduction(Pawn pawn, Hediff injury)
        { return Find(pawn)?.ProviderSettlesGlobal == true ? 0f : GlobalReduction(pawn, injury); }

        internal void Rebuild(StarPathSubject subject)
        {
            StarPathStats.Invalidate(subject.Pawn, subject.Effects);
            subject.Effects = new StarPathEffects(subject.Account);
            StarPathStats.Invalidate(subject.Pawn, subject.Effects); subject.Revision++;
        }
        internal void RefreshMechanisms()
        {
            foreach (StarPathSubject subject in byPawn.Values) Rebuild(subject);
        }
        private void RequireSubject(StarPathSubject subject)
        {
            LibraryCapabilities.RequirePublicationThread();
            if (subject == null || Find(subject.Pawn) != subject) throw new InvalidOperationException("Star path subject is not registered.");
        }
        public void ExposeData()
        {
            Scribe_Collections.Look(ref subjects, "starPathAscension", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.LoadingVars) subjects ??= new List<StarPathSubject>();
            if (Scribe.mode != LoadSaveMode.PostLoadInit) return;
            byIdentity.Clear(); byPawn.Clear();
            foreach (StarPathSubject subject in subjects) byIdentity.Add(subject.Key, subject);
        }
    }
}
