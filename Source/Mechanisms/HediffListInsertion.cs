using System;
using System.Collections.Generic;
using System.Linq;
using LihuatLib.StarBlood;
using Verse;

namespace LihuatLib.Mechanisms
{
    public static class HediffListInsertion
    {
        public static long Rejected { get; private set; }
        private static StarBloodSubject Subject(List<Hediff> list, Hediff value)
        {
            if (value is not Hediff_MissingPart || list == null) return null;
            HediffSet set = PinnedHediffs.OwnerOf(list);
            if (set == null && value.pawn?.health?.hediffSet is HediffSet linked && ReferenceEquals(linked.hediffs, list)) set = linked;
            StarBloodSubject subject = StarBloodRegistry.For(set?.pawn)?.Subject;
            return subject?.Enabled == true && subject.MissingPartAdmission != null ? subject : null;
        }
        private static bool Admit(List<Hediff> list, Hediff value, out StarBloodSubject subject)
        {
            subject = Subject(list, value);
            if (subject == null || subject.MissingPartAdmission(value)) return true;
            Rejected++; return false;
        }
        public static void Add(List<Hediff> list, Hediff value)
        {
            if (!Admit(list, value, out var subject)) return;
            list.Add(value);
            subject?.SettleWounds?.Invoke();
        }
        public static void Insert(List<Hediff> list, int index, Hediff value)
        {
            if ((uint)index > (uint)list.Count) throw new ArgumentOutOfRangeException(nameof(index));
            if (!Admit(list, value, out var subject)) return;
            list.Insert(index, value);
            subject?.SettleWounds?.Invoke();
        }
        public static void AddRange(List<Hediff> list, IEnumerable<Hediff> values) => InsertRange(list, list.Count, values);
        public static void InsertRange(List<Hediff> list, int index, IEnumerable<Hediff> values)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            if ((uint)index > (uint)list.Count) throw new ArgumentOutOfRangeException(nameof(index));
            StarBloodSubject owner = StarBloodRegistry.For(PinnedHediffs.OwnerOf(list)?.pawn)?.Subject;
            if (owner?.Enabled != true || owner.MissingPartAdmission == null) { list.InsertRange(index, values); return; }
            // The snapshot preserves self-insertion semantics while admission may settle another injury in this list.
            Hediff[] snapshot = values.ToArray();
            var accepted = new List<Hediff>(snapshot.Length);
            StarBloodSubject settled = null;
            foreach (Hediff value in snapshot)
                if (Admit(list, value, out var subject)) { accepted.Add(value); settled ??= subject; }
            list.InsertRange(Math.Min(index, list.Count), accepted);
            settled?.SettleWounds?.Invoke();
        }
        public static void Replace(List<Hediff> list, int index, Hediff value)
        {
            if (!PinnedHediffs.AllowReplacement(list, index, value) || !Admit(list, value, out var subject)) return;
            list[index] = value;
            subject?.SettleWounds?.Invoke();
        }
        public static void CollectionAdd(ICollection<Hediff> values, Hediff value)
        { if (values is List<Hediff> list) Add(list, value); else values.Add(value); }
        public static void ListInsert(IList<Hediff> values, int index, Hediff value)
        { if (values is List<Hediff> list) Insert(list, index, value); else values.Insert(index, value); }
        public static void ListReplace(IList<Hediff> values, int index, Hediff value)
        { if (values is List<Hediff> list) Replace(list, index, value); else values[index] = value; }
    }
}
