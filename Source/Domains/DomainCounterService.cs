using System.Collections.Generic;
using System.Linq;
using Verse;

namespace LihuatLib.Domains
{
    public sealed class DomainCounterService : IExposable
    {
        private List<CounterRecord> records = new List<CounterRecord>();
        private readonly Dictionary<Pawn, CounterRecord> byPawn = new Dictionary<Pawn, CounterRecord>(Core.ReferenceIdentity<Pawn>.Instance);
        private readonly Dictionary<Pawn, CounterSubject> subjects = new Dictionary<Pawn, CounterSubject>(Core.ReferenceIdentity<Pawn>.Instance);
        private readonly Dictionary<Pawn, CounterSubject> active = new Dictionary<Pawn, CounterSubject>(Core.ReferenceIdentity<Pawn>.Instance);
        private CounterSubject[] tickSubjects = System.Array.Empty<CounterSubject>();
        private bool subjectsChanged;
        public int ActiveCount => active.Count;
        public long TickVisits { get; private set; }
        public void Register(CounterSubject subject)
        {
            LibraryCapabilities.Require(LibraryCapability.Domains);
            subjects[subject.Pawn] = subject;
            if (!byPawn.ContainsKey(subject.Pawn))
            {
                var record = new CounterRecord { Pawn = subject.Pawn, LastPaidAt = subject.Clock() };
                records.Add(record); byPawn.Add(subject.Pawn, record);
            }
            RefreshActive(byPawn[subject.Pawn]);
        }
        public void Unregister(Pawn pawn)
        {
            if (pawn == null) return;
            subjects.Remove(pawn);
            if (active.Remove(pawn)) subjectsChanged = true;
            if (byPawn.TryGetValue(pawn, out CounterRecord record)) { byPawn.Remove(pawn); records.Remove(record); }
        }
        public bool Amplifying(Pawn pawn) => pawn != null && byPawn.TryGetValue(pawn, out CounterRecord record) && record.Amplification;
        public bool Signing(Pawn pawn) => pawn != null && byPawn.TryGetValue(pawn, out CounterRecord record) && record.HandSigns;
        public bool BlocksAbilities(Pawn pawn) => Amplifying(pawn) || Signing(pawn);
        public bool SetAmplification(Pawn pawn, bool active)
        {
            if (!subjects.TryGetValue(pawn, out CounterSubject subject) || active && (!subject.Available() || !subject.FormalMastery())) return false;
            CounterRecord record = byPawn[pawn];
            if (record.Amplification == active) return false;
            record.Amplification = active; RefreshActive(record); Changed(subject); return true;
        }
        public bool SetHandSigns(Pawn pawn, bool active)
        {
            if (!subjects.TryGetValue(pawn, out CounterSubject subject) || active && !subject.Available()) return false;
            CounterRecord record = byPawn[pawn];
            if (record.HandSigns == active) return false;
            record.HandSigns = active;
            RefreshActive(record);
            if (active)
            {
                record.LastPaidAt = subject.Clock();
                pawn.stances?.CancelBusyStanceHard();
                pawn.jobs?.EndCurrentJob(Verse.AI.JobCondition.InterruptForced);
            }
            Changed(subject); return true;
        }
        public void Tick()
        {
            if (subjectsChanged)
            {
                tickSubjects = active.Values.ToArray();
                subjectsChanged = false;
            }
            foreach (CounterSubject subject in tickSubjects)
            {
                if (!active.TryGetValue(subject.Pawn, out CounterSubject current) || current != subject) continue;
                if (!byPawn.TryGetValue(subject.Pawn, out CounterRecord record)) continue;
                TickVisits++;
                if (!record.Amplification && !record.HandSigns) { RefreshActive(record); continue; }
                if (!subject.Available())
                {
                    record.Amplification = false; record.HandSigns = false; RefreshActive(record); Changed(subject); continue;
                }
                if (!record.HandSigns) continue;
                int seconds = (subject.Clock() - record.LastPaidAt) / 60;
                if (seconds <= 0) continue;
                record.LastPaidAt += seconds * 60;
                if (!subject.Spend(seconds * subject.CostPerSecond)) { record.HandSigns = false; RefreshActive(record); Changed(subject); }
            }
        }
        private void RefreshActive(CounterRecord record)
        {
            Pawn pawn = record.Pawn;
            if (!byPawn.TryGetValue(pawn, out CounterRecord current) || current != record) return;
            if ((record.Amplification || record.HandSigns) && subjects.TryGetValue(pawn, out CounterSubject subject))
            {
                if (active.TryGetValue(pawn, out CounterSubject previous) && previous == subject) return;
                active[pawn] = subject;
                subjectsChanged = true;
            }
            else if (active.Remove(pawn)) subjectsChanged = true;
        }
        private static void Changed(CounterSubject subject) { subject.Changed(); LihuatGame.Current.Domains.RefreshPressure(subject.Pawn); }
        public void ExposeData()
        {
            Scribe_Collections.Look(ref records, "domainCounters", LookMode.Deep);
            if (records == null) records = new List<CounterRecord>();
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                byPawn.Clear();
                foreach (CounterRecord record in records) if (record.Pawn != null) byPawn[record.Pawn] = record;
                active.Clear(); tickSubjects = System.Array.Empty<CounterSubject>(); subjectsChanged = false;
                foreach (CounterRecord record in byPawn.Values) RefreshActive(record);
            }
        }
    }
}
