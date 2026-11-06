using System;
using System.Collections.Generic;
using System.Linq;
using LihuatLib.Core;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace LihuatLib.WorldArts
{
    public sealed partial class WorldArtService : IExposable
    {
        private readonly Dictionary<string, WorldArtSubject> subjects = new(StringComparer.Ordinal);
        private readonly Dictionary<Pawn, WorldArtSubject> byPawn = new();
        private readonly Dictionary<Pawn, List<WorldArtSubject>> providers = new();
        private readonly Dictionary<Caravan, WorldFlightState> byCaravan = new();
        private List<WorldFlightState> flights = new();
        private List<WorldArtCast> casts = new();
        private List<WorldArtMapWork> mapWork = new();
        private Dictionary<string, WorldArtRange> ranges = new(StringComparer.Ordinal);
        public WorldArtSubject Find(Pawn pawn) => pawn != null && byPawn.TryGetValue(pawn, out var result) ? result : null;
        public WorldFlightState Flight(Caravan caravan) => caravan != null && byCaravan.TryGetValue(caravan, out var result) ? result : null;
        public WorldArtRange RangeFor(WorldArtSubject subject) => ranges.TryGetValue(subject.Id, out var range)
            ? range.Copy() : WorldArtRange.From(subject.Profile);

        public void SetRange(WorldArtSubject subject, WorldArtRange range)
        {
            LibraryCapabilities.RequirePublicationThread();
            if (Find(subject.Pawn) == subject) ranges[subject.Id] = range.Copy();
        }

        public void Register(WorldArtSubject subject)
        {
            LibraryCapabilities.Require(LibraryCapability.WorldArts); LibraryCapabilities.RequirePublicationThread();
            if (subjects.TryGetValue(subject.Id, out var previous))
            { providers[previous.Pawn].Remove(previous); Refresh(previous.Pawn); }
            subjects[subject.Id] = subject;
            if (!providers.TryGetValue(subject.Pawn, out var choices)) providers[subject.Pawn] = choices = new();
            choices.Add(subject);
            Refresh(subject.Pawn);
            foreach (AbilityDef ability in WorldArtDefs.Abilities)
                if (subject.Pawn.abilities.GetAbility(ability) == null) subject.Pawn.abilities.GainAbility(ability);
        }

        public void Unregister(string id)
        {
            LibraryCapabilities.RequirePublicationThread();
            if (subjects.Remove(id, out var subject))
            { providers[subject.Pawn].Remove(subject); Refresh(subject.Pawn); }
        }

        public void Unregister(WorldArtSubject subject)
        {
            if (subject != null && subjects.TryGetValue(subject.Id, out var current) && ReferenceEquals(current, subject)) Unregister(subject.Id);
        }

        private void Refresh(Pawn pawn)
        {
            WorldArtSubject selected = null;
            if (providers.TryGetValue(pawn, out var choices))
                foreach (var choice in choices)
                    if (selected == null || choice.Priority > selected.Priority
                        || choice.Priority == selected.Priority && string.CompareOrdinal(choice.Id, selected.Id) < 0) selected = choice;
            if (choices?.Count == 0) providers.Remove(pawn);
            if (selected == null) byPawn.Remove(pawn); else byPawn[pawn] = selected;
        }

        public bool Available(WorldArtSubject subject) => subject != null && subject.Permitted && Find(subject.Pawn) == subject
            && !subject.Pawn.Dead && !subject.Pawn.Destroyed && subject.Pawn.GetCaravan() is Caravan caravan && caravan.IsPlayerControlled;

        internal void Tick(int now)
        {
            for (int i = flights.Count - 1; i >= 0; i--)
            {
                var state = flights[i];
                if (state.Caravan == null || !state.Caravan.Spawned || state.Pawn?.GetCaravan() != state.Caravan)
                { if (state.Caravan != null) byCaravan.Remove(state.Caravan); flights.RemoveAt(i); continue; }
                if (state.Moving && !state.Paused) Advance(state);
            }
            for (int i = casts.Count - 1; i >= 0; i--)
            {
                var cast = casts[i];
                if (!cast.Resolved && now >= cast.ImpactAt) { cast.Resolved = true; Resolve(cast); }
                if (now >= cast.EndAt) casts.RemoveAt(i);
            }
            int remaining = 128;
            for (int i = mapWork.Count - 1; i >= 0 && remaining > 0; i--)
                if (mapWork[i].Advance(ref remaining)) mapWork.RemoveAt(i);
        }

        public void ExposeData()
        {
            Scribe_Collections.Look(ref flights, "worldArtFlights", LookMode.Deep);
            Scribe_Collections.Look(ref casts, "worldArtCasts", LookMode.Deep);
            Scribe_Collections.Look(ref mapWork, "worldArtMapWork", LookMode.Deep);
            Scribe_Collections.Look(ref ranges, "worldArtRanges", LookMode.Value, LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            { flights ??= new(); casts ??= new(); mapWork ??= new(); ranges ??= new(StringComparer.Ordinal); }
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                byCaravan.Clear();
                foreach (var state in flights) if (state.Caravan != null) byCaravan[state.Caravan] = state;
            }
        }
    }
}
