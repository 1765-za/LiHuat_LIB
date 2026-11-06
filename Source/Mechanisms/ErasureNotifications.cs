using System;
using LihuatLib.Core;
using Verse;

namespace LihuatLib.Mechanisms
{
    public static class ErasureNotifications
    {
        public static long Failures { get; private set; }
        public static string LastFailure { get; private set; }
        public static long BlockedGenerations { get; private set; }

        internal static void Invoke(Action<Thing> callbacks, Thing target)
        {
            if (callbacks == null) return;
            Pawn source = target as Pawn ?? (target as Corpse)?.InnerPawn;
            using var scope = LihuatGame.Current?.Regeneration.ObserveRemoval(source) ?? default;
            foreach (Action<Thing> callback in callbacks.GetInvocationList())
                try { callback(target); }
                catch (RegenerationDeniedException) { BlockedGenerations++; }
                catch (Exception error)
                {
                    Failures++;
                    LastFailure = callback.Method.DeclaringType?.FullName + "." + callback.Method.Name + ": " + error.GetType().Name + ": " + error.Message;
                }
        }
    }
}
