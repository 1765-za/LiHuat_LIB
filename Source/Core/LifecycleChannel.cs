using System;
using System.Collections.Generic;
using System.Reflection;
using LihuatLib.StarBlood;
using Verse;
using RimWorld;
using Verse.AI.Group;

namespace LihuatLib.Core
{
    public enum DestructiveEntry { Kill, SetDead, ShouldBeDead, Destroy, Discard, DeSpawn, WorldGC }
    public sealed class LifecycleHandler
    {
        public string Id;
        public int Order;
        public Func<Pawn, DestructiveEntry, bool> Block;
        public Func<Thing, DestructiveEntry, bool> BlockThing;
        public Func<Thing, FieldInfo[], bool> BlocksGeneratedMutation;
        public Func<Pawn, bool> BlocksComponentRemoval;
        public Action<Pawn, DestructiveEntry> Preparing;
        public Action<Pawn, DestructiveEntry> Completed;
        public Func<Pawn, bool> AllowsRestore;
        public Func<Pawn, bool> AllowsRegistration;
        public Func<bool> AllowsGeneration;
        public Action<Pawn> Restored;
        public Func<RegenerationOrigin, bool> AllowsRegeneration;
        public Func<Pawn, bool> AllowsErasureObservers;
        public Action<Pawn> Generated;
        public Action<Pawn> Registered;
        public Action<Pawn, Faction, Map> Kidnapped;
        public Func<Pawn, Lord, bool> AllowsLord;
        public Func<Pawn, bool> BeforeWorldDeparture;
        public Func<Map, bool> BlocksMapRemoval;
        public Action<Map> MapRemoving;
        public Action<Map, bool> MapRemovalFinished;
        public Action DeveloperStarted;
        public Action<Pawn, DestructiveEntry> DeveloperPreparing;
        public Action DeveloperFinished;
        internal LifecycleHandler Copy(string id)
        {
            var copy = (LifecycleHandler)MemberwiseClone();
            copy.Id = id;
            return copy;
        }
    }
    public sealed class LifecycleChannel
    {
        private readonly GenerationAdmission admission;
        internal LifecycleChannel(GenerationAdmission admission) { this.admission = admission; }
        private readonly OrderedHandlers<LifecycleHandler> handlers = new(h => h.Id, h => h.Order);
        public void Register(LifecycleHandler handler)
        {
            LibraryCapabilities.Require(LibraryCapability.Lifecycle);
            if (handler == null || string.IsNullOrWhiteSpace(handler.Id)) throw new ArgumentException("Lifecycle registration requires an identity.");
            handlers.Register(handler);
        }
        internal void Unregister(LifecycleHandler handler) => handlers.Remove(handler);
        public bool Block(Pawn pawn, DestructiveEntry entry)
        {
            if (DeveloperExecution.Allows(pawn)) return false;
            if (pawn == null || OperationScope.Terminates(pawn)) return false;
            if (entry != DestructiveEntry.DeSpawn && entry != DestructiveEntry.WorldGC && StarBloodRegistry.For(pawn)?.BeforeDeath(pawn) == true) return true;
            foreach (LifecycleHandler handler in handlers.Items) if (handler.Block?.Invoke(pawn, entry) == true) return true;
            return false;
        }
        public bool Before(Pawn pawn, DestructiveEntry entry)
        {
            if (DeveloperExecution.Active)
            {
                foreach (LifecycleHandler handler in handlers.Items) handler.DeveloperPreparing?.Invoke(pawn, entry);
                return true;
            }
            if (Block(pawn, entry)) return false;
            foreach (LifecycleHandler handler in handlers.Items) handler.Preparing?.Invoke(pawn, entry);
            return true;
        }
        internal bool BeforeThing(Thing thing, DestructiveEntry entry)
        {
            if (thing == null || thing is Pawn || DeveloperExecution.Allows(thing) || OperationScope.Terminates(thing)) return true;
            foreach (LifecycleHandler handler in handlers.Items) if (handler.BlockThing?.Invoke(thing, entry) == true) return false;
            return true;
        }
        internal bool AllowsGeneratedMutation(Thing thing, FieldInfo[] fields)
        {
            if (thing == null || thing is Pawn || DeveloperExecution.Allows(thing) || OperationScope.Terminates(thing)) return true;
            foreach (LifecycleHandler handler in handlers.Items) if (handler.BlocksGeneratedMutation?.Invoke(thing, fields) == true) return false;
            return true;
        }
        public void After(Pawn pawn, DestructiveEntry entry) { if (!DeveloperExecution.Active) foreach (LifecycleHandler handler in handlers.Items) handler.Completed?.Invoke(pawn, entry); }
        internal void DeveloperStarted() { foreach (LifecycleHandler handler in handlers.Items) handler.DeveloperStarted?.Invoke(); }
        internal void DeveloperFinished() { foreach (LifecycleHandler handler in handlers.Items) handler.DeveloperFinished?.Invoke(); }
        public bool AllowsComponentRemoval(Pawn pawn)
        {
            if (DeveloperExecution.Active) return true;
            foreach (LifecycleHandler handler in handlers.Items) if (handler.BlocksComponentRemoval?.Invoke(pawn) == true) return false;
            return true;
        }
        public bool AllowsRestore(Pawn pawn)
        {
            if (DeveloperExecution.Active) return true;
            if (!admission.AllowsRestore(pawn)) return false;
            foreach (LifecycleHandler handler in handlers.Items) if (handler.AllowsRestore?.Invoke(pawn) == false) return false;
            return true;
        }
        public bool AllowsRegistration(Pawn pawn)
        {
            if (DeveloperExecution.Active) return true;
            if (!admission.AllowsArrival(pawn)) return false;
            foreach (LifecycleHandler handler in handlers.Items) if (handler.AllowsRegistration?.Invoke(pawn) == false) return false;
            return true;
        }
        public bool AllowsGeneration()
        {
            if (DeveloperExecution.Active) return true;
            if (!admission.AllowsGeneration) return false;
            foreach (LifecycleHandler handler in handlers.Items) if (handler.AllowsGeneration?.Invoke() == false) return false;
            return true;
        }
        public bool AllowsRegeneration(RegenerationOrigin origin)
        {
            if (DeveloperExecution.Active) return true;
            foreach (LifecycleHandler handler in handlers.Items) if (handler.AllowsRegeneration?.Invoke(origin) == false) return false;
            return true;
        }
        public bool AllowsErasureObservers(Pawn pawn)
        {
            foreach (LifecycleHandler handler in handlers.Items) if (handler.AllowsErasureObservers?.Invoke(pawn) == false) return false;
            return true;
        }
        public void Generated(Pawn pawn) { if (!DeveloperExecution.Active) foreach (LifecycleHandler handler in handlers.Items) handler.Generated?.Invoke(pawn); }
        public void Registered(Pawn pawn)
        {
            admission.Registered(pawn);
            if (!DeveloperExecution.Active) foreach (LifecycleHandler handler in handlers.Items) handler.Registered?.Invoke(pawn);
        }
        internal void Kidnapped(Pawn pawn, Faction captor, Map source)
        {
            foreach (LifecycleHandler handler in handlers.Items) handler.Kidnapped?.Invoke(pawn, captor, source);
        }
        public bool AllowsLord(Pawn pawn, Lord lord)
        {
            foreach (LifecycleHandler handler in handlers.Items) if (handler.AllowsLord?.Invoke(pawn, lord) == false) return false;
            return true;
        }
        internal bool BeforeWorldDeparture(Pawn pawn)
        {
            foreach (LifecycleHandler handler in handlers.Items) if (handler.BeforeWorldDeparture?.Invoke(pawn) == false) return false;
            return true;
        }
        internal bool BeforeMapRemoval(Map map)
        {
            foreach (LifecycleHandler handler in handlers.Items) if (handler.BlocksMapRemoval?.Invoke(map) == true) return false;
            foreach (LifecycleHandler handler in handlers.Items) handler.MapRemoving?.Invoke(map);
            return true;
        }
        internal void AfterMapRemoval(Map map, bool removed)
        {
            foreach (LifecycleHandler handler in handlers.Items) handler.MapRemovalFinished?.Invoke(map, removed);
        }
        public void Restored(Pawn pawn)
        {
            if (!PawnLife.Dead(pawn)) StarBloodRegistry.For(pawn)?.AcceptRestoration();
            foreach (LifecycleHandler handler in handlers.Items) handler.Restored?.Invoke(pawn);
        }
    }
}
