using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using LihuatLib.Mechanisms;

namespace LihuatLib.Dispatching
{
    internal sealed class GenericDispatchEntry
    {
        private readonly MethodInfo target;
        private readonly ModuleBuilder module;
        private readonly List<MethodInfo> retained = new();
        private byte[] current;
        internal MethodInfo Control { get; private set; }

        internal GenericDispatchEntry(MethodInfo target)
        {
            this.target = target;
            module = NativeRuntime.PublicationModule(target);
        }

        internal void Publish(IReadOnlyList<DispatchHandler> handlers)
        {
            var source = new GenericMethodEmission(target, module);
            NativeDispatcher.EmitSource(target, source.IL, handlers, source.EmitRuntimeCall);
            MethodInfo body = source.Complete();
            var wrapper = new GenericMethodEmission(target, module);
            DispatchCompiler.Emit(target, body, handlers, wrapper.IL);
            MethodInfo control = wrapper.Complete();
            byte[] next = Forward(control);
            GenericBodyPublication.Protect(body);
            try { GenericBodyPublication.Protect(control); }
            catch { GenericBodyPublication.Release(body); throw; }
            try { GenericBodyPublication.Publish(target, next, current); }
            catch { GenericBodyPublication.Release(control); GenericBodyPublication.Release(body); throw; }
            retained.Add(body); retained.Add(control);
            current = next;
            Control = control;
        }

        private byte[] Forward(MethodInfo control)
        {
            using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
            int count = IndependentIl.Parameters(target).Length;
            for (int i = 0; i < count; i++)
            {
                if (i < 4) writer.Write((byte)(0x02 + i));
                else { writer.Write((byte)0xfe); writer.Write((byte)0x09); writer.Write((ushort)i); }
            }
            writer.Write((byte)0x28); writer.Write(RuntimeMethodBodies.Token(target, control)); writer.Write((byte)0x2a);
            return MethodBodyBlob.Create(stream.ToArray(), Math.Max(1, count));
        }
    }
}
