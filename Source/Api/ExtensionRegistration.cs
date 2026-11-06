using System;

namespace LihuatLib.Api
{
    public sealed class ExtensionRegistration : IDisposable
    {
        private Action unregister;
        private Action<ExtensionRegistration> released;
        public string Id { get; }
        public bool IsDisposed => unregister == null;
        internal ExtensionRegistration(string id, Action unregister, Action<ExtensionRegistration> released)
        { Id = id; this.unregister = unregister; this.released = released; }
        public void Dispose()
        {
            if (IsDisposed) return;
            LibraryCapabilities.RequirePublicationThread();
            unregister();
            unregister = null;
            var callback = released;
            released = null;
            callback(this);
        }
    }
}
