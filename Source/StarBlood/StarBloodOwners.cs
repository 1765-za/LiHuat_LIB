using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace LihuatLib.StarBlood
{
    internal sealed class StarBloodOwners : SafeHandleZeroOrMinusOneIsInvalid
    {
        internal StarBloodOwners() : base(true)
        {
            SetHandle(GCHandle.ToIntPtr(GCHandle.Alloc(new ConditionalWeakTable<object, StarBloodRules>())));
        }

        // Keep the admission index out of the component graph without rooting its weak keys.
        private ConditionalWeakTable<object, StarBloodRules> Index =>
            (ConditionalWeakTable<object, StarBloodRules>)GCHandle.FromIntPtr(handle).Target;

        internal bool TryGetValue(object key, out StarBloodRules value) => Index.TryGetValue(key, out value);
        internal void Add(object key, StarBloodRules value) => Index.Add(key, value);
        internal void Remove(object key) => Index.Remove(key);

        protected override bool ReleaseHandle()
        {
            GCHandle.FromIntPtr(handle).Free();
            return true;
        }
    }
}
