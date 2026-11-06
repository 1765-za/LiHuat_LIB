using System;
using Verse;

namespace LihuatLib.Mechanisms
{
    internal static class DeferredCallbacks
    {
        private sealed class Callback(Action action)
        {
            internal void Invoke() => action();
        }
        // The engine's queue profiler requires an actual declaring type on the delegate method.
        internal static void Enqueue(Action action) => LongEventHandler.ExecuteWhenFinished(new Callback(action).Invoke);
    }
}
