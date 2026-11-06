using System;
using System.Collections.Generic;
using Verse;

namespace LihuatLib.AstralArts
{
    public sealed class AstralSpellCooldown : IExposable
    {
        internal AstralSpellDef Spell;
        internal int Until;
        public AstralSpellCooldown() { }
        public void ExposeData() { Scribe_Defs.Look(ref Spell, "spell"); Scribe_Values.Look(ref Until, "until"); }
    }

    public sealed class AstralSpellBook : IExposable
    {
        private List<AstralSpellCooldown> cooldowns = new();
        private readonly Dictionary<AstralSpellDef, AstralSpellCooldown> bySpell = new();
        public int ReadyAt(AstralSpellDef spell) => bySpell.TryGetValue(spell, out var value) ? value.Until : 0;
        internal bool Cooling(int now)
        {
            foreach (AstralSpellCooldown cooldown in cooldowns) if (cooldown.Until > now) return true;
            return false;
        }
        internal void Start(AstralSpellDef spell, int now, float seconds)
        {
            if (!bySpell.TryGetValue(spell, out var value))
            { value = new AstralSpellCooldown { Spell = spell }; cooldowns.Add(value); bySpell.Add(spell, value); }
            value.Until = checked(now + (int)Math.Ceiling(seconds * 60f));
        }
        public void ExposeData()
        {
            Scribe_Collections.Look(ref cooldowns, "cooldowns", LookMode.Deep);
            if (Scribe.mode != LoadSaveMode.PostLoadInit) return;
            bySpell.Clear();
            foreach (AstralSpellCooldown cooldown in cooldowns) bySpell.Add(cooldown.Spell, cooldown);
        }
    }
}
