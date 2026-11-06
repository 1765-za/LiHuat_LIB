using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace LihuatLib.Mechanisms
{
    internal static class ErasureDeparture
    {
        private static readonly Dictionary<MethodInfo, DynamicMethod> copies = new Dictionary<MethodInfo, DynamicMethod>();
        private static readonly Action<Thing, DestroyMode> despawn = (Action<Thing, DestroyMode>)Copy(AccessTools.Method(typeof(Thing), nameof(Thing.DeSpawn))).CreateDelegate(typeof(Action<Thing, DestroyMode>));
        private static readonly Action<Pawn_PathFollower> stopPath = Copy(AccessTools.Method(typeof(Pawn_PathFollower), nameof(Pawn_PathFollower.StopDead))).CreateDelegate<Action<Pawn_PathFollower>>();
        private static readonly AccessTools.FieldRef<Thing, sbyte> mapIndex = AccessTools.FieldRefAccess<Thing, sbyte>("mapIndexOrState");

        internal static void Remove(Thing thing)
        {
            if (thing is Pawn pawn && pawn.pather != null) stopPath(pawn.pather);
            int index = mapIndex(thing);
            if (index >= 0 && index < Find.Maps.Count) despawn(thing, DestroyMode.Vanish);
        }

        private static DynamicMethod Copy(MethodInfo original)
        {
            if (copies.TryGetValue(original, out DynamicMethod copy)) return copy;
            copy = IndependentIl.New(original);
            copies.Add(original, copy);
            MethodBody body = NativeRuntime.SourceBody(original);
            if (!PortableIlEmitter.TryEmit(original, body, CilRewrite.ReadSource(original, body), copy.GetILGenerator(), out string reason, Resolve))
            { copies.Remove(original); throw new InvalidOperationException("Erasure departure IL emission failed: " + reason); }
            return copy;
        }

        private static MethodInfo Resolve(MethodInfo method, bool virtualCall)
        {
            Type type = method.DeclaringType;
            string name = method.Name;
            if (type == typeof(ListerThings) && name == nameof(ListerThings.Remove)) return AccessTools.Method(typeof(ThingListerStorage), nameof(ThingListerStorage.RemoveStored));
            if (type == typeof(List<Thing>) && name == "Remove") return AccessTools.Method(typeof(ErasureDeparture), nameof(RemoveThing));
            if (type == typeof(List<object>) && name == "Remove") return AccessTools.Method(typeof(ErasureDeparture), nameof(RemoveObject));
            if (type == typeof(Action<Thing>) && name == "Invoke") return AccessTools.Method(typeof(ErasureNotifications), nameof(ErasureNotifications.Invoke));
            bool selected = type == typeof(Thing) && (name == "get_Destroyed" || name == "get_Spawned" || name == "get_Map" || name == "get_Position")
                || type == typeof(RegionListersUpdater) && name == "DeregisterInRegions"
                || type == typeof(ThingGrid) && (name == "Deregister" || name == "DeregisterInCell")
                || type == typeof(DynamicDrawManager) && name == "DeRegisterDrawable"
                || type == typeof(Selector) && (name == "Deselect" || name == "DeselectInternal" || name == "IsSelected")
                || type == typeof(OverlayDrawer) && name == "DisposeHandle";
            selected |= type == typeof(MapEvents) && name == nameof(MapEvents.Notify_ThingDespawned);
            selected |= type == typeof(Pawn_PathFollower) && (name == nameof(Pawn_PathFollower.DisposeAndClearCurPathRequest)
                || name == nameof(Pawn_PathFollower.DisposeAndClearCurPath));
            selected |= (type == typeof(PathRequest) || type == typeof(PawnPath)) && name == nameof(IDisposable.Dispose);
            return selected ? Copy(method) : method;
        }

        private static bool RemoveThing(List<Thing> list, Thing thing) => ReferenceListStorage<Thing>.Remove(list, thing) != 0;
        private static bool RemoveObject(List<object> list, object value) => ReferenceListStorage<object>.Remove(list, value) != 0;
    }
}
