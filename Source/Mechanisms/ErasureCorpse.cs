using System;
using System.Linq;
using HarmonyLib;
using Verse;

namespace LihuatLib.Mechanisms
{
    internal static class ErasureCorpse
    {
        private static readonly Action<Thing, DestroyMode> destroy = OriginalMethods.Create<Action<Thing, DestroyMode>>(
            AccessTools.Method(typeof(Thing), nameof(Thing.Destroy)), deathGraph: true);
        private static readonly AccessTools.FieldRef<Thing, sbyte> mapIndex = AccessTools.FieldRefAccess<Thing, sbyte>("mapIndexOrState");

        public static void Retire(Corpse corpse)
        {
            if (corpse == null) return;
            ThingOwner holder = corpse.holdingOwner;
            CellRect footprint = MapRegistrationStorage.Footprint(corpse);
            // The occupant must remain available until selection and draw registrations are gone.
            ErasureDeparture.Remove(corpse);
            RegistrationStorage.Process(corpse, holder, footprint, true);
            corpse.holdingOwner = null;
            if (mapIndex(corpse) != -2) destroy(corpse, DestroyMode.Vanish);
            if (mapIndex(corpse) != -2 || corpse.holdingOwner != null || RegistrationStorage.Process(corpse, holder, footprint, false))
                throw new InvalidOperationException("Erased corpse retained registration: " + corpse.ThingID);
        }
    }
}
