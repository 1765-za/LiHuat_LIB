using Verse;

namespace LihuatLib.AstralArts
{
    [System.Flags]
    public enum AstralArt { None = 0, Repair = 1, Isolation = 2, Heal = 4, Enhance = 8, Mobility = 16, Offense = 32, All = Repair | Isolation | Heal | Enhance | Mobility | Offense }

    public sealed class AstralEntry : IExposable
    {
        private string provider, identity;
        private string key;
        private Pawn pawn;
        private float strength;
        private AstralArtsProfileDef profile;
        private AstralAccount account = new AstralAccount();
        private AstralArt arts;
        private StarPaths.StarPathAccount skills = new();
        private AstralSpellBook spells = new();
        internal bool ProviderSettlesEnhancement;
        internal bool ProviderSettlesIsolation;
        internal bool ProviderSettlesSpells;
        internal int EnhancementStatTier;
        internal AstralSkillEffects SkillEffects = AstralSkillEffects.Empty;
        public StarPaths.StarPathAccount Skills => skills;
        public AstralSpellBook Spells => spells;
        public AstralArt Arts => arts;
        public int SkillRevision { get; internal set; }
        public Pawn Pawn => pawn;
        public float Strength => strength;
        public AstralArtsProfileDef Profile => profile;
        public AstralAccount Account => account;
        public bool Allows(AstralArt art) => (arts & art) == art;
        public float Capacity => profile.Capacity(strength) * (1f + SkillEffects.Capacity);
        public float Recovery(bool resting) => Capacity * profile.RecoveryFraction(strength) * (1f + SkillEffects.Recovery)
            * (resting ? profile.restingRecoveryFactor * (1f + SkillEffects.Meditation) : 1f);
        public float RepairLimit => profile.Repair(strength) * (1f + SkillEffects.Repair);
        public float RepairCost => profile.RepairCost(strength) * SkillEffects.RepairCost;
        internal string Key => key;
        public AstralEntry() { }
        internal AstralEntry(string provider, string identity) { this.provider = provider; this.identity = identity; key = provider + "\n" + identity; }
        internal void Bind(Pawn body, float power, AstralArtsProfileDef configuration, AstralArt allowed)
        { pawn = body; strength = power; profile = configuration; arts = allowed; account.Initialize(Capacity); }
        public void ExposeData()
        {
            Scribe_Values.Look(ref provider, "provider");
            Scribe_Values.Look(ref identity, "identity");
            Scribe_References.Look(ref pawn, "pawn", saveDestroyedThings: true);
            Scribe_Values.Look(ref strength, "strength");
            Scribe_Defs.Look(ref profile, "profile");
            Scribe_Deep.Look(ref account, "account");
            Scribe_Deep.Look(ref skills, "skills");
            Scribe_Deep.Look(ref spells, "spells");
            if (Scribe.mode == LoadSaveMode.LoadingVars) skills ??= new StarPaths.StarPathAccount();
            if (Scribe.mode == LoadSaveMode.LoadingVars) spells ??= new AstralSpellBook();
            if (Scribe.mode == LoadSaveMode.LoadingVars) key = provider + "\n" + identity;
        }
    }
}
