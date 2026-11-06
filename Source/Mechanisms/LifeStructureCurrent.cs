using System.Collections.Generic;
using System;
using Verse;

namespace LihuatLib.Mechanisms
{
    public sealed partial class LifeStructureSnapshot
    {
        private readonly LifeCollectionState collections;
        private readonly List<Node> bindingNodes;
        private readonly Dictionary<Type, LifeAnalysisResult> structureTypes;
        private Pawn_HealthTracker rootHealth;
        private HediffSet rootSet;
        private List<ThingComp> rootComponents;
        private List<Hediff> rootConditions;
        private int assemblyRevision;

        private void BeginStructureCapture()
        {
            if (collections == null) return;
            rootHealth = pawn.health; rootSet = pawn.health.hediffSet;
            rootComponents = pawn.AllComps; rootConditions = rootSet.hediffs;
            bindingNodes.Clear(); structureTypes.Clear(); collections.Begin();
        }

        private void CompleteStructureCapture()
        {
            if (collections == null) return;
            collections.Complete();
            assemblyRevision = LifeMethodRevisions.AssemblyRevision;
        }

        internal bool StructureMatches()
        {
            if (collections == null || assemblyRevision != LifeMethodRevisions.AssemblyRevision
                || !ReferenceEquals(rootHealth, pawn.health) || !ReferenceEquals(rootSet, pawn.health.hediffSet)
                || !ReferenceEquals(rootComponents, pawn.AllComps) || !ReferenceEquals(rootConditions, rootSet.hediffs)
                || !RootsMatch()) return false;
            foreach (var entry in structureTypes)
                if (!ReferenceEquals(entry.Value, LifeAnalysisCoordinator.Shared.Find(new LifeAnalysisKey(LifeAnalysisKind.Type, entry.Key))))
                    return false;
            foreach (Node node in nodes.Values)
            {
                if (node.CustomType == null) continue;
                if (!node.AttachmentsMatch(node.Value, node.Attachments)) return false;
            }
            return collections.Matches();
        }

        internal void RefreshBindings()
        {
            foreach (Node node in bindingNodes) LifeExecutionGuard.Register(node.Analysis.Sites, node.Value, pawn);
        }
    }
}
