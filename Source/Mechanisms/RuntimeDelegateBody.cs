using System;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace LihuatLib.Mechanisms
{
    internal sealed class RuntimeDelegateBody
    {
        private static readonly ConditionalWeakTable<Type, RuntimeDelegateBody> sources = new();
        private readonly FieldInfo target;
        private readonly MethodBase original;
        private Delegate current;
        internal readonly byte[] Body;
        internal bool Intact => ReferenceEquals(target.GetValue(null), current);

        internal RuntimeDelegateBody(MethodBase original, ModuleBuilder module)
        {
            this.original = original;
            Type[] signature = IndependentIl.Parameters(original);
            Type delegateType = Expression.GetDelegateType(signature.Concat(new[] { IndependentIl.ResultType(original) }).ToArray());
            TypeBuilder type = module.DefineType("Publication_" + Guid.NewGuid().ToString("N"), TypeAttributes.Public | TypeAttributes.Sealed);
            type.DefineField("Target", delegateType, FieldAttributes.Public | FieldAttributes.Static);
            target = type.CreateType().GetField("Target");
            using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
            writer.Write((byte)0x7e); writer.Write(RuntimeMethodBodies.Token(original, target));
            for (int i = 0; i < signature.Length; i++)
            {
                if (i < 4) writer.Write((byte)(0x02 + i));
                else { writer.Write((byte)0xfe); writer.Write((byte)0x09); writer.Write((ushort)i); }
            }
            writer.Write((byte)0x6f); writer.Write(RuntimeMethodBodies.Token(original, delegateType.GetMethod("Invoke")));
            writer.Write((byte)0x2a);
            Body = MethodBodyBlob.Create(stream.ToArray(), signature.Length + 1);
            sources.Add(target.DeclaringType, this);
        }

        internal static MethodInfo SourceOf(FieldInfo field) => field?.DeclaringType != null
            && sources.TryGetValue(field.DeclaringType, out RuntimeDelegateBody body) && body.target.Equals(field)
                ? body.original as MethodInfo : null;

        internal void Publish(MethodInfo replacement)
        {
            Delegate next = replacement.CreateDelegate(target.FieldType);
            target.SetValue(null, next);
            current = next;
        }
    }
}
