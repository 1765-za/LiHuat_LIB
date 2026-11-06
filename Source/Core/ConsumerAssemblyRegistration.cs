using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Verse;

namespace LihuatLib.Core
{
    internal sealed class ConsumerAssemblyRegistration
    {
        private readonly string[] roots;
        private static bool initialized;

        private ConsumerAssemblyRegistration(IEnumerable<ModContentPack> mods)
        {
            var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ModContentPack mod in mods)
            {
                if (!ConsumerModDeclaration.UsesLibrary(mod)) continue;
                folders.Add(Path.GetFullPath(mod.RootDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    + Path.DirectorySeparatorChar);
                foreach (Assembly assembly in mod.assemblies.loadedAssemblies) InfrastructureAssemblies.Register(assembly);
            }
            roots = folders.ToArray();
        }

        internal static void Initialize(IEnumerable<ModContentPack> mods)
        {
            if (initialized) return;
            var registration = new ConsumerAssemblyRegistration(mods);
            initialized = true;
            if (registration.roots.Length == 0) return;
            AppDomain.CurrentDomain.AssemblyLoad += registration.OnAssemblyLoaded;
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies()) registration.Register(assembly);
        }

        private void OnAssemblyLoaded(object sender, AssemblyLoadEventArgs args) => Register(args.LoadedAssembly);

        private void Register(Assembly assembly)
        {
            if (assembly.IsDynamic || InfrastructureAssemblies.Contains(assembly) || string.IsNullOrEmpty(assembly.Location)) return;
            string location = Path.GetFullPath(assembly.Location);
            foreach (string root in roots)
                if (location.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                {
                    InfrastructureAssemblies.Register(assembly);
                    return;
                }
        }
    }
}
