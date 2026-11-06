using System;
using System.Reflection;
using System.Reflection.Emit;

namespace LihuatLib.Mechanisms
{
    internal static class RuntimeMethodBodies
    {
        [ThreadStatic] private static int writing;
        internal static bool Writing => writing != 0;
        private static readonly Type updater = typeof(object).Assembly.GetType("WaylithClr.MethodBodyUpdater", true);
        private static readonly Action<MethodBase, byte[]> replace = CreateReplace();
        private static readonly Func<MethodBase, byte[]> read = CreateRead();
        private static readonly Action<MethodBase> revert = updater.GetMethod("Revert", new[] { typeof(MethodBase) }).CreateDelegate<Action<MethodBase>>();
        private static readonly Func<Module, MethodBase, int> methodToken = updater.GetMethod("GetTokenFor", new[] { typeof(Module), typeof(MethodBase) }).CreateDelegate<Func<Module, MethodBase, int>>();
        private static readonly Func<Module, FieldInfo, int> fieldToken = updater.GetMethod("GetTokenFor", new[] { typeof(Module), typeof(FieldInfo) }).CreateDelegate<Func<Module, FieldInfo, int>>();
        private static readonly Func<Module, Type, int> typeToken = updater.GetMethod("GetTokenFor", new[] { typeof(Module), typeof(Type) }).CreateDelegate<Func<Module, Type, int>>();
        internal static bool Supported => (bool)updater.GetProperty("IsSupported").GetValue(null);
        internal static byte[] Read(MethodBase method) => read(method);
        internal static void Replace(MethodBase method, byte[] body)
        {
            writing++;
            try { replace(method, body); }
            finally { writing--; }
        }
        internal static void Revert(MethodBase method)
        {
            writing++;
            try { revert(method); }
            finally { writing--; }
        }
        internal static int Token(MethodBase method, MethodBase member) => methodToken(method.Module, member);
        internal static int Token(MethodBase method, FieldInfo member) => fieldToken(method.Module, member);
        internal static int Token(MethodBase method, Type type) => typeToken(method.Module, type);

        private static Action<MethodBase, byte[]> CreateReplace()
        {
            Type offsets = typeof(ReadOnlySpan<>).MakeGenericType(typeof(object).Assembly.GetType("WaylithClr.ILOffsetMapEntry", true));
            MethodInfo target = updater.GetMethod("ReplaceMethodBody", new[] { typeof(MethodBase), typeof(ReadOnlySpan<byte>), offsets, typeof(object) });
            var bridge = new DynamicMethod("LihuatReplaceBody", typeof(void), new[] { typeof(MethodBase), typeof(byte[]) }, typeof(RuntimeMethodBodies).Module, true);
            ILGenerator il = bridge.GetILGenerator();
            LocalBuilder map = il.DeclareLocal(offsets);
            il.Emit(OpCodes.Ldloca, map); il.Emit(OpCodes.Initobj, offsets);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, typeof(ReadOnlySpan<byte>).GetMethod("op_Implicit", new[] { typeof(byte[]) }));
            il.Emit(OpCodes.Ldloc, map); il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Call, target); il.Emit(OpCodes.Ret);
            return bridge.CreateDelegate<Action<MethodBase, byte[]>>();
        }

        private static Func<MethodBase, byte[]> CreateRead()
        {
            Assembly core = typeof(object).Assembly;
            Type view = core.GetType("WaylithClr.MethodBodyView", true);
            Type clauses = typeof(Span<>).MakeGenericType(core.GetType("WaylithClr.MethodBodyClause", true));
            MethodInfo target = updater.GetMethod("TryGetDefaultMethodBody", new[] { typeof(MethodBase), clauses, view.MakeByRefType() });
            var bridge = new DynamicMethod("LihuatReadBody", typeof(byte[]), new[] { typeof(MethodBase) }, typeof(RuntimeMethodBodies).Module, true);
            ILGenerator il = bridge.GetILGenerator();
            LocalBuilder exceptions = il.DeclareLocal(clauses), body = il.DeclareLocal(view), code = il.DeclareLocal(typeof(ReadOnlySpan<byte>));
            Label found = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldloc, exceptions); il.Emit(OpCodes.Ldloca, body);
            il.Emit(OpCodes.Call, target); il.Emit(OpCodes.Brtrue_S, found);
            il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Ret);
            il.MarkLabel(found); il.Emit(OpCodes.Ldloca, body); il.Emit(OpCodes.Call, view.GetProperty("IL").GetMethod);
            il.Emit(OpCodes.Stloc, code); il.Emit(OpCodes.Ldloca, code);
            il.Emit(OpCodes.Call, typeof(ReadOnlySpan<byte>).GetMethod("ToArray")); il.Emit(OpCodes.Ret);
            return bridge.CreateDelegate<Func<MethodBase, byte[]>>();
        }
    }
}
