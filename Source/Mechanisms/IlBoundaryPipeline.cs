using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Linq;

namespace LihuatLib.Mechanisms
{
    internal sealed class IlBoundaryPipeline
    {
        internal sealed class Layer
        {
            internal string Id;
            internal int Order;
            internal Func<MethodInfo, DynamicMethod> Build;
            internal bool BypassDeveloper;
        }
        private readonly List<Layer> layers = new List<Layer>();
        internal MethodInfo Source;
        internal int Count => layers.Count;
        internal IlBoundaryPipeline(MethodInfo source) { Source = source; }
        internal void Add(string id, int order, Func<MethodInfo, DynamicMethod> build, bool bypassDeveloper = true)
        {
            foreach (Layer layer in layers)
            {
                if (layer.Id == id) throw new InvalidOperationException("IL boundary already registered: " + id);
                if (layer.Order == order) throw new InvalidOperationException("IL boundary order is ambiguous: " + id + " and " + layer.Id);
            }
            layers.Add(new Layer { Id = id, Order = order, Build = build, BypassDeveloper = bypassDeveloper });
            layers.Sort((a, b) => a.Order.CompareTo(b.Order));
        }
        internal void Remove(string id) => layers.RemoveAll(layer => layer.Id == id);
        internal Layer Find(string id) => layers.Find(layer => layer.Id == id);
        internal DynamicMethod Compose(MethodInfo source = null)
        {
            source = source ?? Source;
            MethodInfo result = source, developer = source;
            bool separated = false;
            foreach (Layer layer in layers)
            {
                result = layer.Build(result);
                if (layer.BypassDeveloper) separated = true;
                else developer = separated ? layer.Build(developer) : result;
            }
            Type[] arguments = IndependentIl.Parameters(source);
            if (!ReferenceEquals(result, source) && ReferenceEquals(result, developer)
                && result is DynamicMethod direct && direct.ReturnType == source.ReturnType
                && arguments.SequenceEqual(IndependentIl.Parameters(direct))) return direct;
            DynamicMethod wrapper = IndependentIl.New(source);
            ILGenerator il = wrapper.GetILGenerator();
            if (!ReferenceEquals(result, developer))
            {
                Label guarded = il.DefineLabel();
                il.Emit(OpCodes.Call, typeof(Core.DeveloperExecution).GetProperty(nameof(Core.DeveloperExecution.Active)).GetGetMethod());
                il.Emit(OpCodes.Brfalse, guarded);
                EmitCall(il, arguments, developer);
                il.MarkLabel(guarded);
            }
            EmitCall(il, arguments, result);
            return wrapper;
        }

        private static void EmitCall(ILGenerator il, Type[] arguments, MethodInfo result)
        {
            Type[] expected = IndependentIl.Parameters(result);
            if (arguments.Length != expected.Length) throw new ArgumentException("IL copy argument count differs.");
            for (int i = 0; i < arguments.Length; i++)
            {
                il.Emit(OpCodes.Ldarg, (short)i);
                if (arguments[i] == expected[i] || expected[i].IsAssignableFrom(arguments[i])) continue;
                if (arguments[i].IsValueType || arguments[i].IsByRef || arguments[i].IsPointer
                    || expected[i].IsValueType || expected[i].IsByRef || expected[i].IsPointer)
                    throw new ArgumentException("IL copy argument type differs at " + i);
                il.Emit(OpCodes.Castclass, expected[i]);
            }
            il.Emit(OpCodes.Call, result);
            il.Emit(OpCodes.Ret);
        }
    }
}
