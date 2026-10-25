// ToolBelt drop-in — also copy Graphs/Graph.cs.
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
        // current DFS path from that node to reconstruct the cycle. The DFS keeps its own stack of
        // (vertex, neighbor enumerator), so a long chain cannot overflow the call stack.
        private static IReadOnlyList<T> FindCycle<T>(Graph<T> graph) where T : notnull
        {
            const int White = 0, Gray = 1, Black = 2;
            var color = new Dictionary<T, int>(graph.Comparer);
            foreach (T v in graph.Vertices) color[v] = White;

            var path = new List<T>();
            var position = new Dictionary<T, int>(graph.Comparer);   // index in path of each gray vertex
            var stack = new Stack<IEnumerator<(T To, double Weight)>>();

            foreach (T start in graph.Vertices)
            {
                if (color[start] != White) continue;
                Enter(start);
                while (stack.Count > 0)
                {
                    var neighbors = stack.Peek();
                    if (!neighbors.MoveNext())
                    {
                        stack.Pop();
                        T done = path[path.Count - 1];
                        color[done] = Black;
                        position.Remove(done);
                        path.RemoveAt(path.Count - 1);
                        continue;
                    }
                    T w = neighbors.Current.To;
                    if (color[w] == White) Enter(w);
                    else if (color[w] == Gray)
                    {
                        int from = position[w];
                        return path.GetRange(from, path.Count - from);
                    }
                }
            }
            return Array.Empty<T>();

            void Enter(T u)
            {
                color[u] = Gray;
                position[u] = path.Count;
                path.Add(u);
                stack.Push(graph.Neighbors(u).GetEnumerator());
            }
        }
    }
}
