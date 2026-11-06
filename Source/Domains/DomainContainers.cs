using System.Collections.Generic;
using Verse;
using LihuatLib.Domains;

namespace LihuatLib.Domains
{
    public static class DomainContainers
    {
        public static void Capture(DomainMember member)
        {
            IThingHolder target = member.Pawn.ParentHolder;
            if (target == null || member.Container == null) return;
            List<int> path = new List<int>();
            if (Find(member.Container as IThingHolder, target, path, new HashSet<IThingHolder>()))
            {
                member.ContainerPath = path;
                member.ContainerHolderType = target.GetType().AssemblyQualifiedName;
            }
        }
        private static bool Find(IThingHolder current, IThingHolder target, List<int> path, HashSet<IThingHolder> seen)
        {
            if (current == null || !seen.Add(current)) return false;
            if (current == target) return true;
            List<IThingHolder> children = new List<IThingHolder>(); current.GetChildHolders(children);
            for (int i = 0; i < children.Count; i++)
            {
                path.Add(i);
                if (Find(children[i], target, path, seen)) return true;
                path.RemoveAt(path.Count - 1);
            }
            return false;
        }
        public static ThingOwner Resolve(DomainMember member)
        {
            if (member.Container?.Destroyed != false || member.ContainerPath == null) return null;
            IThingHolder holder = member.Container as IThingHolder;
            foreach (int index in member.ContainerPath)
            {
                List<IThingHolder> children = new List<IThingHolder>(); holder.GetChildHolders(children);
                if (index < 0 || index >= children.Count) return null;
                holder = children[index];
            }
            return holder?.GetType().AssemblyQualifiedName == member.ContainerHolderType ? holder.GetDirectlyHeldThings() : null;
        }
    }
}
