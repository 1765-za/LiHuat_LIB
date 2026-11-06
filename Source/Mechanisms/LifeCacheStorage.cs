using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;

namespace LihuatLib.Mechanisms
{
    internal sealed class LifeCacheStorage
    {
        private readonly string directory;
        private readonly string context;
        private readonly ConcurrentDictionary<Module, LifeCacheOwner> owners = new ConcurrentDictionary<Module, LifeCacheOwner>();
        private readonly ConcurrentDictionary<string, LifeCachePackage> packages = new ConcurrentDictionary<string, LifeCachePackage>(StringComparer.OrdinalIgnoreCase);
        internal int Removed { get; private set; }
        internal long RemovedBytes { get; private set; }
        internal int CleanupFailures { get; private set; }

        internal LifeCacheStorage(string directory, IEnumerable<LifeCacheOwner> groups, string context)
        {
            this.context = context;
            this.directory = Path.GetFullPath(directory);
            if (Directory.Exists(this.directory) && (File.GetAttributes(this.directory) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Cache directory cannot be a reparse point.");
            if (groups != null)
                foreach (LifeCacheOwner owner in groups)
                    foreach (Module module in owner.Modules) owners.TryAdd(module, owner);
        }

        internal LifeCachePackage Package(LifeAnalysisKey key)
        {
            LifeCacheOwner owner = owners.GetOrAdd(key.Root.Module, module => LifeCacheOwner.FromAssembly(module.Assembly));
            return packages.GetOrAdd(owner.FileName, _ => new LifeCachePackage(directory, owner, context));
        }

        internal Task MaintainAsync() => Task.Run(Maintain);

        private void Maintain()
        {
            var replacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (LifeCachePackage package in packages.Values)
            {
                try { package.Maintain(); }
                catch (IOException) { CleanupFailures++; }
                catch (UnauthorizedAccessException) { CleanupFailures++; }
                active.Add(package.Path);
                if (package.Used) replacements[package.OwnerId] = package.Path;
            }
            if (!Directory.Exists(directory)) return;
            DateTime expired = DateTime.UtcNow.AddDays(-30), abandoned = DateTime.UtcNow.AddDays(-1);
            try
            {
                foreach (string path in Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        var file = new FileInfo(path);
                        if ((file.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                        bool remove = Legacy(file.Name);
                        if (!remove && file.Name.Length == 105 && Legacy(file.Name.Substring(0, 68))
                            && file.Name[68] == '.' && Guid.TryParseExact(file.Name.Substring(69, 32), "N", out _)
                            && file.Extension == ".tmp") remove = file.LastWriteTimeUtc < abandoned;
                        if (!remove && file.Extension == ".sbac" && !active.Contains(path))
                        {
                            string owner = LifeCachePackage.ReadOwner(path);
                            remove = owner != null && (replacements.ContainsKey(owner) || file.LastWriteTimeUtc < expired);
                        }
                        if (!remove && file.Name.Contains(".sbac.", StringComparison.Ordinal) && file.Extension == ".tmp")
                            remove = file.LastWriteTimeUtc < abandoned;
                        if (!remove) continue;
                        long length = file.Length;
                        file.Delete(); Removed++; RemovedBytes += length;
                    }
                    catch (IOException) { CleanupFailures++; }
                    catch (UnauthorizedAccessException) { CleanupFailures++; }
                }
            }
            catch (IOException) { CleanupFailures++; }
            catch (UnauthorizedAccessException) { CleanupFailures++; }
        }

        private static bool Legacy(string name) => name.Length == 68 && name.EndsWith(".bin", StringComparison.OrdinalIgnoreCase)
            && name.AsSpan(0, 64).IndexOfAnyExcept("0123456789abcdefABCDEF") < 0;
    }
}
