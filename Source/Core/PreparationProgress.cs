using System;
using System.Diagnostics;

namespace LihuatLib.Core
{
    internal sealed class PreparationProgress
    {
        internal bool Active { get; private set; }
        internal bool Complete { get; private set; }
        internal int Stage { get; private set; }
        internal int Stages { get; private set; }
        internal string StageKey { get; private set; }
        internal int Completed { get; private set; }
        internal int Total { get; private set; }
        internal long Revision { get; private set; }
        internal long FinishedAt { get; private set; }
        internal int Percent => Complete ? 100 : Stages == 0 ? 0
            : Math.Min(99, (int)(100d * (Math.Max(0, Stage - 1) + (Total > 0 ? Completed / (double)Total : 0)) / Stages));

        internal void Begin(int stages)
        {
            Active = true; Complete = false; Stage = 0; Stages = stages;
            Completed = Total = 0; FinishedAt = 0; StageKey = null; Revision++;
        }
        internal void Next(string key)
        {
            Stage++; StageKey = key; Completed = Total = 0; Revision++;
        }
        internal void Report(int completed, int total)
        {
            completed = Math.Max(0, completed); total = Math.Max(completed, total);
            if (Completed == completed && Total == total) return;
            Completed = completed; Total = total; Revision++;
        }
        internal void Finish()
        {
            Complete = true; FinishedAt = Stopwatch.GetTimestamp(); Revision++;
        }
        internal void Close() { Active = false; Revision++; }
    }
}
