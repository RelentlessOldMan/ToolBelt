// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;

namespace ToolBelt.Graphs
{
    /// <summary>The outcome of a topological sort: either a valid order, or the cycle that prevented one.</summary>
    public sealed class TopologicalSortResult<T>
    {
        internal TopologicalSortResult(bool isAcyclic, IReadOnlyList<T> order, IReadOnlyList<T> cycle)
        {
            IsAcyclic = isAcyclic;
            Order = order;
            Cycle = cycle;
        }

        /// <summary>True if the graph is a DAG and <see cref="Order"/> is valid.</summary>
        public bool IsAcyclic { get; }

        /// <summary>A valid topological order when acyclic; empty otherwise.</summary>
        public IReadOnlyList<T> Order { get; }

        /// <summary>When cyclic, one detected cycle as consecutive vertices (the last links back to the first); empty otherwise.</summary>
        public IReadOnlyList<T> Cycle { get; }
    }

    /// <summary>
    /// Topological ordering of a directed graph by Kahn's algorithm. When the graph contains a cycle no
    /// order exists, so the result reports the actual offending cycle — which is what makes a dependency
    /// failure actionable rather than merely detected.
    /// </summary>
    public static class TopologicalSort
    {
        public static TopologicalSortResult<T> Sort<T>(Graph<T> graph) where T : notnull
        {
            if (graph is null) throw new ArgumentNullException(nameof(graph));
            if (!graph.IsDirected)
                throw new ArgumentException("Topological sort requires a directed graph.", nameof(graph));

            var inDegree = new Dictionary<T, int>(graph.Comparer);
            foreach (T v in graph.Vertices)
                inDegree[v] = 0;
            foreach (T v in graph.Vertices)
                foreach (var (to, _) in graph.Neighbors(v))
                    inDegree[to] = inDegree[to] + 1;

            var ready = new Queue<T>();
            foreach (var kv in inDegree)
                if (kv.Value == 0)
                    ready.Enqueue(kv.Key);

            var order = new List<T>(graph.VertexCount);
            while (ready.Count > 0)
            {
                T v = ready.Dequeue();
                order.Add(v);
                foreach (var (to, _) in graph.Neighbors(v))
                {
                    int d = inDegree[to] - 1;
                    inDegree[to] = d;
                    if (d == 0) ready.Enqueue(to);
                }
            }

            if (order.Count == graph.VertexCount)
                return new TopologicalSortResult<T>(true, order, Array.Empty<T>());

            // Remaining vertices (in-degree never reached zero) contain at least one cycle; extract one.
            var cycle = FindCycle(graph);
            return new TopologicalSortResult<T>(false, Array.Empty<T>(), cycle);
        }

        // DFS with white/gray/black coloring; when a back edge to a gray node is found, slice the
        // current DFS path from that node to reconstruct the cycle.
        private static IReadOnlyList<T> FindCycle<T>(Graph<T> graph) where T : notnull
        {
            const int White = 0, Gray = 1, Black = 2;
            var color = new Dictionary<T, int>(graph.Comparer);
            foreach (T v in graph.Vertices) color[v] = White;

            var path = new List<T>();

            foreach (T start in graph.Vertices)
            {
                if (color[start] != White) continue;
                var found = Visit(start);
                if (found != null) return found;
            }
            return Array.Empty<T>();

            List<T>? Visit(T u)
            {
                color[u] = Gray;
                path.Add(u);

                foreach (var (w, _) in graph.Neighbors(u))
                {
                    if (color[w] == White)
                    {
                        var found = Visit(w);
                        if (found != null) return found;
                    }
                    else if (color[w] == Gray)
                    {
                        int from = path.IndexOf(w);
                        return path.GetRange(from, path.Count - from);
                    }
                }

                color[u] = Black;
                path.RemoveAt(path.Count - 1);
                return null;
            }
        }
    }
}
