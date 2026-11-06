using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;

namespace LihuatLib.Integrations
{
    internal static class MonoModAssemblyIntegration
    {
        private static readonly ConcurrentQueue<Assembly> pending = new();
        private static readonly ConditionalWeakTable<Assembly, object> processed = new();
        private static int publicationThread;
        private static bool installing;

        internal static void Install()
        {
            if (publicationThread != 0) return;
            publicationThread = Environment.CurrentManagedThreadId;
            AppDomain.CurrentDomain.AssemblyLoad += Loaded;
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies()) pending.Enqueue(assembly);
            Pump();
        }

        private static void Loaded(object sender, AssemblyLoadEventArgs arguments)
        {
            Assembly assembly = arguments.LoadedAssembly;
            if (assembly.IsDynamic || assembly == typeof(Harmony).Assembly) return;
            pending.Enqueue(assembly);
            if (Environment.CurrentManagedThreadId == publicationThread && !installing) Pump();
        }

        internal static void Pump()
        {
            if (pending.IsEmpty || installing) return;
            installing = true;
            try
            {
                while (pending.TryDequeue(out Assembly assembly))
                {
                    if (assembly.IsDynamic || assembly == typeof(Harmony).Assembly || processed.TryGetValue(assembly, out _)) continue;
                    if (assembly.GetType("MonoMod.Utils.DynamicMethodDefinition") != null) HarmonyLifecycleCopies.InstallFor(assembly);
                    if (assembly.GetType("MonoMod.Core.Platforms.PlatformTriple") != null)
                    {
                        HarmonyNativeAddressBoundary.InstallFor(assembly);
                        HarmonyNativeDetourBoundary.InstallFor(assembly);
                    }
                    processed.Add(assembly, new object());
                }
            }
            finally { installing = false; }
        }
    }
}
