using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Verse;

namespace LihuatLib.Mechanisms
{
    public static class FieldWriteProtection
    {
        private static readonly HashSet<MethodInfo> registered = new HashSet<MethodInfo>();
        private static readonly List<Func<FieldInfo, object, object, bool>> handlers = new List<Func<FieldInfo, object, object, bool>>();

        internal static void Install()
        {
            if (!LihuatLib.Configuration.HighMechanismPolicy.NativeProtection) return;
            Type field = typeof(FieldInfo).Assembly.GetType("System.Reflection.RtFieldInfo", true);
            MethodInfo gate = typeof(FieldWriteProtection).GetMethod(nameof(Allows), BindingFlags.NonPublic | BindingFlags.Static);
            NativeIlGuards.Guard(field.GetMethod("SetValue", BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly), gate, 0, 1, 2);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void Register(MethodInfo handler)
        {
            if (!LihuatLib.Configuration.HighMechanismPolicy.NativeProtection) return;
            CodePublicationAuthority.RequireCaller();
            CodePublicationAuthority.RequireBlueprint(handler);
            handler = MethodIdentity.Declaration(handler);
            if (registered.Contains(handler)) return;
            var callback = (Func<FieldInfo, object, object, bool>)GeneratedIntegrity.Create(handler).CreateDelegate(typeof(Func<FieldInfo, object, object, bool>));
            handlers.Add(callback);
            registered.Add(handler);
        }

        internal static bool Allows(FieldInfo field, object target, object value)
        {
            if ((Scribe.mode == LoadSaveMode.Inactive || Scribe.mode == LoadSaveMode.Saving) && GeneratedIntegrity.Owns(field)) return false;
            for (int i = 0; i < handlers.Count; i++) if (!handlers[i](field, target, value)) return false;
            return true;
        }
    }
}
