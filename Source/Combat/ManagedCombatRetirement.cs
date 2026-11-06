using System;
using System.Linq;
using LihuatLib.Core;
using LihuatLib.Mechanisms;
using RimWorld;
using Verse;

namespace LihuatLib.Combat
{
    internal static class ManagedCombatRetirement
    {
        internal static bool Retire(ManagedCombatShape shape, object state, Pawn attacker)
        {
            if (state == null || LihuatGame.Current == null || !LibraryCapabilities.Has(LibraryCapability.Erasure)) return false;
            Map map = shape.Map(state);
            if (map == null || string.IsNullOrEmpty(shape.Key(state))) return false;
            object manager = shape.Manager(map);
            RetirementSubjects.Register(shape.StateType);
            SubjectCallbacks.InstallEffects(shape.StateType);
            ManagedRegistrationBoundary.Install(shape);
            if (!shape.Health.IsDead(state))
                shape.Health.Hit(state, shape.Health.Core, new DamageInfo(DamageDefOf.Cut,
                    Math.Max(shape.Health.Current(state), shape.Health.Capacity(state)), instigator: attacker), true);
            if (!shape.Health.IsDead(state)) return false;
            var states = shape.States(map);
            IntVec3 previous = shape.Position(state).ToIntVec3();
            try
            {
                shape.Detach.Invoke(state, null);
                ErasureCollectionEntries.Remove(states, state);
                if (shape.Map(state) != null || states.Cast<object>().Any(item => ReferenceEquals(item, state)))
                    throw new InvalidOperationException("Combat state did not leave its owner.");
            }
            catch
            {
                if (!states.Cast<object>().Any(item => ReferenceEquals(item, state))) states.Add(state);
                if (shape.Map(state) != map) shape.Arrive.Invoke(state, new object[] { map, previous });
                throw;
            }
            LihuatGame.Current.RetiredSubjects.Retire(state);
            map.GetComponent<CombatRetirementMap>().Record(shape.Id, shape.Key(state));
            var cleanup = new SubjectCleanup(state);
            cleanup.Run(manager, state);
            if (cleanup.Limited || cleanup.Failures > 0)
                Log.Warning("[LihuatLib.Combat] Retirement observer cleanup incomplete: source=" + shape.Id
                    + " limited=" + cleanup.Limited + " failures=" + cleanup.Failures);
            return true;
        }
    }
}
