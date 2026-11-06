using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using Verse;

namespace LihuatLib.Api
{
    public interface ILibraryExtension
    {
        void Register(ExtensionContext context);
    }

    public static class LibraryApi
    {
        public const int Version = 1;
        private static readonly OrderedHandlers<ExtensionDefinition> definitions = new(d => d.Id, d => d.Order);
        internal static ExtensionDefinition[] Definitions => definitions.Items;
        public static bool IsReady => LihuatGame.Current?.Extensions.Ready == true;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void Register<T>(ModContentPack mod, string key, LibraryCapability capabilities, int order = 100)
            where T : ILibraryExtension, new()
        {
            if (LibraryCapabilities.Frozen) throw new InvalidOperationException("Register extensions in the Mod constructor, before library startup.");
            ArgumentNullException.ThrowIfNull(mod);
            ValidateKey(key);
            Assembly caller = Assembly.GetCallingAssembly();
            if (!mod.assemblies.loadedAssemblies.Contains(caller) || !mod.assemblies.loadedAssemblies.Contains(typeof(T).Assembly))
                throw new ArgumentException("The caller and extension must belong to the supplied mod.", nameof(mod));
            string id = mod.PackageId.ToLowerInvariant() + "/" + key;
            foreach (var registered in definitions.Items)
                if (registered.Id == id) throw new InvalidOperationException("Extension already registered: " + id);
            capabilities = LibraryCapabilities.Expand(capabilities);
            LibraryCapabilities.Enable(capabilities);
            InfrastructureAssemblies.Register(typeof(T).Assembly);
            definitions.Register(new ExtensionDefinition(id, order, capabilities, () => new T()));
        }

        internal static void ValidateKey(string key)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("A nonempty registration key is required.", nameof(key));
            foreach (char c in key)
                if (!(c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '.' or '_' or '-'))
                    throw new ArgumentException("Registration keys may contain letters, digits, dots, underscores and hyphens.", nameof(key));
        }
    }

    internal sealed record ExtensionDefinition(string Id, int Order, LibraryCapability Capabilities, Func<ILibraryExtension> Create);
}
