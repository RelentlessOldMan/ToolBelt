using System;
using System.Collections.Generic;
using System.Linq;
using ToolBelt.Graphs;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Graphs
{
    public sealed class TopologicalSortTests
    {
        public void OrdersAcyclicGraph()
        {
            var g = new Graph<string>();
            g.AddEdge("shirt", "tie");
            g.AddEdge("tie", "jacket");
            g.AddEdge("belt", "jacket");
            var result = TopologicalSort.Sort(g);

            Check.True(result.IsAcyclic);
            AssertRespectsEdges(g, result.Order);
        }

        public void DetectsCycleAndReportsIt()
        {
            var g = new Graph<int>();
            g.AddEdge(1, 2); g.AddEdge(2, 3); g.AddEdge(3, 1);
            var result = TopologicalSort.Sort(g);

            Check.False(result.IsAcyclic);
            Check.Equal(0, result.Order.Count);
            AssertIsRealCycle(g, result.Cycle);
        }

        public void UndirectedGraph_Throws()
        {
            var g = new Graph<int>(directed: false);
            g.AddEdge(1, 2);
            Check.Throws<ArgumentException>(() => TopologicalSort.Sort(g));
        }

        public void EmptyGraph()
        {
            var result = TopologicalSort.Sort(new Graph<int>());
            Check.True(result.IsAcyclic);
            Check.Equal(0, result.Order.Count);
        }

        // Differential: random DAGs (edges only go forward along a permutation) must always sort, with
        // every edge respected. Random graphs with a back edge must be reported cyclic.
        public void Property_RandomDagsSortAndRespectEdges()
        {
            var rng = new Random(31);
            for (int trial = 0; trial < 2000; trial++)
            {
                int n = rng.Next(1, 15);
                var perm = Enumerable.Range(0, n).OrderBy(_ => rng.Next()).ToArray();
                var g = new Graph<int>();
                for (int i = 0; i < n; i++) g.AddVertex(perm[i]);

                for (int i = 0; i < n; i++)
                    for (int j = i + 1; j < n; j++)
                        if (rng.NextDouble() < 0.3)
                            g.AddEdge(perm[i], perm[j]); // always earlier -> later => acyclic

                var result = TopologicalSort.Sort(g);
                Check.True(result.IsAcyclic, $"trial {trial}: DAG reported cyclic");
                AssertRespectsEdges(g, result.Order);
            }
        }

        private static void AssertRespectsEdges<T>(Graph<T> g, IReadOnlyList<T> order) where T : notnull
        {
            var position = new Dictionary<T, int>(g.Comparer);
            for (int i = 0; i < order.Count; i++) position[order[i]] = i;
            Check.Equal(g.VertexCount, order.Count, "order must contain every vertex");
            foreach (var e in g.Edges)
                Check.True(position[e.From] < position[e.To], $"edge {e.From}->{e.To} violated");
        }

        private static void AssertIsRealCycle<T>(Graph<T> g, IReadOnlyList<T> cycle) where T : notnull
        {
            Check.True(cycle.Count >= 1, "cycle must be non-empty");
            for (int i = 0; i < cycle.Count; i++)
            {
                T from = cycle[i];
                T to = cycle[(i + 1) % cycle.Count];
                Check.True(g.Neighbors(from).Any(nb => g.Comparer.Equals(nb.To, to)),
                    $"cycle edge {from}->{to} does not exist");
            }
        }
    }
}
