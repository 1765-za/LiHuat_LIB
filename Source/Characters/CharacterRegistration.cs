using System;
using Verse;

namespace LihuatLib.Characters
{
    public sealed class CharacterRegistration
    {
        public string ProviderId { get; }
        public string CharacterId { get; }
        public string ProviderLabelKey { get; }
        public Pawn Pawn { get; }
        public Func<Pawn, AcceptanceReport> CanStore { get; }
        public Func<Pawn, AcceptanceReport> CanRelease { get; }
        public Action<Pawn, bool> StorageChanged { get; }

        public CharacterRegistration(string providerId, string characterId, string providerLabelKey, Pawn pawn,
            Func<Pawn, AcceptanceReport> canStore = null, Action<Pawn, bool> storageChanged = null, Func<Pawn, AcceptanceReport> canRelease = null)
        {
            if (string.IsNullOrWhiteSpace(providerId) || providerId.Contains("\n")) throw new ArgumentException("A provider needs a stable identity.", nameof(providerId));
            if (string.IsNullOrWhiteSpace(characterId) || characterId.Contains("\n")) throw new ArgumentException("A character needs a stable identity.", nameof(characterId));
            if (string.IsNullOrWhiteSpace(providerLabelKey)) throw new ArgumentException("A provider needs a localized label key.", nameof(providerLabelKey));
            ProviderId = providerId; CharacterId = characterId; ProviderLabelKey = providerLabelKey;
            Pawn = pawn ?? throw new ArgumentNullException(nameof(pawn)); CanStore = canStore; StorageChanged = storageChanged; CanRelease = canRelease;
        }
        internal string Key => ProviderId + "\n" + CharacterId;
    }
}
