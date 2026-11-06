using System;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace LihuatLib.Mechanisms
{
    internal static class ObjectAddressBoundary
    {
        internal static void Install()
        {
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
            NativeIlGuards.RetainEntries(new[] { typeof(PointerAccessProtection).GetMethod(nameof(PointerAccessProtection.RequireAccess), flags) });
            foreach (string name in new[] { nameof(Cast), nameof(Reinterpret), nameof(Address), nameof(Unbox) })
                GenericBodyPublication.Protect(typeof(ObjectAddressBoundary).GetMethod(name, flags));
        }

        internal static T Cast<T>(object value) where T : class
        {
            if (value != null && value is not T) PointerAccessProtection.RequireAccess(value);
            return Unsafe.As<T>(value);
        }

        internal static ref TTo Reinterpret<TFrom, TTo>(ref TFrom value)
            where TFrom : allows ref struct where TTo : allows ref struct
        {
            if (!typeof(TFrom).IsValueType)
            {
                object target = Unsafe.As<TFrom, object>(ref value);
                if (target != null && !typeof(TTo).IsInstanceOfType(target))
                    PointerAccessProtection.RequireAccess(target);
            }
            return ref Unsafe.As<TFrom, TTo>(ref value);
        }

        internal static unsafe void* Address<T>(ref T value) where T : allows ref struct
        {
            if (!typeof(T).IsValueType) PointerAccessProtection.RequireAccess(Unsafe.As<T, object>(ref value));
            return Unsafe.AsPointer(ref value);
        }

        internal static ref T Unbox<T>(object value) where T : struct
        {
            if (value != null && value.GetType() != typeof(T)) PointerAccessProtection.RequireAccess(value);
            return ref Unsafe.Unbox<T>(value);
        }
    }
}
