using System;
using HarmonyLib;
using LihuatLib.Core;
using Verse;

namespace LihuatLib.Mechanisms
{
    internal sealed class WorldDepartureScope : IDisposable
    {
        private static readonly Action<Pawn, DestroyMode> despawn = OriginalMethods.CreateTransfer<Action<Pawn, DestroyMode>>(
            AccessTools.DeclaredMethod(typeof(Pawn), nameof(Pawn.DeSpawn)));
        private static readonly Func<ThingOwner, Thing, bool> remove = OriginalMethods.CreateTransfer<Func<ThingOwner, Thing, bool>>(
            AccessTools.Method(typeof(ThingOwner), nameof(ThingOwner.Remove), new[] { typeof(Thing) }));
        private static readonly AccessTools.FieldRef<Thing, sbyte> mapIndex = AccessTools.FieldRefAccess<Thing, sbyte>("mapIndexOrState");
        [ThreadStatic] private static WorldDepartureScope current;
        private readonly WorldDepartureScope previous;
        private readonly Pawn pawn;
        private readonly Map source;
        private readonly ThingOwner sourceHolder;
        private readonly CellRect footprint;
        private readonly OperationScope operation;

        internal WorldDepartureScope(Pawn pawn)
        {
            this.pawn = pawn;
            source = pawn.MapHeld;
            sourceHolder = source != null ? pawn.holdingOwner : null;
            footprint = GenAdj.OccupiedRect(pawn.PositionHeld, pawn.Rotation, pawn.def.size);
            operation = new OperationScope(OperationKind.Transfer);
            previous = current;
            current = this;
        }

        internal static bool Complete(Thing thing, bool selected)
        {
            for (WorldDepartureScope scope = current; scope != null; scope = scope.previous)
            {
                if (!ReferenceEquals(scope.pawn, thing)) continue;
                // ExitMap also runs after a destination holder has already accepted the pawn.
                if (scope.source == null) break;
                if (thing.Spawned && thing.Map == scope.source) despawn(scope.pawn, DestroyMode.Vanish);
                if (scope.sourceHolder != null)
                {
                    if (RegistrationStorage.Holder(scope.sourceHolder, thing, false)) remove(scope.sourceHolder, thing);
                    RegistrationStorage.Holder(scope.sourceHolder, thing, true);
                    if (ReferenceEquals(thing.holdingOwner, scope.sourceHolder)) thing.holdingOwner = null;
                }
                if (!scope.source.Disposed
                    && MapTransferResidue.Contains(scope.source, thing, scope.footprint))
                    MapTransferResidue.Clear(scope.source, thing, scope.footprint);
                int index = mapIndex(thing);
                if (index >= 0 && index < Find.Maps.Count && ReferenceEquals(Find.Maps[index], scope.source)) mapIndex(thing) = -1;
                break;
            }
            return selected;
        }

        public void Dispose()
        {
            current = previous;
            operation.Dispose();
        }
    }
}
