using System;
using System.Collections.Generic;
using Verse;

namespace LihuatLib.Api
{
    internal sealed class ExtensionHost(Game game, LihuatGame library)
    {
        private readonly List<(ILibraryExtension Extension, ExtensionContext Context)> extensions = new();
        internal bool Ready { get; private set; }
        private bool initializing;
        internal bool IsCurrent => ReferenceEquals(Current.Game, game) && ReferenceEquals(LihuatGame.Current, library);
        internal Game Game => game;
        internal LihuatGame Library => library;
        internal DomainEventChannel DomainEvents { get; } = new(library.Domains);

        internal void Schedule() => LongEventHandler.ExecuteWhenFinished(Initialize);
        private void Initialize()
        {
            if (Ready || initializing || !IsCurrent) return;
            LibraryCapabilities.RequirePublicationThread();
            initializing = true;
            foreach (var definition in LibraryApi.Definitions)
            {
                var context = new ExtensionContext(this, definition);
                try
                {
                    var extension = definition.Create();
                    extension.Register(context);
                    extensions.Add((extension, context));
                }
                catch (Exception error)
                {
                    context.Dispose();
                    Log.Error("[LihuatLib.Api] Extension registration failed: " + definition.Id + "\n" + error);
                }
            }
            Ready = true;
            initializing = false;
        }
    }
}
