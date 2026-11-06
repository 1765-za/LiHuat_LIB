using System;
using System.Collections;
using System.Collections.Generic;
using System.Buffers;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Verse;

namespace LihuatLib.Mechanisms
{
    public sealed partial class LifeStructureSnapshot
    {
        private sealed class IdentityComparer : IEqualityComparer<object>
        {
            public new bool Equals(object x, object y) => ReferenceEquals(x, y);
            public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
        }
        private struct Mount
        {
            public LifeStructureRemoval Removal;
            public object Parent;
            public FieldInfo ParentField;
            public object Root;
            public bool SameLocation(Mount other) => ReferenceEquals(Parent, other.Parent) && ParentField == other.ParentField && ReferenceEquals(Root, other.Root);
        }
        private sealed class Node
        {
            public object Value;
            public long Generation;
            public LifeAnalysisResult Analysis;
            public Type CustomType;
            public Func<object, object[], bool> AttachmentsMatch;
            public object[] Attachments;
            public readonly List<Mount> Mounts = new List<Mount>();
            public readonly Dictionary<FieldInfo, object> Values = new Dictionary<FieldInfo, object>();
        }
        private readonly Dictionary<object, Node> nodes = new Dictionary<object, Node>(new IdentityComparer());
        private readonly HashSet<object> rejected = new HashSet<object>(new IdentityComparer());
        private readonly LifeResourceEvidence evidence;
        private readonly Pawn pawn;
        private readonly bool captureValues;
        private readonly List<object> roots = new List<object>();
        private readonly List<object> retired = new List<object>();
        private readonly List<ThingComp> componentBuffer = new List<ThingComp>();
        private readonly List<Hediff> hediffBuffer = new List<Hediff>();
        private readonly List<HediffComp> hediffComponentBuffer = new List<HediffComp>();
        private readonly HashSet<FieldInfo> fields = new HashSet<FieldInfo>();
        private long generation;
        private bool pendingDetach;

        public LifeStructureSnapshot(Pawn pawn, LifeResourceEvidence evidence) : this(pawn, evidence, true) { }

        internal LifeStructureSnapshot(Pawn pawn, LifeResourceEvidence evidence, bool captureValues)
        {
            this.pawn = pawn;
            this.evidence = evidence;
            this.captureValues = captureValues;
            if (!captureValues)
            {
                collections = new LifeCollectionState();
                bindingNodes = new();
                structureTypes = new();
            }
            Refresh();
        }

        internal void Refresh()
        {
            generation++;
            roots.Clear(); rejected.Clear(); pendingDetach = false;
            rootHediffGroups.Clear();
            BeginStructureCapture();
            Capture(pawn, default);
            Capture(pawn.health, default);
            Capture(pawn.health.hediffSet, default);
            CopyInto(componentBuffer, pawn.AllComps);
            rootThingCount = componentBuffer.Count;
            foreach (ThingComp comp in componentBuffer)
            { roots.Add(comp); Capture(comp, new LifeStructureRemoval(LifeStructureLocation.PawnComp, pawn, comp), pawn.AllComps); }
            componentBuffer.Clear();
            CopyInto(hediffBuffer, pawn.health.hediffSet.hediffs);
            rootHediffCount = hediffBuffer.Count;
            foreach (Hediff hediff in hediffBuffer) roots.Add(hediff);
            foreach (Hediff hediff in hediffBuffer)
            {
                Capture(hediff, new LifeStructureRemoval(LifeStructureLocation.Hediff, pawn, hediff), pawn.health.hediffSet.hediffs);
                if (hediff is HediffWithComps condition)
                    rootHediffGroups.Add((condition, condition.comps, condition.comps?.Count ?? 0));
                if (hediff is HediffWithComps withComps && withComps.comps != null)
                {
                    CopyInto(hediffComponentBuffer, withComps.comps);
                    foreach (HediffComp comp in hediffComponentBuffer)
                    { roots.Add(comp); Capture(comp, new LifeStructureRemoval(LifeStructureLocation.HediffComp, withComps, comp), withComps.comps); }
                    hediffComponentBuffer.Clear();
                }
            }
            hediffBuffer.Clear();
            foreach (Node node in nodes.Values)
                if (node.Generation != generation) retired.Add(node.Value);
            foreach (object value in retired) nodes.Remove(value);
            retired.Clear();
            if (nodes.EnsureCapacity(0) > Math.Max(512, nodes.Count * 4)) nodes.TrimExcess();
            if (roots.Capacity > Math.Max(512, roots.Count * 4)) roots.TrimExcess();
            if (rootHediffGroups.Capacity > Math.Max(512, rootHediffGroups.Count * 4)) rootHediffGroups.TrimExcess();
            if (retired.Capacity > 512) retired.TrimExcess();
            CompleteStructureCapture();
        }

        private static void CopyInto<T>(List<T> buffer, List<T> source)
        {
            buffer.Clear(); buffer.AddRange(source);
            if (buffer.Capacity > Math.Max(512, buffer.Count * 4)) buffer.TrimExcess();
        }

        private void Capture(object value, LifeStructureRemoval removal, object parent = null, FieldInfo parentField = null, object root = null)
        {
            if (value == null) return;
            var mount = new Mount { Removal = removal, Parent = parent, ParentField = parentField, Root = root ?? value };
            if (nodes.TryGetValue(value, out Node node))
            {
                if (node.Generation == generation) { AddMount(node, mount); return; }
                node.Mounts.Clear(); node.Values.Clear();
            }
            else { node = new Node { Value = value }; nodes.Add(value, node); }
            node.Generation = generation;
            AddMount(node, mount);
            Type type = value.GetType();
            node.Analysis = null;
            node.CustomType = null;
            if (!LifeResourceEvidence.IsCustom(type)) return;
            node.CustomType = type;
            node.Analysis = evidence.Observe(value, pawn, captureValues ? fields : null);
            if (bindingNodes != null && node.Analysis?.Sites.Length > 0) bindingNodes.Add(node);
            if (captureValues) foreach (FieldInfo field in fields) node.Values[field] = field.GetValue(value);
            LifeStructureFields layout = LifeStructureFields.For(type);
            if (!captureValues)
            {
                structureTypes.TryAdd(type, node.Analysis);
                node.AttachmentsMatch ??= layout.PrepareAttachmentMatcher();
                node.Attachments ??= new object[layout.Attachments.Length];
            }
            FieldInfo[] scanFields = captureValues ? layout.All : layout.Attachments;
            for (int index = 0; index < scanFields.Length; index++)
                {
                    FieldInfo field = scanFields[index];
                    object child = field.GetValue(value);
                    node.Values[field] = child;
                    if (!captureValues) node.Attachments[index] = child;
                    if (child == null || child is Pawn || child is Thing || child is Def || child is string || child.GetType().IsValueType) continue;
                    if (child is IDictionary dictionary)
                    {
                        CaptureCollection(dictionary.Values, dictionary, dictionary.IsReadOnly || dictionary.IsFixedSize, removal, mount.Root);
                    }
                    else if (child is IList list && !list.IsReadOnly)
                    {
                        CaptureCollection(list, list, false, removal, mount.Root);
                    }
                    else if (LifeResourceEvidence.IsCustom(child.GetType()))
                        Capture(child, field.IsInitOnly ? removal : new LifeStructureRemoval(LifeStructureLocation.Field, value, child, field), value, field, mount.Root);
                }
        }

        private void AddMount(Node node, Mount mount)
        {
            foreach (Mount existing in node.Mounts)
                if (existing.SameLocation(mount)) return;
            node.Mounts.Add(mount);
        }

        private void CaptureCollection(ICollection items, object parent, bool removeParent, LifeStructureRemoval removal, object root)
        {
            int count = items.Count;
            if (count == 0) { collections?.Capture(parent, items, Array.Empty<object>(), 0); return; }
            object[] copy = ArrayPool<object>.Shared.Rent(count);
            try
            {
                items.CopyTo(copy, 0);
                collections?.Capture(parent, items, copy, count);
                for (int i = 0; i < count; i++)
                {
                    object item = copy[i];
                    if (item != null && !item.GetType().IsValueType && LifeResourceEvidence.IsCustom(item.GetType()))
                        Capture(item, removeParent ? removal : new LifeStructureRemoval(LifeStructureLocation.Collection, parent, item), parent, root: root);
                }
            }
            finally { ArrayPool<object>.Shared.Return(copy, clearArray: true); }
        }

        public void ExcludeUndeclaredComponents()
        {
            foreach (Node node in nodes.Values.ToArray())
            {
                bool injected = node.Mounts.Count > 0 && node.Mounts.All(m =>
                    m.Root is ThingComp thing && !(pawn.def.comps?.Any(p => p.compClass == thing.GetType()) == true)
                    || m.Root is HediffComp hediff && !(hediff.parent.def.comps?.Any(p => p.compClass == hediff.GetType()) == true));
                if (injected) { nodes.Remove(node.Value); pendingDetach = true; }
            }
        }

        public int RemainingRejected(LifeStructureSnapshot inspected) => inspected.rejected.Count(value => nodes.ContainsKey(value));

        public int ExcludeRejected(LifeStructureSnapshot inspected)
        {
            int remaining = 0;
            foreach (object value in inspected.rejected)
                if (nodes.Remove(value)) { pendingDetach = true; remaining++; }
            return remaining;
        }

        public bool HasNewStructures(LifeStructureSnapshot before)
        {
            foreach (Node node in nodes.Values)
            {
                if (!before.nodes.TryGetValue(node.Value, out Node previous)) return true;
                foreach (Mount mount in node.Mounts)
                {
                    bool found = false;
                    foreach (Mount old in previous.Mounts)
                        if (mount.SameLocation(old)) { found = true; break; }
                    if (!found) return true;
                }
            }
            return false;
        }

        public int StripIntroduced(LifeStructureSnapshot before, Action<string> report, bool restoreFields = true, bool componentsOnly = false, Action<Pawn, Type> detached = null)
        {
            int stripped = 0;
            foreach (Node node in nodes.Values)
            {
                Mount[] mounts = node.Mounts.Where(m => !componentsOnly || m.Root is ThingComp || m.Root is HediffComp).ToArray();
                if (mounts.Length == 0) continue;
                HashSet<FieldInfo> fields = evidence.Fields(node.Value, pawn);
                if (fields.Count == 0) continue;
                bool existed = before.nodes.TryGetValue(node.Value, out Node previous);
                Mount[] introduced = existed ? mounts.Where(m => !previous.Mounts.Any(m.SameLocation)).ToArray() : mounts;
                if (introduced.Any(m => m.Removal.Available))
                {
                    if (!existed) rejected.Add(node.Value);
                    foreach (Mount mount in introduced)
                    {
                        if (!mount.Removal.Available) continue;
                        if (mount.Parent != null && mount.ParentField?.IsInitOnly == false
                            && before.nodes.TryGetValue(mount.Parent, out Node oldParent)
                            && oldParent.Values.TryGetValue(mount.ParentField, out object oldReference))
                            mount.ParentField.SetValue(mount.Parent, oldReference);
                        else mount.Removal.Apply();
                    }
                    detached?.Invoke(pawn, node.Value.GetType());
                    foreach (object root in introduced.Select(m => m.Root).Distinct(new IdentityComparer()))
                        if (!ReferenceEquals(root, node.Value)) detached?.Invoke(pawn, root.GetType());
                    stripped++;
                    report(node.Value.GetType().FullName + ": rejected introduced life structure");
                    continue;
                }
                if (!existed || !restoreFields) continue;
                foreach (FieldInfo field in fields)
                    if (previous.Values.TryGetValue(field, out object original) && node.Values.TryGetValue(field, out object current)
                        && !Equals(original, current) && !field.IsInitOnly)
                    {
                        field.SetValue(node.Value, original); stripped++;
                        report(node.Value.GetType().FullName + "." + field.Name + ": restored pre-attack life field");
                    }
            }
            if (stripped > 0) pawn.health.hediffSet.DirtyCache();
            return stripped;
        }
    }
}
