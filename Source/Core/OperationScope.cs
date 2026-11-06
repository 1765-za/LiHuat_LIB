using System;

namespace LihuatLib.Core
{
    using Verse;
    using System.Reflection;
    using System.Runtime.CompilerServices;
    public enum OperationKind { Transfer, Rebirth, TerminalDeath, Medical, ControlledDamage }

    public sealed class OperationScope : IDisposable
    {
        [ThreadStatic] private static int transfer;
        [ThreadStatic] private static int rebirth;
        [ThreadStatic] private static int terminal;
        [ThreadStatic] private static OperationScope current;
        private readonly OperationKind kind;
        private readonly Thing subject;
        private readonly OperationScope previous;
        private readonly bool entered;
        private bool disposed;

        public static bool Transferring => transfer > 0 || DeveloperExecution.Active;
        public static bool Rebirthing => rebirth > 0;
        public static bool Terminating => terminal > 0 || DeveloperExecution.Active;
        public static bool MedicalFor(Thing thing) => DeveloperExecution.Allows(thing) || Allows(OperationKind.Medical, thing);
        public static bool ControlsDamage(Thing thing) => Allows(OperationKind.ControlledDamage, thing);

        public static bool Terminates(Thing thing)
        {
            return DeveloperExecution.Allows(thing) || Allows(OperationKind.TerminalDeath, thing)
                || thing is Corpse corpse && Allows(OperationKind.TerminalDeath, corpse.InnerPawn);
        }

        public static bool HasTerminalIntent(Thing thing) => Allows(OperationKind.TerminalDeath, thing)
            || thing is Corpse corpse && Allows(OperationKind.TerminalDeath, corpse.InnerPawn);

        private static bool Allows(OperationKind kind, Thing thing)
        {
            if (thing == null) return false;
            for (OperationScope scope = current; scope != null; scope = scope.previous)
                if (scope.kind == kind && ReferenceEquals(scope.subject, thing)) return true;
            return false;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public OperationScope(OperationKind kind) : this(kind, null, Assembly.GetCallingAssembly()) { }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public OperationScope(OperationKind kind, Thing subject) : this(kind, subject, Assembly.GetCallingAssembly()) { }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private OperationScope(OperationKind kind, Thing subject, Assembly caller)
        {
            if (kind < OperationKind.Transfer || kind > OperationKind.ControlledDamage)
                throw new ArgumentOutOfRangeException(nameof(kind));
            if (subject == null && kind != OperationKind.Transfer && kind != OperationKind.Rebirth)
                throw new ArgumentNullException(nameof(subject));
            this.kind = kind;
            this.subject = subject;
            if (!DeveloperExecution.Active && (!InfrastructureAssemblies.Contains(caller) && !Mechanisms.OperationCaller.IsInfrastructureReplacement()
                || Assembly.GetCallingAssembly() != typeof(OperationScope).Assembly)) return;
            entered = true;
            previous = current;
            current = this;
            Adjust(1);
        }

        private void Adjust(int change)
        {
            switch (kind)
            {
                case OperationKind.Transfer: transfer += change; break;
                case OperationKind.Rebirth: rebirth += change; break;
                case OperationKind.TerminalDeath: terminal += change; break;
            }
        }

        public void Dispose()
        {
            if (disposed || !entered) return;
            if (!ReferenceEquals(current, this)) throw new InvalidOperationException("Operation scopes must exit in reverse entry order.");
            try
            {
                if (kind == OperationKind.Transfer && transfer == 1) StarBlood.PawnContinuity.AcceptTransfers();
            }
            finally
            {
                current = previous;
                Adjust(-1);
                disposed = true;
            }
        }
    }
}
