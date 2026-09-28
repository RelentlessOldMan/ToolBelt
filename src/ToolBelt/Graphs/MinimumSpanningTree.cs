// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;
using ToolBelt.Collections;

namespace ToolBelt.Graphs
{
    /// <summary>The result of a minimum-spanning-tree/forest computation.</summary>
    public sealed class MinimumSpanningTreeResult<T>
    {
        internal MinimumSpanningTreeResult(IReadOnlyList<Edge<T>> edges, double totalWeight, int componentCount)
        {
            Edges = edges;
            TotalWeight = totalWeight;
            ComponentCount = componentCount;
        }

        /// <summary>The chosen edges, in ascending weight order.</summary>
        public IReadOnlyList<Edge<T>> Edges { get; }

        /// <summary>Sum of the chosen edge weights.</summary>
        public double TotalWeight { get; }

        /// <summary>Number of connected components spanned (1 means the result is a single tree).</summary>
        public int ComponentCount { get; }

        /// <summary>True if the graph is connected, so the result spans every vertex as one tree.</summary>
        public bool IsSpanningTree => ComponentCount <= 1;
    }

    /// <summary>
    /// Minimum spanning tree (or forest, if the graph is disconnected) of an undirected graph by Kruskal's
    /// algorithm — a weight sort plus the shared <see cref="DisjointSet"/> union-find. Negative weights are
    /// allowed. Directed graphs are rejected.
    /// </summary>
    public static class MinimumSpanningTree
    {
        public static MinimumSpanningTreeResult<T> Kruskal<T>(Graph<T> graph) where T : notnull
        {
            if (graph is null) throw new ArgumentNullException(nameof(graph));
            if (graph.IsDirected)
                throw new ArgumentException("Minimum spanning tree requires an undirected graph.", nameof(graph));

            // Map vertices to dense indices for the union-find.
            var index = new Dictionary<T, int>(graph.Comparer);
            foreach (T v in graph.Vertices)
                index[v] = index.Count;

            var sorted = new List<Edge<T>>(graph.Edges);
            sorted.Sort((a, b) => a.Weight.CompareTo(b.Weight));

            var forest = new DisjointSet(index.Count);
            var chosen = new List<Edge<T>>();
            double total = 0.0;

            foreach (Edge<T> e in sorted)
            {
                if (forest.Union(index[e.From], index[e.To]))
                {
                    chosen.Add(e);
                    total += e.Weight;
                }
            }

            // SetCount counts remaining disjoint sets; that is the number of connected components.
            int components = index.Count == 0 ? 0 : forest.SetCount;
            return new MinimumSpanningTreeResult<T>(chosen, total, components);
        }
    }
}
