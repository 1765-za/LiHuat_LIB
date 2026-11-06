using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace LihuatLib.Mechanisms
{
    internal static class ContainerTransferBoundary
    {
        private sealed class EntryFields
        {
            internal readonly FieldInfo Pawn;
            internal readonly FieldInfo Casket;
            internal EntryFields(Type type)
            {
                FieldInfo[] fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                Pawn = fields.Single(f => f.FieldType == typeof(Pawn));
                Casket = fields.Single(f => typeof(Building_Casket).IsAssignableFrom(f.FieldType));
            }
        }
        private static readonly Dictionary<Type, EntryFields> entries = new Dictionary<Type, EntryFields>();

        internal static void Install()
        {
            var reader = new LifeIlReader();
            InstallEntries(typeof(JobDriver_EnterCryptosleepCasket), reader);
            InstallEntries(typeof(JobDriver_EnterTransporter), reader);
            VanillaEntryBoundary.Install(reader);
            foreach (MethodInfo method in AccessTools.GetDeclaredMethods(typeof(Pawn_CarryTracker)).Where(m => m.Name == nameof(Pawn_CarryTracker.TryStartCarry)))
                Scope(method, method.ReturnType == typeof(bool) ? nameof(BeginCarry) : nameof(BeginCarryCount), 0, 1);
            foreach (MethodInfo method in AccessTools.GetDeclaredMethods(typeof(ThingOwner)))
            {
                ParameterInfo[] parameters = method.GetParameters();
                if (method.Name == nameof(ThingOwner.TryDrop) && parameters.Length >= 4
                    && parameters[1].ParameterType == typeof(IntVec3) && parameters[2].ParameterType == typeof(Map))
                    Scope(method, nameof(BeginDrop), 0, 1, 2, 3);
                if (method.Name == nameof(ThingOwner.TryTransferToContainer) && parameters.Any(p => p.IsOut))
                    Scope(method, nameof(BeginTransfer), 0, 1, 2);
            }
        }

        private static void InstallEntries(Type type, LifeIlReader reader)
        {
            foreach (MethodInfo method in AccessTools.GetDeclaredMethods(type))
            {
                if (method.IsStatic || method.ReturnType != typeof(void) || method.GetParameters().Length != 0) continue;
                if (reader.Read(method)?.Any(i => i.operand is MethodInfo called && called.DeclaringType == typeof(Thing)
                    && called.Name == nameof(Thing.DeSpawnOrDeselect)) != true) continue;
                if (type != typeof(JobDriver_EnterTransporter)) entries.Add(type, new EntryFields(type));
                Scope(method, nameof(BeginEntry), 0);
            }
            foreach (Type nested in type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)) InstallEntries(nested, reader);
        }

        private static void Scope(MethodInfo method, string begin, params int[] arguments)
            => NativeIlGuards.Scoped(method, AccessTools.Method(typeof(ContainerTransferBoundary), begin), arguments,
                AccessTools.Method(typeof(ContainerTransferBoundary), nameof(End)), Array.Empty<int>());

        private static bool BeginEntry(object receiver, out ContainerTransferScope scope)
        {
            scope = null;
            Pawn pawn; ThingOwner owner;
            if (receiver is JobDriver_EnterTransporter driver)
            { pawn = driver.pawn; owner = driver.Transporter?.GetDirectlyHeldThings(); }
            else
            {
                EntryFields fields = entries[receiver.GetType()];
                pawn = (Pawn)fields.Pawn.GetValue(receiver);
                owner = ((Building_Casket)fields.Casket.GetValue(receiver))?.GetDirectlyHeldThings();
            }
            if (!ContainerTransferScope.AllowsEnter(pawn, owner)) return false;
            scope = ContainerTransferScope.Enter(pawn, owner);
            return true;
        }

        private static bool BeginCarry(Pawn_CarryTracker tracker, Thing thing, out ContainerTransferScope scope, ref bool result)
        { scope = null; if (!ContainerTransferScope.AllowsEnter(thing, tracker.innerContainer)) return false; scope = ContainerTransferScope.Enter(thing, tracker.innerContainer); return true; }

        private static bool BeginCarryCount(Pawn_CarryTracker tracker, Thing thing, out ContainerTransferScope scope, ref int result)
        { scope = null; if (!ContainerTransferScope.AllowsEnter(thing, tracker.innerContainer)) return false; scope = ContainerTransferScope.Enter(thing, tracker.innerContainer); return true; }

        private static bool BeginDrop(ThingOwner owner, Thing thing, IntVec3 cell, Map map, out ContainerTransferScope scope, ref bool result)
        { scope = null; if (!ContainerTransferScope.AllowsLeave(thing, map)) return false; scope = ContainerTransferScope.Leave(owner, thing, cell, map); return true; }

        private static bool BeginTransfer(ThingOwner owner, Thing thing, ThingOwner destination, out ContainerTransferScope scope, ref int result)
        {
            scope = null;
            if (!ContainerTransferScope.AllowsEnter(thing, destination)) return false;
            scope = thing is Pawn && ContainerTransferScope.Owns(owner, thing) ? ContainerTransferScope.Enter(thing, destination) : null;
            return true;
        }

        private static void End(ContainerTransferScope scope) => scope?.Dispose();
    }
}
