using System.Collections.Generic;
using Verse;

namespace LihuatLib.Core
{
    internal sealed class RegenerationChain : IExposable
    {
        internal string Root;
        internal HashSet<string> Bodies = new HashSet<string>();
        internal int Activations;
        public void ExposeData()
        {
            Scribe_Values.Look(ref Root, "root");
            Scribe_Collections.Look(ref Bodies, "bodies", LookMode.Value);
            Scribe_Values.Look(ref Activations, "activations");
            if (Bodies == null) Bodies = new HashSet<string>();
        }
    }
    internal sealed class RegenerationHistory : IExposable
    {
        private List<RegenerationChain> chains = new List<RegenerationChain>();
        private readonly Dictionary<string, RegenerationChain> byRoot = new Dictionary<string, RegenerationChain>();
        private readonly Dictionary<string, string> byBody = new Dictionary<string, string>();
        internal string RootForBody(string body) => body != null && byBody.TryGetValue(body, out string root) ? root : null;
        internal int Count(string root) => root != null && byRoot.TryGetValue(root, out RegenerationChain chain)
            ? System.Math.Max(chain.Activations, chain.Bodies.Count) : 0;
        internal void Record(string root, Pawn pawn)
        {
            if (!byRoot.TryGetValue(root, out RegenerationChain chain))
            { chain = new RegenerationChain { Root = root }; byRoot.Add(root, chain); chains.Add(chain); }
            int previous = System.Math.Max(chain.Activations, chain.Bodies.Count);
            chain.Activations = previous == int.MaxValue ? previous : previous + 1;
            chain.Bodies.Add(pawn.ThingID);
            byBody[pawn.ThingID] = root;
        }
        public void ExposeData()
        {
            Scribe_Collections.Look(ref chains, "regenerationHistory", LookMode.Deep);
            if (chains == null) chains = new List<RegenerationChain>();
            if (Scribe.mode != LoadSaveMode.PostLoadInit) return;
            byRoot.Clear(); byBody.Clear();
            foreach (RegenerationChain chain in chains)
                if (!string.IsNullOrEmpty(chain.Root))
                {
                    byRoot[chain.Root] = chain;
                    foreach (string body in chain.Bodies) if (!string.IsNullOrEmpty(body)) byBody[body] = chain.Root;
                }
        }
    }
}
