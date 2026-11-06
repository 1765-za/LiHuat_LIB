using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Verse;

namespace LihuatLib.Mechanisms
{
    internal static class LifePlatformAssemblies
    {
        private static readonly HashSet<string> paths = CreatePaths();
        private static readonly ConcurrentDictionary<Assembly, bool> known = new ConcurrentDictionary<Assembly, bool>();
        internal static readonly string PolicyKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            string.Join("\n", paths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase)))));

        // Platform membership bounds analysis; it does not authorize writes or lifecycle operations.
        internal static bool Contains(Assembly assembly) => known.GetOrAdd(assembly,
            value => !value.IsDynamic && paths.Contains(value.Location));

        private static HashSet<string> CreatePaths()
        {
            var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                Path.GetDirectoryName(typeof(object).Assembly.Location),
                Path.GetDirectoryName(typeof(Pawn).Assembly.Location)
            };
            string platform = (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty;
            return new HashSet<string>(platform.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                .Where(path => directories.Contains(Path.GetDirectoryName(path))), StringComparer.OrdinalIgnoreCase);
        }
    }
}
