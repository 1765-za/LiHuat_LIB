using System;
using System.Collections.Generic;
using System.Reflection;

namespace LihuatLib.Combat
{
    internal enum ReadOrigin { Value, Borrowed, Owned }

    internal readonly record struct ManagedReadValue(ReadOrigin Origin, Type Type = null, int? Constant = null,
        int Local = -1, int Argument = -1, bool Address = false, bool Writable = false, FieldInfo Delegate = null, bool Subject = false)
    {
        internal static ManagedReadValue Scalar(Type type = null, int? value = null) => new(ReadOrigin.Value, type, value);
        internal static ManagedReadValue External(Type type) => new(ReadOrigin.Borrowed, type);
        internal static ManagedReadValue Owned(Type type) => new(ReadOrigin.Owned, type);
        internal static ManagedReadValue For(Type type) => type.IsPrimitive || type.IsEnum || type.IsValueType && !type.IsByRefLike
            ? Scalar(type) : External(type);
        internal static ManagedReadValue Merge(ManagedReadValue left, ManagedReadValue right)
        {
            if (left == right) return left;
            return new ManagedReadValue(left.Origin == right.Origin ? left.Origin : ReadOrigin.Borrowed,
                left.Type == right.Type ? left.Type : null, left.Constant == right.Constant ? left.Constant : null,
                left.Local == right.Local ? left.Local : -1, left.Argument == right.Argument ? left.Argument : -1,
                left.Address && right.Address, left.Writable && right.Writable, left.Delegate == right.Delegate ? left.Delegate : null, left.Subject && right.Subject);
        }
    }

    internal sealed class ManagedReadFrame
    {
        internal readonly List<ManagedReadValue> Stack = new();
        internal ManagedReadValue[] Locals, Arguments;
        internal bool[] WritableArguments;
        internal ManagedReadFrame Clone()
        {
            var result = new ManagedReadFrame { Locals = (ManagedReadValue[])Locals.Clone(), Arguments = (ManagedReadValue[])Arguments.Clone(), WritableArguments = WritableArguments };
            result.Stack.AddRange(Stack); return result;
        }
        internal ManagedReadValue Resolve(ManagedReadValue value) => value.Local >= 0 ? Locals[value.Local]
            : value.Argument >= 0 ? Arguments[value.Argument] : value;
        internal bool Write(ManagedReadValue address, ManagedReadValue value)
        {
            if (!address.Address || !address.Writable) return false;
            if (address.Local >= 0) Locals[address.Local] = value;
            else if (address.Argument >= 0) Arguments[address.Argument] = value;
            return true;
        }
        internal ManagedReadValue Pop()
        {
            if (Stack.Count == 0) throw new InvalidProgramException("Read analysis stack is empty.");
            var value = Stack[^1]; Stack.RemoveAt(Stack.Count - 1); return value;
        }
        internal bool Merge(ManagedReadFrame incoming, out bool changed)
        {
            changed = false;
            if (Stack.Count != incoming.Stack.Count) return false;
            for (int i = 0; i < Stack.Count; i++) { var value = ManagedReadValue.Merge(Stack[i], incoming.Stack[i]); changed |= value != Stack[i]; Stack[i] = value; }
            for (int i = 0; i < Locals.Length; i++) { var value = ManagedReadValue.Merge(Locals[i], incoming.Locals[i]); changed |= value != Locals[i]; Locals[i] = value; }
            for (int i = 0; i < Arguments.Length; i++) { var value = ManagedReadValue.Merge(Arguments[i], incoming.Arguments[i]); changed |= value != Arguments[i]; Arguments[i] = value; }
            return true;
        }
    }
}
