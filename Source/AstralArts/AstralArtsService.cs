using System;
using System.Collections.Generic;
using LihuatLib.Core;
using Verse;

namespace LihuatLib.AstralArts
{
    public sealed partial class AstralArtsService : IExposable
    {
        private List<AstralEntry> entries = new List<AstralEntry>();
        private readonly Dictionary<string, AstralEntry> byIdentity = new Dictionary<string, AstralEntry>(StringComparer.Ordinal);
        private readonly Dictionary<Pawn, AstralEntry> byPawn = new Dictionary<Pawn, AstralEntry>();
        private readonly Dictionary<string, Func<Pawn, bool>> permissions = new Dictionary<string, Func<Pawn, bool>>(StringComparer.Ordinal);
        private AstralEntry[] active = Array.Empty<AstralEntry>();
        private int lastTick = -1;
        public AstralPayments Payments { get; } = new();
        public int Count => active.Length;
        public AstralEntry Find(Pawn pawn) => pawn != null && byPawn.TryGetValue(pawn, out AstralEntry entry) ? entry : null;
        public bool TryBlockOrdinary(Pawn pawn, DamageInfo info)
        {
            if (OperationScope.MedicalFor(pawn) || pawn == null || !info.Def.harmsHealth || info.Def.consideredHelpful) return false;
            AstralEntry entry = Find(pawn);
            return entry?.Account.IsolationEnabled == true && entry.Allows(AstralArt.Isolation) && Available(entry)
                && entry.Account.TryBlock(entry, Verse.Find.TickManager.TicksGame, info.Amount);
        }
        public float DamageReduction(Pawn pawn)
        {
            if (!LihuatLib.Configuration.HighMechanismPolicy.AstralArts) return 0f;
            if (OperationScope.MedicalFor(pawn)) return 0f;
            AstralEntry entry = Find(pawn);
            float spells = entry?.ProviderSettlesSpells == true ? 0f : SpellReduction(pawn);
            if (entry == null || !Available(entry)) return spells;
            float isolation = entry.ProviderSettlesIsolation ? 0f : IsolationReduction(pawn);
            float enhancement = entry.ProviderSettlesEnhancement ? 0f : EnhancementReduction(pawn);
            float baseReduction = isolation == 0f ? enhancement : 1f - (1f - isolation) * (1f - enhancement);
            return spells == 0f ? baseReduction : 1f - (1f - baseReduction) * (1f - spells);
        }

        public AstralEntry Register(string providerId, string identity, Pawn pawn, float strength,
            Func<Pawn, bool> permitted = null, AstralArtsProfileDef profile = null, AstralArt arts = AstralArt.All)
        {
            LibraryCapabilities.Require(LibraryCapability.AstralArts);
            LibraryCapabilities.RequirePublicationThread();
            if (pawn == null || string.IsNullOrWhiteSpace(providerId) || string.IsNullOrWhiteSpace(identity)) throw new ArgumentException("Astral registration requires a provider, persistent identity and pawn.");
            if (float.IsNaN(strength) || float.IsInfinity(strength) || strength < 0f || strength > 1f) throw new ArgumentOutOfRangeException(nameof(strength));
            if ((arts & ~AstralArt.All) != 0) throw new ArgumentOutOfRangeException(nameof(arts));
            string key = providerId + "\n" + identity;
            bool membershipChanged = !byPawn.ContainsKey(pawn);
            if (byPawn.TryGetValue(pawn, out AstralEntry owner) && owner.Key != key) throw new InvalidOperationException("Pawn already has an astral identity.");
            if (!byIdentity.TryGetValue(key, out AstralEntry entry))
            { entry = new AstralEntry(providerId, identity); entries.Add(entry); byIdentity.Add(key, entry); }
            if (entry.Pawn != null && entry.Pawn != pawn)
            { AstralEnhancementStats.Invalidate(entry.Pawn); byPawn.Remove(entry.Pawn); entry.EnhancementStatTier = 0; }
            CompileSkills(entry);
            entry.Bind(pawn, strength, profile ?? AstralArtsDefs.Lihuat_AstralArts, arts);
            entry.Account.BindPayments(Payments, entry);
            entry.SkillRevision++;
            if (!entry.Allows(AstralArt.Repair)) entry.Account.SetRepair(false);
            if (!entry.Allows(AstralArt.Isolation)) entry.Account.SetIsolation(false, entry.Profile, Verse.Find.TickManager.TicksGame);
            if (!entry.Allows(AstralArt.Enhance)) entry.Account.SetEnhance(false, entry, Verse.Find.TickManager.TicksGame);
            byPawn[pawn] = entry; permissions[key] = permitted;
            RefreshEnhancement(entry);
            if (membershipChanged) active = new List<AstralEntry>(byPawn.Values).ToArray();
            return entry;
        }
        public void Unregister(string providerId, string identity)
        {
            LibraryCapabilities.RequirePublicationThread();
            string key = providerId + "\n" + identity;
            if (!byIdentity.TryGetValue(key, out AstralEntry entry)) return;
            AstralEnhancementStats.Invalidate(entry.Pawn);
            permissions.Remove(key); if (entry.Pawn != null) byPawn.Remove(entry.Pawn);
            entries.Remove(entry); byIdentity.Remove(key);
            active = new List<AstralEntry>(byPawn.Values).ToArray();
        }
        public bool Available(AstralEntry entry)
        {
            if (!LihuatLib.Configuration.HighMechanismPolicy.AstralArts) return false;
            Pawn pawn = entry?.Pawn;
            if (pawn == null || pawn.Dead || pawn.Destroyed || Find(pawn) != entry
                || !permissions.TryGetValue(entry.Key, out Func<Pawn, bool> permission)) return false;
            LihuatGame game = LihuatGame.Current;
            return game?.Characters?.IsStored(pawn) != true && game?.Counters.BlocksAbilities(pawn) != true
                && (game == null || (game.Domains.PressureOn(pawn) & Domains.DomainPressure.Abilities) == 0)
                && (permission == null || permission(pawn));
        }
        public void SetRepair(AstralEntry entry, bool enabled)
        { if (Find(entry?.Pawn) == entry && entry != null && (!enabled || entry.Allows(AstralArt.Repair) && Available(entry))) entry.Account.SetRepair(enabled); }
        public float IsolationReduction(Pawn pawn)
        {
            AstralEntry entry = Find(pawn);
            return entry?.Account.IsolationEnabled == true && entry.Allows(AstralArt.Isolation) && Available(entry)
                ? entry.Account.Reduction(Verse.Find.TickManager.TicksGame) : 0f;
        }
        public void UseProviderIsolationReduction(AstralEntry entry)
        { if (entry != null && Find(entry.Pawn) == entry) entry.ProviderSettlesIsolation = true; }
        public void SetIsolation(AstralEntry entry, bool enabled)
        { if (Find(entry?.Pawn) == entry && entry != null && (!enabled || entry.Allows(AstralArt.Isolation) && Available(entry))) entry.Account.SetIsolation(enabled, entry.Profile, Verse.Find.TickManager.TicksGame); }
        public void SetMultiplier(AstralEntry entry, int multiplier)
        { if (Available(entry) && entry.Allows(AstralArt.Isolation)) entry.Account.SetMultiplier(multiplier, entry.Profile, Verse.Find.TickManager.TicksGame); }
        public bool SetHealingBudget(AstralEntry entry, int budget)
        {
            if (entry == null || Find(entry.Pawn) != entry || !entry.Allows(AstralArt.Heal) || budget < 1 || budget > entry.Capacity) return false;
            entry.Account.SetHealingBudget(budget);
            return true;
        }
        internal void Tick(int now)
        {
            if (!LihuatLib.Configuration.HighMechanismPolicy.AstralArts) return;
            SpellRuntime.Tick(now);
            if (active.Length == 0 || now % AstralAccount.Interval != 0 || lastTick == now) return;
            lastTick = now;
            AstralEntry[] subjects = active;
            foreach (AstralEntry entry in subjects)
            {
                Pawn pawn = entry.Pawn;
                if (pawn == null || pawn.Dead || pawn.Destroyed || LihuatGame.Current?.Characters?.IsStored(pawn) == true) continue;
                bool allowed = Available(entry);
                entry.Account.Advance(entry, now, pawn.jobs?.curDriver?.asleep == true, allowed);
                RefreshEnhancement(entry);
                if (allowed && entry.Account.RepairEnabled) AstralRepair.Apply(entry);
            }
        }
        public void ExposeData()
        {
            SpellRuntime.ExposeData();
            Scribe_Collections.Look(ref entries, "astralArts", LookMode.Deep);
            Scribe_Values.Look(ref lastTick, "astralLastTick", -1);
            if (Scribe.mode != LoadSaveMode.PostLoadInit) return;
            byIdentity.Clear(); byPawn.Clear(); permissions.Clear(); active = Array.Empty<AstralEntry>();
            if (entries == null) entries = new List<AstralEntry>();
            foreach (AstralEntry entry in entries) byIdentity.Add(entry.Key, entry);
        }
    }
}
