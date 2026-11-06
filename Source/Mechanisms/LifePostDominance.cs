using System;
using System.Collections.Generic;
using System.Linq;

namespace LihuatLib.Mechanisms
{
    internal static class LifePostDominance
    {
        internal static int[] Joins(int[][] next)
        {
            int exit = next.Length;
            var reverse = new List<int>[exit + 1];
            for (int i = 0; i <= exit; i++) reverse[i] = new List<int>();
            for (int i = 0; i < exit; i++) foreach (int target in next[i]) reverse[target].Add(i);
            int[] order = ReversePostOrder(reverse, exit);
            var rank = new int[exit + 1];
            var parent = Enumerable.Repeat(-1, exit + 1).ToArray();
            for (int i = 0; i < order.Length; i++) rank[order[i]] = i;
            parent[exit] = exit;
            bool changed;
            do
            {
                changed = false;
                for (int i = 1; i < order.Length; i++)
                {
                    int node = order[i], common = -1;
                    foreach (int successor in next[node])
                    {
                        if (parent[successor] < 0) continue;
                        common = common < 0 ? successor : Intersect(common, successor, parent, rank);
                    }
                    if (parent[node] == common) continue;
                    parent[node] = common; changed = true;
                }
            } while (changed);
            var joins = new int[exit];
            for (int i = 0; i < exit; i++) joins[i] = parent[i] < 0 ? exit : parent[i];
            return joins;
        }

        private static int Intersect(int left, int right, int[] parent, int[] rank)
        {
            while (left != right)
            {
                while (rank[left] > rank[right]) left = parent[left];
                while (rank[right] > rank[left]) right = parent[right];
            }
            return left;
        }

        private static int[] ReversePostOrder(List<int>[] graph, int root)
        {
            var visited = new bool[graph.Length];
            var cursor = new int[graph.Length];
            var stack = new Stack<int>();
            var order = new List<int>();
            visited[root] = true; stack.Push(root);
            while (stack.Count > 0)
            {
                int node = stack.Peek();
                if (cursor[node] == graph[node].Count) { order.Add(stack.Pop()); continue; }
                int child = graph[node][cursor[node]++];
                if (visited[child]) continue;
                visited[child] = true; stack.Push(child);
            }
            order.Reverse();
            return order.ToArray();
        }
    }
}
