using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace LihuatLib.Core
{
    public sealed class RetainedState<T> : SafeHandleZeroOrMinusOneIsInvalid where T : class
    {
        public RetainedState(T value) : base(true)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            // Conditional ownership keeps value-to-owner cycles collectible when the owning game is released.
            var states = new ConditionalWeakTable<RetainedState<T>, T>();
            states.Add(this, value);
            SetHandle(GCHandle.ToIntPtr(GCHandle.Alloc(states)));
        }

        public T Value
        {
            get
            {
                var states = (ConditionalWeakTable<RetainedState<T>, T>)GCHandle.FromIntPtr(handle).Target;
                return states.TryGetValue(this, out T value) ? value
                    : throw new InvalidOperationException("Retained state has no live owner.");
            }
        }

        protected override bool ReleaseHandle()
        {
            GCHandle.FromIntPtr(handle).Free();
            return true;
        }
    }
}
