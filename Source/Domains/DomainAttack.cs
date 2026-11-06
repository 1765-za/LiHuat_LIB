using System;
using Verse;

namespace LihuatLib.Domains
{
    public static class DomainAttack
    {
        public static bool CanAffect(string domainId, Pawn target, bool guaranteedHit)
        {
            DomainArea area = DomainAPI.Find(domainId);
            if (area == null || area.Ending || area.Closed || !LihuatGame.Current.Domains.Covers(area, target)) return false;
            if (LihuatGame.Current.Counters.BlocksAbilities(area.Owner)) return false;
            if (target == area.Owner && !area.AffectsOwner) return false;
            if (guaranteedHit && (LihuatGame.Current.Counters.Signing(target) || LihuatGame.Current.Domains.IsImmune(target))) return false;
            return true;
        }
        public static bool Execute(string domainId, Pawn target, bool guaranteedHit, Action<Pawn> effect)
        {
            if (effect == null) throw new ArgumentNullException(nameof(effect));
            if (!CanAffect(domainId, target, guaranteedHit)) return false;
            effect(target); return true;
        }
    }
}
