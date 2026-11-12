using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;

namespace LihuatLib.Mechanisms
{
    internal static class RuntimeBodyBoundary
    {
        private static readonly Type info = typeof(object).Assembly.GetType("System.IRuntimeMethodInfo", true);
        private static readonly Func<object, IntPtr> handle = CreateHandleReader();
        private static readonly Func<RuntimeMethodHandle, MethodBase> metadata = CreateMetadataReader();
        private static readonly Func<object, RuntimeMethodHandle> runtimeHandle = CreateRuntimeHandleReader();
        private static HashSet<IntPtr> protectedMethods = new HashSet<IntPtr>();
        private static HashSet<MethodBase> genericDefinitions = new();
        internal static MethodBase Resolve(RuntimeMethodHandle method) => metadata(method);

        internal static void Track(MethodBase method)
        {
            if (GenericMethodIdentity.Applies(method))
            {
                var definitions = new HashSet<MethodBase>(Volatile.Read(ref genericDefinitions)) { GenericMethodIdentity.Definition((MethodInfo)method) };
                Volatile.Write(ref genericDefinitions, definitions);
            }
            var next = new HashSet<IntPtr>(Volatile.Read(ref protectedMethods)) { method.MethodHandle.Value };
            Volatile.Write(ref protectedMethods, next);
        }

        internal static void Release(MethodBase method)
        {
            if (GenericMethodIdentity.Applies(method))
            {
                var definitions = new HashSet<MethodBase>(Volatile.Read(ref genericDefinitions));
                definitions.Remove(GenericMethodIdentity.Definition((MethodInfo)method));
                Volatile.Write(ref genericDefinitions, definitions);
            }
            var next = new HashSet<IntPtr>(Volatile.Read(ref protectedMethods));
            next.Remove(method.MethodHandle.Value);
            Volatile.Write(ref protectedMethods, next);
        }

        internal static void Install()
        {
            if (!LihuatLib.Configuration.HighMechanismPolicy.NativeProtection) return;
            Assembly core = typeof(object).Assembly;
            Type updater = core.GetType("WaylithClr.MethodBodyUpdater", true);
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
            Type offsets = typeof(ReadOnlySpan<>).MakeGenericType(core.GetType("WaylithClr.ILOffsetMapEntry", true));
            Type tokens = typeof(ReadOnlySpan<>).MakeGenericType(core.GetType("System.Reflection.Emit.DynamicScope", true));
            MethodInfo replace = updater.GetMethod("ReplaceMethodBodiesCore", flags, null,
                new[] { info.MakeArrayType(), typeof(ReadOnlySpan<byte>), typeof(ReadOnlySpan<int>), offsets,
                    typeof(ReadOnlySpan<int>), typeof(ReadOnlySpan<object>), tokens }, null)
                ?? throw new MissingMethodException(updater.FullName, "ReplaceMethodBodiesCore");
            MethodInfo revert = updater.GetMethod("Revert", new[] { typeof(ReadOnlySpan<RuntimeMethodHandle>) })
                ?? throw new MissingMethodException(updater.FullName, "Revert(ReadOnlySpan<RuntimeMethodHandle>)");
            NativeIlGuards.Guard(replace, typeof(RuntimeBodyBoundary).GetMethod(nameof(RequireBatch), flags), 0);
            NativeIlGuards.Guard(revert, typeof(RuntimeBodyBoundary).GetMethod(nameof(RequireHandles), flags), 0);
        }

        private static void RequireBatch(object[] infos)
        {
            if (RuntimeMethodBodies.Writing) return;
            HashSet<IntPtr> protectedSnapshot = Volatile.Read(ref protectedMethods);
            foreach (object methodInfo in infos)
                if (protectedSnapshot.Contains(handle(methodInfo)) || ProtectedDefinition(runtimeHandle(methodInfo)))
                    throw new UnauthorizedAccessException("Cannot replace or revert a protected method body.");
        }

        private static void RequireHandles(ReadOnlySpan<RuntimeMethodHandle> methods)
        {
            if (RuntimeMethodBodies.Writing) return;
            HashSet<IntPtr> protectedSnapshot = Volatile.Read(ref protectedMethods);
            foreach (RuntimeMethodHandle method in methods)
                if (protectedSnapshot.Contains(method.Value) || ProtectedDefinition(method))
                    throw new UnauthorizedAccessException("Cannot replace or revert a protected method body.");
        }

        private static Func<object, IntPtr> CreateHandleReader()
        {
            var method = new DynamicMethod("LihuatRuntimeMethodHandle", typeof(IntPtr), new[] { typeof(object) }, typeof(RuntimeBodyBoundary).Module, true);
            ILGenerator il = method.GetILGenerator();
            MethodInfo value = info.GetProperty("Value").GetMethod;
            LocalBuilder local = il.DeclareLocal(value.ReturnType);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Castclass, info); il.Emit(OpCodes.Callvirt, value); il.Emit(OpCodes.Stloc, local);
            il.Emit(OpCodes.Ldloca, local);
            il.Emit(OpCodes.Call, value.ReturnType.GetProperty("Value", BindingFlags.NonPublic | BindingFlags.Instance).GetMethod);
            il.Emit(OpCodes.Ret);
            return method.CreateDelegate<Func<object, IntPtr>>();
        }

        private static bool ProtectedDefinition(RuntimeMethodHandle method)
        {
            HashSet<MethodBase> definitions = Volatile.Read(ref genericDefinitions);
            MethodBase candidate = metadata(method);
            if (InfrastructureMethodProtection.Contains(candidate)) return true;
            return candidate is MethodInfo generic && GenericMethodIdentity.Applies(generic)
                && definitions.Contains(GenericMethodIdentity.Definition(generic));
        }

        private static Func<object, RuntimeMethodHandle> CreateRuntimeHandleReader()
        {
            var bridge = new DynamicMethod("LihuatRuntimeHandle", typeof(RuntimeMethodHandle), new[] { typeof(object) }, typeof(RuntimeBodyBoundary).Module, true);
            ILGenerator il = bridge.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Castclass, info);
            il.Emit(OpCodes.Newobj, typeof(RuntimeMethodHandle).GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { info }, null));
            il.Emit(OpCodes.Ret);
            return bridge.CreateDelegate<Func<object, RuntimeMethodHandle>>();
        }

        private static Func<RuntimeMethodHandle, MethodBase> CreateMetadataReader()
        {
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
            MethodInfo owner = typeof(RuntimeMethodHandle).GetMethod("GetDeclaringType", flags, null, new[] { info }, null);
            var bridge = new DynamicMethod("LihuatRuntimeDefinition", typeof(MethodBase), new[] { typeof(RuntimeMethodHandle) }, typeof(RuntimeBodyBoundary).Module, true);
            ILGenerator il = bridge.GetILGenerator();
            LocalBuilder type = il.DeclareLocal(typeof(Type));
            Label found = il.DefineLabel();
            il.Emit(OpCodes.Ldarga_S, (byte)0);
            il.Emit(OpCodes.Call, typeof(RuntimeMethodHandle).GetMethod("GetMethodInfo", BindingFlags.NonPublic | BindingFlags.Instance));
            il.Emit(OpCodes.Call, owner);
            il.Emit(OpCodes.Stloc, type); il.Emit(OpCodes.Ldloc, type); il.Emit(OpCodes.Brtrue, found);
            il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Ret);
            il.MarkLabel(found); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldloc, type);
            il.Emit(OpCodes.Callvirt, typeof(Type).GetProperty(nameof(Type.TypeHandle)).GetMethod);
            il.Emit(OpCodes.Call, typeof(MethodBase).GetMethod(nameof(MethodBase.GetMethodFromHandle), new[] { typeof(RuntimeMethodHandle), typeof(RuntimeTypeHandle) }));
            il.Emit(OpCodes.Ret);
            return bridge.CreateDelegate<Func<RuntimeMethodHandle, MethodBase>>();
        }
    }
}
