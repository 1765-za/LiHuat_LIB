using System;
using Verse;

namespace LihuatLib.StarPaths
{
    public delegate void StarPathOutgoing(Pawn attacker, Pawn target, DamageChannel channel, ref float amount);

    public sealed class StarPathSubject : IExposable
    {
        private string provider, identity;
        private Pawn pawn;
        private StarPathTreeDef tree;
        private StarPathAccount account = new StarPathAccount();
        internal Func<Pawn, bool> Permission;
        internal Func<DamageChannel, float> Incoming;
        internal StarPathOutgoing Outgoing;
        internal Func<Hediff, bool> InjuryCost;
        internal bool ProviderSettlesGlobal;
        public bool GuaranteedHit { get; set; }
        internal StarPathEffects Effects = StarPathEffects.Empty;
        public Pawn Pawn => pawn;
        public StarPathTreeDef Tree => tree;
        public StarPathAccount Account => account;
        public int Revision { get; internal set; }
        internal string Key => provider + "\n" + identity;
        public StarPathSubject() { }
        internal StarPathSubject(string provider, string identity) { this.provider = provider; this.identity = identity; }
        internal void Bind(Pawn value, StarPathTreeDef definition, Func<Pawn, bool> permission, Func<DamageChannel, float> incoming,
            StarPathOutgoing outgoing, Func<Hediff, bool> injuryCost)
        { pawn = value; tree = definition; Permission = permission; Incoming = incoming; Outgoing = outgoing; InjuryCost = injuryCost; }
        public void ExposeData()
        {
            Scribe_Values.Look(ref provider, "provider");
            Scribe_Values.Look(ref identity, "identity");
            Scribe_References.Look(ref pawn, "pawn", saveDestroyedThings: true);
            Scribe_Defs.Look(ref tree, "tree");
            Scribe_Deep.Look(ref account, "account");
        }
    }
}
