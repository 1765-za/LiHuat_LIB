using System;
using LihuatLib.Mechanisms;
using Verse;

namespace LihuatLib.Characters
{
    public sealed partial class CharacterManager
    {
        public AcceptanceReport Store(CharacterEntry entry)
        {
            LibraryCapabilities.Require(LibraryCapability.CharacterManagement); LibraryCapabilities.RequirePublicationThread();
            if (!ReferenceEquals(LihuatGame.Current?.Characters, this)) return Rejected("Lihuat_CharactersUnbound");
            AcceptanceReport admission;
            try { admission = CanStore(entry); }
            catch (Exception error) { Log.Warning("[LihuatLib.Characters] Admission failed: " + error); return Rejected("Lihuat_CharactersOperationFailed"); }
            if (!admission) return admission;
            Pawn pawn = entry.Pawn; Map source = pawn.Map; IntVec3 cell = pawn.Position; Rot4 rotation = pawn.Rotation;
            CharacterRegistration binding = bindings[entry.Key];
            busy = true;
            try
            {
                using (new OperationScope(OperationKind.Transfer))
                {
                    if (!MapTransfer.Store(pawn, stored) || !IsStored(pawn) || !RegistrationStorage.Holder(stored, pawn, false))
                        throw new InvalidOperationException("Character storage did not acquire the original body.");
                    binding.StorageChanged?.Invoke(pawn, true);
                    if (!Authoritative(entry) || !IsStored(pawn) || pawn.Spawned || PawnLife.Dead(pawn))
                        throw new InvalidOperationException("Character storage changed before commitment.");
                    entry.LastMap = source; entry.LastCell = cell;
                }
                Revision++; return true;
            }
            catch (Exception error)
            {
                try
                {
                    using (new OperationScope(OperationKind.Transfer))
                    {
                        if (!PawnMapPresence.Registered(pawn, source)) MapTransfer.Place(pawn, source, cell, rotation);
                        binding.StorageChanged?.Invoke(pawn, false);
                    }
                }
                catch (Exception rollback) { Log.Error("[LihuatLib.Characters] Storage rollback failed: " + rollback); }
                Log.Warning("[LihuatLib.Characters] Storage rejected: " + error);
                Revision++; return Rejected("Lihuat_CharactersOperationFailed");
            }
            finally { busy = false; }
        }

        public AcceptanceReport Release(CharacterEntry entry, Map map, IntVec3 cell)
        {
            LibraryCapabilities.Require(LibraryCapability.CharacterManagement); LibraryCapabilities.RequirePublicationThread();
            if (!ReferenceEquals(LihuatGame.Current?.Characters, this)) return Rejected("Lihuat_CharactersUnbound");
            AcceptanceReport admission = CanRelease(entry, map, cell);
            if (!admission) return admission;
            Pawn pawn = entry.Pawn; CharacterRegistration binding = bindings[entry.Key];
            try
            {
                if (binding.CanRelease != null) { admission = binding.CanRelease(pawn); if (!admission) return admission; }
                if (!LihuatGame.Current.Lifecycle.AllowsRegistration(pawn)) return Rejected("Lihuat_CharactersReturnBlocked");
            }
            catch (Exception error) { Log.Warning("[LihuatLib.Characters] Return admission failed: " + error); return Rejected("Lihuat_CharactersOperationFailed"); }
            busy = true;
            try
            {
                using (new OperationScope(OperationKind.Transfer))
                {
                    MapTransfer.Place(pawn, map, cell, pawn.Rotation);
                    binding.StorageChanged?.Invoke(pawn, false);
                    if (!Authoritative(entry) || !PawnMapPresence.Registered(pawn, map) || PawnLife.Dead(pawn))
                        throw new InvalidOperationException("Character return changed before commitment.");
                    entry.LastMap = map; entry.LastCell = cell;
                }
                Revision++; return true;
            }
            catch (Exception error)
            {
                try
                {
                    using (new OperationScope(OperationKind.Transfer))
                    {
                        if (!IsStored(pawn) && !MapTransfer.Store(pawn, stored)) throw new InvalidOperationException("Character could not return to storage.");
                        binding.StorageChanged?.Invoke(pawn, true);
                    }
                }
                catch (Exception rollback) { Log.Error("[LihuatLib.Characters] Return rollback failed: " + rollback); }
                Log.Warning("[LihuatLib.Characters] Return rejected: " + error);
                Revision++; return Rejected("Lihuat_CharactersOperationFailed");
            }
            finally { busy = false; }
        }
    }
}
