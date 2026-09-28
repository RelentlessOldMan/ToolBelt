using System;
using System.Collections.Generic;
using System.Linq;
using ToolBelt.Graphs;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Graphs
{
    public sealed class StronglyConnectedComponentsTests
    {
        public void KnownExample()
        {
            // Two cycles {1,2,3} and {4,5} joined by a one-way edge 3->4.
            var g = new Graph<int>();
            g.AddEdge(1, 2); g.AddEdge(2, 3); g.AddEdge(3, 1);
            g.AddEdge(3, 4);
            g.AddEdge(4, 5); g.AddEdge(5, 4);

            var comps = Normalize(StronglyConnectedComponents.Find(g));
            Check.Equal(2, comps.Count);
            Check.True(comps.Contains("1,2,3"));
            Check.True(comps.Contains("4,5"));
        }

        public void EachVertexOwnComponentWhenAcyclic()
        {
            var g = new Graph<int>();
            g.AddEdge(1, 2); g.AddEdge(2, 3);
            Check.Equal(3, StronglyConnectedComponents.Find(g).Count);
        }

        public void UndirectedGraph_Throws()
        {
            var g = new Graph<int>(directed: false);
            g.AddEdge(1, 2);
            Check.Throws<ArgumentException>(() => StronglyConnectedComponents.Find(g));
        }

        // Differential: Tarjan must agree with the brute-force definition — u,v share a component iff each
        // is reachable from the other.
        public void Property_MatchesReachabilityDefinition()
        {
            var rng = new Random(123);
            for (int trial = 0; trial < 500; trial++)
            {
                int n = rng.Next(1, 12);
                var g = new Graph<int>();
                for (int i = 0; i < n; i++) g.AddVertex(i);
                for (int i = 0; i < n; i++)
                    for (int j = 0; j < n; j++)
                        if (i != j && rng.NextDouble() < 0.25)
                            g.AddEdge(i, j);

                // Component id per vertex from Tarjan.
                var tarjan = StronglyConnectedComponents.Find(g);
                var compId = new Dictionary<int, int>();
                for (int c = 0; c < tarjan.Count; c++)
                    foreach (int v in tarjan[c]) compId[v] = c;

                // Every vertex must appear exactly once.
                Check.Equal(n, compId.Count, $"trial {trial}: vertex coverage");

                for (int u = 0; u < n; u++)
                    for (int v = 0; v < n; v++)
                    {
                        bool mutual = Reaches(g, u, v, n) && Reaches(g, v, u, n);
                        bool sameComp = compId[u] == compId[v];
                        Check.Equal(mutual, sameComp, $"trial {trial}: pair ({u},{v})");
                    }
            }
        }

        private static bool Reaches(Graph<int> g, int from, int to, int n)
        {
            if (from == to) return true;
            var seen = new HashSet<int> { from };
            var stack = new Stack<int>();
            stack.Push(from);
            while (stack.Count > 0)
            {
                int v = stack.Pop();
                foreach (var (w, _) in g.Neighbors(v))
                {
                    if (w == to) return true;
                    if (seen.Add(w)) stack.Push(w);
                }
            }
            return false;
        }

        private static List<string> Normalize(IReadOnlyList<IReadOnlyList<int>> comps)
            => comps.Select(c => string.Join(",", c.OrderBy(x => x))).ToList();
    }
}
