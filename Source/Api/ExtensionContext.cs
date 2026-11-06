using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace LihuatLib.Api
{
    public sealed class ExtensionContext : IDisposable
    {
        private readonly ExtensionHost host;
        private readonly Dictionary<string, ExtensionRegistration> registrations = new(StringComparer.Ordinal);
        public string Id { get; }
        public LibraryCapability Capabilities { get; }
        public Game Game => host.Game;
        public bool IsCurrent => host.IsCurrent;
        public bool IsDisposed { get; private set; }

        internal ExtensionContext(ExtensionHost host, ExtensionDefinition definition)
        { this.host = host; Id = definition.Id; Capabilities = definition.Capabilities; }

        public string Qualify(string key) { LibraryApi.ValidateKey(key); return Id + "/" + key; }
        public Domains.DomainCoordinator Domains { get { Require(LibraryCapability.Domains); return host.Library.Domains; } }
        public AstralArts.AstralArtsService AstralArts { get { Require(LibraryCapability.AstralArts); return host.Library.AstralArts; } }
        public StarPaths.StarPathService StarPaths { get { Require(LibraryCapability.StarPaths); return host.Library.StarPaths; } }
        public WorldArts.WorldArtService WorldArts { get { Require(LibraryCapability.WorldArts); return host.Library.WorldArts; } }
        public Characters.CharacterManager Characters { get { Require(LibraryCapability.CharacterManagement); return host.Library.Characters; } }

        public ExtensionRegistration RegisterWorldArt(string key, WorldArts.WorldArtSubject subject)
        {
            RequireWrite(LibraryCapability.WorldArts);
            ArgumentNullException.ThrowIfNull(subject);
            if (subject.Id != Qualify(key)) throw new ArgumentException("World art subject must use the qualified registration key.", nameof(subject));
            return Publish("world-art/" + key, subject.Id, () => host.Library.WorldArts.Register(subject),
                () => host.Library.WorldArts.Unregister(subject));
        }

        public ExtensionRegistration RegisterLifecycle(string key, LifecycleHandler callbacks)
        {
            RequireWrite(LibraryCapability.Lifecycle);
            ArgumentNullException.ThrowIfNull(callbacks);
            LifecycleHandler handler = callbacks.Copy(Qualify(key));
            return Publish("lifecycle/" + key, handler.Id, () => host.Library.Lifecycle.Register(handler),
                () => host.Library.Lifecycle.Unregister(handler));
        }

        public ExtensionRegistration RegisterDamage(string key, int order, Func<Thing, DamageInfo, float?> amount)
        {
            RequireWrite(LibraryCapability.Combat);
            ArgumentNullException.ThrowIfNull(amount);
            var handler = new DamagePreparationHandler { Id = Qualify(key), Order = order, Amount = amount };
            return Publish("damage/" + key, handler.Id, () => host.Library.DamagePreparation.Register(handler),
                () => host.Library.DamagePreparation.Unregister(handler));
        }

        public ExtensionRegistration RegisterStatLimit(string key, int order, Func<StatDef, Pawn, float?> maximum)
        {
            RequireWrite(LibraryCapability.Combat);
            ArgumentNullException.ThrowIfNull(maximum);
            var handler = new StatLimitHandler { Id = Qualify(key), Order = order, Maximum = maximum };
            return Publish("stat/" + key, handler.Id, () => host.Library.StatLimits.Register(handler),
                () => host.Library.StatLimits.Unregister(handler));
        }

        public ExtensionRegistration RegisterDomainEvents(string key, DomainCallbacks callbacks)
        {
            RequireWrite(LibraryCapability.Domains);
            ArgumentNullException.ThrowIfNull(callbacks);
            var handler = callbacks.Copy(Qualify(key));
            return Publish("domain-events/" + key, handler.Id, () => host.DomainEvents.Register(handler),
                () => host.DomainEvents.Unregister(handler));
        }

        public ExtensionRegistration RegisterDomainSpawnPolicy(string key, Func<bool> allowsSpawn)
        {
            RequireWrite(LibraryCapability.Domains);
            ArgumentNullException.ThrowIfNull(allowsSpawn);
            string id = Qualify(key);
            Func<bool> callback = () => allowsSpawn();
            return Publish("domain-spawn/" + key, id, () => host.Library.Domains.RegisterSpawnPolicy(id, callback),
                () => host.Library.Domains.UnregisterSpawnPolicy(id, callback));
        }

        public ExtensionRegistration RegisterDomainClosing(string key, int order, Action<Domains.DomainArea> prepare)
        {
            RequireWrite(LibraryCapability.Domains);
            ArgumentNullException.ThrowIfNull(prepare);
            string id = Qualify(key);
            Action<Domains.DomainArea> callback = area => prepare(area);
            return Publish("domain-closing/" + key, id, () => host.Library.Domains.RegisterClosingHandler(id, order, callback),
                () => host.Library.Domains.UnregisterClosingHandler(id, callback));
        }

        private ExtensionRegistration Publish(string slot, string id, Action register, Action unregister)
        {
            register();
            if (registrations.TryGetValue(slot, out var previous)) previous.Dispose();
            var registration = new ExtensionRegistration(id, unregister, item =>
            {
                if (registrations.TryGetValue(slot, out var current) && ReferenceEquals(current, item)) registrations.Remove(slot);
            });
            registrations[slot] = registration;
            return registration;
        }

        private void Require(LibraryCapability capability)
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            if (!IsCurrent) throw new InvalidOperationException("The extension belongs to a different game.");
            if ((Capabilities & capability) != capability) throw new InvalidOperationException("Extension did not declare capability: " + capability);
        }
        private void RequireWrite(LibraryCapability capability)
        { Require(capability); LibraryCapabilities.RequirePublicationThread(); }

        public void Dispose()
        {
            if (IsDisposed) return;
            LibraryCapabilities.RequirePublicationThread();
            IsDisposed = true;
            var owned = new ExtensionRegistration[registrations.Count];
            registrations.Values.CopyTo(owned, 0);
            foreach (var registration in owned) registration.Dispose();
            registrations.Clear();
        }
    }
}
