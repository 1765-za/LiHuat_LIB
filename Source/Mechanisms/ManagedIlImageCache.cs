using System;
using System.Collections.Generic;
using System.Reflection;

namespace LihuatLib.Mechanisms
{
    internal sealed class ManagedIlImageCache
    {
        private sealed class Entry
        {
            internal WeakReference<ManagedIlImage> Image;
        }

        private readonly Dictionary<Module, Entry> entries = new Dictionary<Module, Entry>();

        internal ManagedIlImage Get(Module module, string path)
        {
            Entry entry;
            lock (entries)
            {
                if (!entries.TryGetValue(module, out entry)) entries.Add(module, entry = new Entry());
            }
            lock (entry)
            {
                if (entry.Image != null && entry.Image.TryGetTarget(out ManagedIlImage existing)) return existing;
                var image = new ManagedIlImage(path);
                if (image.ModuleId == module.ModuleVersionId) entry.Image = new WeakReference<ManagedIlImage>(image);
                return image;
            }
        }
    }
}
