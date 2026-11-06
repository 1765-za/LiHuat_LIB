using System.Runtime.CompilerServices;
using Verse;

namespace LihuatLib.Domains
{
    public sealed partial class DomainCoordinator
    {
        private struct PressureQuery
        {
            internal readonly Pawn Pawn;
            private DomainArea coveredOwner;
            private bool ownerRead, amplificationRead, amplifying;

            internal PressureQuery(Pawn pawn)
            {
                Pawn = pawn;
                coveredOwner = null;
                ownerRead = amplificationRead = amplifying = false;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal DomainArea GetCoveredOwner(DomainCoordinator coordinator)
            {
                if (!ownerRead)
                {
                    DomainArea owner = coordinator.OwnedBy(Pawn);
                    if (owner != null && coordinator.Covers(owner, Pawn)) coveredOwner = owner;
                    ownerRead = true;
                }
                return coveredOwner;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal bool IsAmplifying()
            {
                if (!amplificationRead)
                {
                    amplifying = LihuatGame.Current.Counters.Amplifying(Pawn);
                    amplificationRead = true;
                }
                return amplifying;
            }
        }

        private struct PressureState
        {
            internal DomainPressure Mask;
            internal float Strength;
        }
    }
}
