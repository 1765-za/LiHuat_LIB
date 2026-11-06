using Verse;

namespace LihuatLib.Characters
{
    public sealed class CharacterEntry : IExposable
    {
        private string providerId, characterId, providerLabelKey;
        private Pawn pawn;
        internal Map LastMap;
        internal IntVec3 LastCell = IntVec3.Invalid;
        public string ProviderId => providerId;
        public string CharacterId => characterId;
        public string ProviderLabelKey => providerLabelKey;
        public Pawn Pawn => pawn;
        internal string Key => providerId + "\n" + characterId;
        public CharacterEntry() { }
        internal CharacterEntry(CharacterRegistration registration)
        { providerId = registration.ProviderId; characterId = registration.CharacterId; Update(registration); }
        internal void Update(CharacterRegistration registration) { pawn = registration.Pawn; providerLabelKey = registration.ProviderLabelKey; }
        public void ExposeData()
        {
            Scribe_Values.Look(ref providerId, "provider"); Scribe_Values.Look(ref characterId, "character");
            Scribe_Values.Look(ref providerLabelKey, "providerLabelKey");
            Scribe_References.Look(ref pawn, "pawn", saveDestroyedThings: true);
            Scribe_References.Look(ref LastMap, "lastMap"); Scribe_Values.Look(ref LastCell, "lastCell", IntVec3.Invalid);
        }
    }
}
