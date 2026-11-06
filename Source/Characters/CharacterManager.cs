using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using LihuatLib.Mechanisms;
using Verse;

namespace LihuatLib.Characters
{
    public sealed partial class CharacterManager : IExposable
    {
        private readonly IThingHolder owner;
        private ThingOwner<Pawn> stored;
        private List<CharacterEntry> entries = new List<CharacterEntry>();
        private ReadOnlyCollection<CharacterEntry> view;
        private readonly Dictionary<string, CharacterEntry> byId = new Dictionary<string, CharacterEntry>(StringComparer.Ordinal);
        private readonly Dictionary<Pawn, CharacterEntry> byPawn = new Dictionary<Pawn, CharacterEntry>(ReferenceIdentity<Pawn>.Instance);
        private readonly Dictionary<string, CharacterRegistration> bindings = new Dictionary<string, CharacterRegistration>(StringComparer.Ordinal);
        private bool busy;
        public int Revision { get; private set; }
        public IReadOnlyList<CharacterEntry> Entries => view;
        internal ThingOwner Contents => stored;
        public CharacterManager(IThingHolder owner)
        { this.owner = owner; stored = new ThingOwner<Pawn>(owner, false, LookMode.Deep); view = entries.AsReadOnly(); }
        public bool IsStored(Pawn pawn) => pawn != null && ReferenceEquals(pawn.holdingOwner, stored);
        public bool IsRegistered(CharacterEntry entry) => Owns(entry) && bindings.ContainsKey(entry.Key);
        private bool Owns(CharacterEntry entry) => entry != null && byId.TryGetValue(entry.Key, out CharacterEntry found) && ReferenceEquals(found, entry);
        public CharacterEntry Find(Pawn pawn) => pawn != null && byPawn.TryGetValue(pawn, out CharacterEntry entry) ? entry : null;

        public CharacterEntry Register(CharacterRegistration registration)
        {
            LibraryCapabilities.Require(LibraryCapability.CharacterManagement); LibraryCapabilities.RequirePublicationThread();
            if (registration == null) throw new ArgumentNullException(nameof(registration));
            if (byPawn.TryGetValue(registration.Pawn, out CharacterEntry other) && other.Key != registration.Key)
                throw new InvalidOperationException("The pawn is already registered to another character identity.");
            if (byId.TryGetValue(registration.Key, out CharacterEntry entry))
            {
                if (!ReferenceEquals(entry.Pawn, registration.Pawn))
                {
                    if (IsStored(entry.Pawn)) throw new InvalidOperationException("A stored character cannot be replaced by another body.");
                    if (entry.Pawn != null) byPawn.Remove(entry.Pawn);
                }
                entry.Update(registration);
            }
            else { entry = new CharacterEntry(registration); entries.Add(entry); byId.Add(entry.Key, entry); }
            byPawn[registration.Pawn] = entry; bindings[entry.Key] = registration; Revision++;
            return entry;
        }
        public void Unregister(string providerId, string characterId)
        {
            LibraryCapabilities.RequirePublicationThread();
            string key = providerId + "\n" + characterId;
            if (!byId.TryGetValue(key, out CharacterEntry entry)) return;
            bindings.Remove(key);
            if (!IsStored(entry.Pawn)) { entries.Remove(entry); byId.Remove(key); if (entry.Pawn != null) byPawn.Remove(entry.Pawn); }
            Revision++;
        }
        public void ExposeData()
        {
            Scribe_Deep.Look(ref stored, "stored", owner);
            Scribe_Collections.Look(ref entries, "entries", LookMode.Deep);
            if (stored == null) stored = new ThingOwner<Pawn>(owner, false, LookMode.Deep);
            if (entries == null) entries = new List<CharacterEntry>();
            if (Scribe.mode != LoadSaveMode.PostLoadInit) return;
            byId.Clear(); byPawn.Clear(); bindings.Clear(); view = entries.AsReadOnly();
            foreach (CharacterEntry entry in entries)
            {
                if (entry.Pawn == null || string.IsNullOrEmpty(entry.ProviderId) || string.IsNullOrEmpty(entry.CharacterId)) continue;
                byId.Add(entry.Key, entry); byPawn.Add(entry.Pawn, entry);
            }
            Revision++;
        }
    }
}
