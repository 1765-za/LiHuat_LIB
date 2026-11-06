using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Verse;

namespace LihuatLib.Mechanisms
{
    public static class PinnedHediffs
    {
        private sealed class Owner
        {
            public Pawn Pawn;
            public HediffSet Set;
            public int RepairAt;
            public readonly List<Entry> Entries = new List<Entry>();
        }
        private sealed class Entry
        {
            public Owner Owner;
            public Hediff Hediff;
            public HediffDef Def;
            public BodyPartRecord Part;
            public float Severity;
            public Dictionary<FieldInfo, object> Identity;
            public int Slot = -1;
        }
        private static readonly AccessTools.FieldRef<Hediff, float> severity = AccessTools.FieldRefAccess<Hediff, float>("severityInt");
        private static readonly FieldInfo hediffList = AccessTools.Field(typeof(HediffSet), "hediffs");
        private static readonly Dictionary<Type, FieldInfo[]> identityFields = new Dictionary<Type, FieldInfo[]>();
        private static Game game;
        private static ConditionalWeakTable<Pawn, Owner> owners = new ConditionalWeakTable<Pawn, Owner>();
        private static ConditionalWeakTable<HediffSet, Owner> sets = new ConditionalWeakTable<HediffSet, Owner>();
        private static ConditionalWeakTable<List<Hediff>, Owner> lists = new ConditionalWeakTable<List<Hediff>, Owner>();
        private static ConditionalWeakTable<Hediff, Entry> entries = new ConditionalWeakTable<Hediff, Entry>();
        [ThreadStatic] private static bool writing;
        public static long BlockedRemovals { get; private set; }
        public static long BlockedWrites { get; private set; }
        public static long Restored { get; private set; }

        private static void CurrentGame()
        {
            if (ReferenceEquals(game, Current.Game)) return;
            game = Current.Game;
            owners = new ConditionalWeakTable<Pawn, Owner>(); sets = new ConditionalWeakTable<HediffSet, Owner>();
            lists = new ConditionalWeakTable<List<Hediff>, Owner>(); entries = new ConditionalWeakTable<Hediff, Entry>();
        }

        public static void Pin(Pawn pawn, Hediff hediff)
        {
            LibraryCapabilities.Require(LibraryCapability.HediffProtection);
            if (pawn?.health?.hediffSet == null || hediff?.def == null) return;
            CurrentGame();
            if (entries.TryGetValue(hediff, out _)) return;
            if (!owners.TryGetValue(pawn, out Owner owner))
            {
                owner = new Owner { Pawn = pawn, Set = pawn.health.hediffSet };
                owners.Add(pawn, owner); sets.Add(owner.Set, owner);
                lists.Add(owner.Set.hediffs, owner);
                HediffMutationGuard.Discover();
            }
            var identity = new Dictionary<FieldInfo, object>();
            foreach (FieldInfo field in IdentityFields(hediff.GetType())) identity.Add(field, field.GetValue(hediff));
            var entry = new Entry { Owner = owner, Hediff = hediff, Def = hediff.def, Part = hediff.Part,
                Severity = Math.Max(.001f, severity(hediff)), Identity = identity };
            owner.Entries.Add(entry); entries.Add(hediff, entry);
            Reconcile(pawn, true);
        }

        private static FieldInfo[] IdentityFields(Type type)
        {
            if (identityFields.TryGetValue(type, out FieldInfo[] cached)) return cached;
            var fields = new List<FieldInfo>();
            for (Type current = type; current != null && current.Assembly != typeof(Hediff).Assembly; current = current.BaseType)
                foreach (FieldInfo field in current.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    if (field.FieldType == typeof(string)) fields.Add(field);
            return identityFields[type] = fields.ToArray();
        }

        public static bool Contains(Hediff hediff) => game == Current.Game && hediff != null && entries.TryGetValue(hediff, out _);
        internal static HediffSet OwnerOf(List<Hediff> list) => game == Current.Game && list != null
            && lists.TryGetValue(list, out Owner owner) && ReferenceEquals(owner.Set.hediffs, list) ? owner.Set : null;
        internal static void BindHealth(Pawn pawn, HediffSet set)
        {
            if (game != Current.Game || !owners.TryGetValue(pawn, out Owner owner) || owner.Set == set) return;
            foreach (Entry entry in owner.Entries)
                if (!set.hediffs.Contains(entry.Hediff)) set.hediffs.Add(entry.Hediff);
            sets.Remove(owner.Set);
            lists.Remove(owner.Set.hediffs);
            owner.Set = set;
            sets.Add(set, owner);
            lists.Add(set.hediffs, owner);
            foreach (Entry entry in owner.Entries) entry.Slot = -1;
        }
        private static void Unpin(Hediff hediff)
        {
            if (game != Current.Game || hediff == null || !entries.TryGetValue(hediff, out Entry entry)) return;
            entry.Owner.Entries.Remove(entry);
            entries.Remove(hediff);
        }
        internal static void AcceptDeveloperChanges(Pawn pawn)
        {
            if (game != Current.Game || pawn?.health?.hediffSet == null || !owners.TryGetValue(pawn, out Owner owner)) return;
            for (int i = owner.Entries.Count - 1; i >= 0; i--)
            {
                Entry entry = owner.Entries[i];
                if (!pawn.health.hediffSet.hediffs.Contains(entry.Hediff)) { Unpin(entry.Hediff); continue; }
                entry.Def = entry.Hediff.def; entry.Part = entry.Hediff.Part; entry.Severity = severity(entry.Hediff);
                foreach (FieldInfo field in IdentityFields(entry.Hediff.GetType())) entry.Identity[field] = field.GetValue(entry.Hediff);
            }
        }
        public static bool AllowRemoval(Hediff hediff)
        {
            if (DeveloperExecution.Active) { Unpin(hediff); return true; }
            if (!Contains(hediff)) return true;
            BlockedRemovals++; return false;
        }
        public static bool AllowSeverity(Hediff hediff, float value)
        {
            if (DeveloperExecution.Active) return true;
            if (game != Current.Game || hediff == null || !entries.TryGetValue(hediff, out Entry entry) || value == entry.Severity) return true;
            BlockedWrites++; return false;
        }
        private static bool Protected(List<Hediff> list, Hediff hediff) => !DeveloperExecution.Active && game == Current.Game && list != null
            && lists.TryGetValue(list, out Owner owner) && ReferenceEquals(owner.Set.hediffs, list)
            && hediff != null && entries.TryGetValue(hediff, out Entry entry) && ReferenceEquals(entry.Owner, owner);

        public static void Reconcile(Pawn pawn) => Reconcile(pawn, false);
        private static void Reconcile(Pawn pawn, bool explicitPin)
        {
            if (DeveloperExecution.Active && !explicitPin) return;
            if (game != Current.Game || pawn == null || !owners.TryGetValue(pawn, out Owner owner) || writing) return;
            writing = true;
            try
            {
                if (pawn.health == null) return;
                if (pawn.health.hediffSet != owner.Set) { pawn.health.hediffSet = owner.Set; Restored++; }
                if (owner.Set.pawn != pawn) { owner.Set.pawn = pawn; Restored++; }
                if (owner.Set.hediffs == null) { owner.Set.hediffs = new List<Hediff>(); Restored++; }
                List<Hediff> list = owner.Set.hediffs;
                if (!lists.TryGetValue(list, out _)) lists.Add(list, owner);
                bool changed = false;
                foreach (Entry entry in owner.Entries)
                {
                    Hediff hediff = entry.Hediff;
                    if (hediff.pawn != pawn || hediff.def != entry.Def || hediff.Part != entry.Part || severity(hediff) != entry.Severity)
                    { hediff.pawn = pawn; hediff.def = entry.Def; hediff.Part = entry.Part; severity(hediff) = entry.Severity; changed = true; }
                    foreach (KeyValuePair<FieldInfo, object> field in entry.Identity)
                        if (!Equals(field.Key.GetValue(hediff), field.Value)) { field.Key.SetValue(hediff, field.Value); changed = true; }
                    if ((uint)entry.Slot >= (uint)list.Count || !ReferenceEquals(list[entry.Slot], hediff))
                        entry.Slot = list.IndexOf(hediff);
                    if (entry.Slot < 0) { entry.Slot = list.Count; list.Add(hediff); changed = true; }
                }
                if (changed) { owner.Set.DirtyCache(); Restored++; }
            }
            finally { writing = false; }
        }

        public static void Maintain(Pawn pawn)
        {
            if (game != Current.Game || pawn == null || !owners.TryGetValue(pawn, out Owner owner)) return;
            int now = Find.TickManager.TicksGame;
            if (now < owner.RepairAt) return;
            owner.RepairAt = now + 30;
            Reconcile(pawn);
        }

        public static bool BlocksWrite(FieldInfo field, object target, object value)
        {
            if (DeveloperExecution.Active) return false;
            if (writing || game != Current.Game || field == null || target == null) return false;
            bool blocked = false;
            if (target is Hediff hediff && entries.TryGetValue(hediff, out Entry entry))
            {
                if (entry.Identity.TryGetValue(field, out object identity)) blocked = !Equals(identity, value);
                else if (field.Name == "pawn") blocked = !ReferenceEquals(value, entry.Owner.Pawn);
                else if (field.Name == "def") blocked = !ReferenceEquals(value, entry.Def);
                else if (field.Name == "part") blocked = !ReferenceEquals(value, entry.Part);
                else if (field.Name == "severityInt") blocked = !(value is float number) || number != entry.Severity;
            }
            else if (target is HediffSet set && sets.TryGetValue(set, out Owner owner))
            {
                if (field.Name == "hediffs") blocked = !(value is List<Hediff> replacement) || owner.Entries.Any(e => !replacement.Contains(e.Hediff));
                else if (field.Name == "pawn") blocked = !ReferenceEquals(value, owner.Pawn);
            }
            if (blocked) BlockedWrites++;
            return blocked;
        }

        public static bool Remove(List<Hediff> list, Hediff hediff)
        {
            if (Protected(list, hediff)) { BlockedRemovals++; return false; }
            return list.Remove(hediff);
        }
        public static void RemoveAt(List<Hediff> list, int index)
        {
            if (Protected(list, list[index])) { BlockedRemovals++; return; }
            list.RemoveAt(index);
        }
        private static bool HasPinnedEntries(List<Hediff> list) => !DeveloperExecution.Active && game == Current.Game && list != null
            && lists.TryGetValue(list, out Owner owner) && ReferenceEquals(owner.Set.hediffs, list) && owner.Entries.Count != 0;
        public static void Clear(List<Hediff> list)
        {
            if (!HasPinnedEntries(list)) { list.Clear(); return; }
            RemovePinned(list, _ => true);
        }
        public static int RemoveAll(List<Hediff> list, Predicate<Hediff> predicate)
        {
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));
            return RemovePinned(list, predicate);
        }
        private static int RemovePinned(List<Hediff> list, Predicate<Hediff> predicate) =>
            list.RemoveAll(h => { if (Protected(list, h)) { BlockedRemovals++; return false; } return predicate(h); });
        public static void RemoveRange(List<Hediff> list, int index, int count)
        {
            if (index < 0 || count < 0 || index > list.Count - count) throw new ArgumentOutOfRangeException();
            for (int i = index + count - 1; i >= index; i--) RemoveAt(list, i);
        }
        public static void ReplaceItem(List<Hediff> list, int index, Hediff value)
        {
            if (!AllowReplacement(list, index, value)) return;
            list[index] = value;
        }
        internal static bool AllowReplacement(List<Hediff> list, int index, Hediff value)
        {
            if (!Protected(list, list[index]) || ReferenceEquals(list[index], value)) return true;
            BlockedRemovals++; return false;
        }
        public static void ReplaceList(HediffSet set, List<Hediff> value)
        {
            if (BlocksWrite(hediffList, set, value)) return;
            set.hediffs = value;
            Reconcile(set.pawn);
        }
    }
}
