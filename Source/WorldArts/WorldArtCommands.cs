using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace LihuatLib.WorldArts
{
    internal static class WorldArtCommands
    {
        internal static IEnumerable<Command> For(Ability ability, WorldArtSubject subject, WorldArtService service)
        {
            Caravan caravan = subject.Pawn.GetCaravan();
            var state = service.Flight(caravan);
            Texture2D icon = ContentFinder<Texture2D>.Get(ability.def.iconPath);
            if (ability.def != WorldArtDefs.Lihuat_StarPower)
            {
                WorldArtKind kind = ability.def == WorldArtDefs.Lihuat_Opening ? WorldArtKind.Opening : WorldArtKind.Starfall;
                float cost = kind == WorldArtKind.Opening ? subject.Profile.clearingCost : subject.Profile.impactCost;
                WorldArtRange range = service.RangeFor(subject);
                var command = Action(ability.def.LabelCap, ability.def.description + "\n\n"
                    + (kind == WorldArtKind.Opening ? "Lihuat_WorldArtRectangle".Translate(range.Width, range.Length)
                        : "Lihuat_WorldArtRadius".Translate(range.Radius)) + "\n"
                    + "Lihuat_WorldArtCost".Translate(cost, subject.ResourceKey.Translate()), icon,
                    () => Find.WindowStack.Add(new WorldArtRangeWindow(service, subject, kind, icon)));
                if (subject.Available < cost) command.Disable("Lihuat_WorldArtResourceLow".Translate(subject.ResourceKey.Translate()));
                yield return command;
                yield break;
            }
            yield return new Command_Toggle
            {
                defaultLabel = ability.def.LabelCap,
                defaultDesc = "Lihuat_WorldFlightDescription".Translate(subject.Profile.flightTilesPerSecond,
                    subject.Profile.flightCostPerSecond, subject.ResourceKey.Translate()),
                icon = icon,
                isActive = () => service.Flight(caravan)?.Enabled == true,
                toggleAction = () =>
                {
                    var current = service.Flight(caravan);
                    if (current?.Enabled != true) service.EnableFlight(subject);
                    else if (!service.Land(current)) Messages.Message("Lihuat_WorldFlightCannotLand".Translate(), MessageTypeDefOf.RejectInput, false);
                }
            };
            if (state?.Enabled != true) yield break;
            var move = Action("Lihuat_WorldFlightMove".Translate(), "Lihuat_WorldFlightMoveDesc".Translate(), icon, () =>
                Find.WorldTargeter.BeginTargeting(target => service.StartFlight(state, target.Tile), true, icon,
                    canSelectTarget: target => WorldArtGeometry.Valid(target.Tile), showCancelButton: true));
            if (state.Moving) move.Disable("Lihuat_WorldFlightEnRoute".Translate());
            yield return move;
            if (state.Moving)
                yield return new Command_Toggle { defaultLabel = "Lihuat_WorldFlightPause".Translate(),
                    defaultDesc = "Lihuat_WorldFlightPauseDesc".Translate(), icon = TexCommand.PauseCaravan,
                    isActive = () => state.Paused, toggleAction = () => state.Paused = !state.Paused };
            bool orbit = caravan.Tile.Layer.Def == PlanetLayerDefOf.Orbit;
            PlanetLayerDef destination = orbit ? PlanetLayerDefOf.Surface : PlanetLayerDefOf.Orbit;
            if (subject.Profile.allowOrbit && Find.WorldGrid.PlanetLayers.Values.Any(layer => layer.Def == destination))
            {
                var changeLayer = Action((orbit ? "Lihuat_WorldFlightDescend" : "Lihuat_WorldFlightAscend").Translate(),
                    "Lihuat_WorldFlightLayerDesc".Translate(), icon, () => service.ChangeLayer(state, destination));
                if (state.Moving) changeLayer.Disable("Lihuat_WorldFlightEnRoute".Translate());
                yield return changeLayer;
            }
        }

        private static Command_Action Action(string label, string description, Texture2D icon, System.Action action)
            => new() { defaultLabel = label, defaultDesc = description, icon = icon, action = action };
    }
}
