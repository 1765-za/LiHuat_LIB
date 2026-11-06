using System;
using RimWorld;
using Verse;

namespace LihuatLib.Characters
{
    public sealed partial class CharacterManager
    {
        private static AcceptanceReport Rejected(string key) => key.Translate().ToString();
        public AcceptanceReport CanStore(CharacterEntry entry)
        {
            if (busy) return Rejected("Lihuat_CharactersBusy");
            if (!Owns(entry) || !bindings.TryGetValue(entry.Key, out CharacterRegistration binding)) return Rejected("Lihuat_CharactersUnbound");
            Pawn pawn = entry.Pawn;
            if (pawn == null || pawn.Destroyed || PawnLife.Dead(pawn)) return Rejected("Lihuat_CharactersUnavailable");
            if (IsStored(pawn)) return Rejected("Lihuat_CharactersAlreadyStored");
            if (!pawn.Spawned || pawn.Map == null) return Rejected("Lihuat_CharactersNotOnMap");
            if (LihuatGame.Current.Domains.Contains(pawn) || LihuatGame.Current.Domains.OwnedBy(pawn) != null)
                return Rejected("Lihuat_CharactersInDomain");
            if (binding.CanStore != null) return binding.CanStore(pawn);
            return pawn.Faction == Faction.OfPlayer ? (AcceptanceReport)true : Rejected("Lihuat_CharactersNotPlayer");
        }
        public AcceptanceReport CanRelease(CharacterEntry entry, Map map, IntVec3 cell)
        {
            if (busy) return Rejected("Lihuat_CharactersBusy");
            if (!Owns(entry) || !bindings.ContainsKey(entry.Key)) return Rejected("Lihuat_CharactersUnbound");
            Pawn pawn = entry.Pawn;
            if (pawn == null || pawn.Destroyed || PawnLife.Dead(pawn)) return Rejected("Lihuat_CharactersUnavailable");
            if (!IsStored(pawn)) return Rejected("Lihuat_CharactersNotStored");
            if (map == null || !Verse.Find.Maps.Contains(map)) return Rejected("Lihuat_CharactersNoMap");
            if (!LihuatGame.Current.Domains.CanContain(pawn, map)) return Rejected("Lihuat_CharactersDomainDestination");
            foreach (IntVec3 occupied in GenAdj.OccupiedRect(cell, pawn.Rotation, pawn.def.size))
            {
                if (!occupied.InBounds(map) || !occupied.Standable(map) || occupied.Fogged(map)) return Rejected("Lihuat_CharactersInvalidCell");
                foreach (Thing thing in occupied.GetThingList(map)) if (thing is Pawn) return Rejected("Lihuat_CharactersOccupied");
            }
            return true;
        }
        private bool Authoritative(CharacterEntry entry) => Owns(entry) && ReferenceEquals(Find(entry.Pawn), entry)
            && bindings.TryGetValue(entry.Key, out CharacterRegistration binding) && ReferenceEquals(binding.Pawn, entry.Pawn);
    }
}
