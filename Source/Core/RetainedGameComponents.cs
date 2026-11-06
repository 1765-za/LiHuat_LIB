using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using HarmonyLib;
using Verse;

namespace LihuatLib.Core
{
    public static class RetainedGameComponents
    {
        private static readonly ConditionalWeakTable<Game, ComponentSet> games = new ConditionalWeakTable<Game, ComponentSet>();
        private static readonly Dictionary<Type, Action<Game, ComponentSet.Entry>> indexWriters = new Dictionary<Type, Action<Game, ComponentSet.Entry>>();
        private static readonly MethodInfo publishIndex = typeof(RetainedGameComponents).GetMethod(nameof(PublishIndex), BindingFlags.NonPublic | BindingFlags.Static);

        private static class Index<T> where T : GameComponent
        {
            internal static ConditionalWeakTable<Game, ComponentSet.Entry> Entries;
        }

        public static T Find<T>(Game game) where T : GameComponent
        {
            ConditionalWeakTable<Game, ComponentSet.Entry> index = Volatile.Read(ref Index<T>.Entries);
            return game != null && index != null && index.TryGetValue(game, out ComponentSet.Entry entry) ? entry.Component as T : null;
        }

        public static void Register(Game game, GameComponent component)
        {
            if (game == null) throw new ArgumentNullException(nameof(game));
            if (component == null) throw new ArgumentNullException(nameof(component));
            lock (games)
            {
                ComponentSet.Entry created = games.GetValue(game, _ => new ComponentSet()).Register(component);
                if (created == null) return;
                Type type = component.GetType();
                if (!indexWriters.TryGetValue(type, out Action<Game, ComponentSet.Entry> publish))
                {
                    publish = (Action<Game, ComponentSet.Entry>)Delegate.CreateDelegate(typeof(Action<Game, ComponentSet.Entry>), publishIndex.MakeGenericMethod(type));
                    indexWriters.Add(type, publish);
                }
                publish(game, created);
            }
        }

        private static void PublishIndex<T>(Game game, ComponentSet.Entry entry) where T : GameComponent
        {
            var index = Volatile.Read(ref Index<T>.Entries);
            if (index == null)
            {
                index = new ConditionalWeakTable<Game, ComponentSet.Entry>();
                Volatile.Write(ref Index<T>.Entries, index);
            }
            index.Add(game, entry);
        }
        public static void Maintain(Game game)
        {
            if (game == null || Scribe.mode != LoadSaveMode.Inactive && Scribe.mode != LoadSaveMode.Saving) return;
            if (games.TryGetValue(game, out ComponentSet set)) set.Restore(game);
        }
        public static long Repairs(Game game) => game != null && games.TryGetValue(game, out ComponentSet set) ? set.Repairs : 0;

        private sealed class ComponentSet
        {
            internal sealed class Entry
            {
                internal GameComponent Component;
                internal int Slot = -1;
            }
            private static readonly AccessTools.FieldRef<List<GameComponent>, int> version =
                AccessTools.FieldRefAccess<List<GameComponent>, int>("_version");
            private readonly Dictionary<Type, Entry> entries = new Dictionary<Type, Entry>();
            private List<GameComponent> observed;
            private int observedVersion;
            private int observedCount;
            internal long Repairs { get; private set; }
            internal Entry Register(GameComponent component)
            {
                Type type = component.GetType();
                if (entries.TryGetValue(type, out Entry entry))
                {
                    // Loading constructs the saved component after Game's empty placeholder.
                    if (Scribe.mode != LoadSaveMode.LoadingVars) return null;
                    entry.Component = component;
                    observed = null;
                    return null;
                }
                entry = new Entry { Component = component };
                entries.Add(type, entry);
                observed = null;
                return entry;
            }

            internal void Restore(Game game)
            {
                List<GameComponent> list = game.components;
                bool intact = list != null && ReferenceEquals(list, observed)
                    && version(list) == observedVersion && list.Count == observedCount;
                if (intact)
                {
                    foreach (Entry entry in entries.Values)
                        if (entry.Slot < 0 || entry.Slot >= list.Count || !ReferenceEquals(list[entry.Slot], entry.Component))
                        { intact = false; break; }
                    if (intact) return;
                }
                if (list == null) game.components = list = new List<GameComponent>();
                foreach (Entry entry in entries.Values)
                {
                    int first = -1;
                    for (int i = 0; i < list.Count; i++)
                    {
                        GameComponent item = list[i];
                        if (item == null || item.GetType() != entry.Component.GetType()) continue;
                        if (first < 0 && ReferenceEquals(item, entry.Component)) { first = i; continue; }
                        list.RemoveAt(i--);
                        Repairs++;
                    }
                    if (first >= 0) continue;
                    if (entry.Slot >= 0 && entry.Slot < list.Count && list[entry.Slot] == null) list[entry.Slot] = entry.Component;
                    else list.Insert(entry.Slot < 0 ? list.Count : Math.Min(entry.Slot, list.Count), entry.Component);
                    Repairs++;
                }
                foreach (Entry entry in entries.Values) entry.Slot = list.IndexOf(entry.Component);
                observed = list;
                observedVersion = version(list);
                observedCount = list.Count;
            }
        }
    }
}
