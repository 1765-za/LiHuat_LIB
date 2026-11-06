using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Verse;

namespace LihuatLib.Mechanisms
{
    internal sealed class LifeCacheOwner
    {
        internal readonly string Id, Name, Revision, FileName;
        internal readonly Module[] Modules;

        internal LifeCacheOwner(string id, string name, IEnumerable<Module> modules)
        {
            Id = id; Name = name; Modules = modules.OrderBy(m => m.Name, StringComparer.Ordinal).ToArray();
            DateTime updated = DateTime.MinValue;
            var versions = new List<string>();
            foreach (Module module in Modules)
            {
                var file = new FileInfo(module.FullyQualifiedName);
                if (file.LastWriteTimeUtc > updated) updated = file.LastWriteTimeUtc;
                versions.Add(module.Name + ":" + module.ModuleVersionId.ToString("N") + ":" + file.Length + ":" + file.LastWriteTimeUtc.Ticks);
            }
            Revision = string.Join("\n", versions);
            string label = new string(name.Take(40).Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray());
            FileName = label + " [" + Uri.EscapeDataString(id) + "]_" + updated.ToString("yyyyMMdd-HHmmssfffffff", CultureInfo.InvariantCulture) + ".sbac";
        }

        internal static IEnumerable<LifeCacheOwner> FromMods(IEnumerable<ModContentPack> mods)
        {
            foreach (ModContentPack mod in mods)
            {
                Module[] modules = mod.assemblies.loadedAssemblies.Where(a => !a.IsDynamic).SelectMany(a => a.GetModules()).ToArray();
                if (modules.Length > 0) yield return new LifeCacheOwner(mod.PackageId, mod.Name, modules);
            }
        }

        internal static LifeCacheOwner FromAssembly(Assembly assembly) =>
            new LifeCacheOwner("assembly." + assembly.GetName().Name, assembly.GetName().Name, assembly.GetModules());
    }
}
