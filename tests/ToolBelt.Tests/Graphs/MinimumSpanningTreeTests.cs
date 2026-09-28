using System;
using System.Collections.Generic;
using System.Linq;
using ToolBelt.Graphs;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Graphs
{
    public sealed class MinimumSpanningTreeTests
    {
        public void KnownExample()
        {
            var g = new Graph<int>(directed: false);
            g.AddEdge(0, 1, 1);
            g.AddEdge(1, 2, 2);
            g.AddEdge(0, 2, 3); // heavier alternative, must be skipped
            g.AddEdge(2, 3, 4);

            var r = MinimumSpanningTree.Kruskal(g);
            Check.Close(7, r.TotalWeight, 1e-9);     // 1 + 2 + 4
            Check.Equal(3, r.Edges.Count);           // V - 1
            Check.True(r.IsSpanningTree);
            Check.Equal(1, r.ComponentCount);
        }

        public void DisconnectedGraphYieldsForest()
        {
            var g = new Graph<int>(directed: false);
            g.AddEdge(0, 1, 1);
            g.AddEdge(2, 3, 1);
            var r = MinimumSpanningTree.Kruskal(g);
            Check.Equal(2, r.ComponentCount);
            Check.False(r.IsSpanningTree);
            Check.Equal(2, r.Edges.Count);
        }

        public void DirectedGraph_Throws()
        {
            var g = new Graph<int>(directed: true);
            g.AddEdge(1, 2);
            Check.Throws<ArgumentException>(() => MinimumSpanningTree.Kruskal(g));
        }

        // Differential: on random connected graphs, Kruskal's total weight must equal an independent Prim's
        // implementation (MST weight is unique) and the chosen edges must form a spanning tree (V-1 edges).
        public void Property_MatchesPrim()
        {
            var rng = new Random(555);
            for (int trial = 0; trial < 500; trial++)
            {
                int n = rng.Next(2, 12);
                var g = new Graph<int>(directed: false);
                for (int i = 0; i < n; i++) g.AddVertex(i);

                // Random spanning tree first (guarantees connectivity), then extra random edges.
                var weight = new Dictionary<(int, int), double>();
                for (int i = 1; i < n; i++)
                {
                    int parent = rng.Next(i);
                    double w = rng.Next(1, 50);
                    g.AddEdge(parent, i, w);
                    weight[Key(parent, i)] = Math.Min(w, weight.TryGetValue(Key(parent, i), out var e) ? e : double.MaxValue);
                }
                int extra = rng.Next(0, n);
                for (int k = 0; k < extra; k++)
                {
                    int a = rng.Next(n), b = rng.Next(n);
                    if (a == b) continue;
                    double w = rng.Next(1, 50);
                    g.AddEdge(a, b, w);
                    var key = Key(a, b);
                    weight[key] = Math.Min(w, weight.TryGetValue(key, out var e) ? e : double.MaxValue);
                }

                var r = MinimumSpanningTree.Kruskal(g);
                Check.Equal(1, r.ComponentCount, $"trial {trial}: should be connected");
                Check.Equal(n - 1, r.Edges.Count, $"trial {trial}: spanning tree edge count");
                Check.Close(Prim(n, weight), r.TotalWeight, 1e-9, $"trial {trial}: MST weight");
            }
        }

        private static (int, int) Key(int a, int b) => a < b ? (a, b) : (b, a);

        // Independent reference: Prim's algorithm over the undirected weight map.
        private static double Prim(int n, Dictionary<(int, int), double> weight)
        {
            var inTree = new bool[n];
            inTree[0] = true;
            double total = 0;
            for (int added = 1; added < n; added++)
            {
                double best = double.PositiveInfinity;
                int bestV = -1;
                for (int u = 0; u < n; u++)
                {
                    if (!inTree[u]) continue;
                    for (int v = 0; v < n; v++)
                    {
                        if (inTree[v]) continue;
                        if (weight.TryGetValue(Key(u, v), out double w) && w < best)
                        {
                            best = w;
                            bestV = v;
                        }
                    }
                }
                inTree[bestV] = true;
                total += best;
            }
            return total;
        }
    }
}
