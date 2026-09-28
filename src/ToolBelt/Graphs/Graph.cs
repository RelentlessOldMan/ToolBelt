// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;

namespace ToolBelt.Graphs
{
    /// <summary>A directed or undirected edge with a weight (default 1 for unweighted use).</summary>
    public readonly struct Edge<T>
    {
        public T From { get; }
        public T To { get; }
        public double Weight { get; }

        public Edge(T from, T to, double weight)
        {
            From = from;
            To = to;
            Weight = weight;
        }

        public override string ToString() => $"{From} -> {To} ({Weight})";
    }

    /// <summary>
    /// A light adjacency-list graph, directed or undirected and optionally weighted. Vertices are values
    /// of <typeparamref name="T"/> compared with the supplied (or default) equality comparer; adding an
    /// edge implicitly adds its endpoints. The graph algorithms in this namespace operate on this type;
    /// several also offer delegate-based overloads for callers with their own structure. Not thread-safe.
    /// </summary>
    public sealed class Graph<T> where T : notnull
    {
        private readonly Dictionary<T, List<(T To, double Weight)>> _adjacency;
        private readonly List<Edge<T>> _edges = new List<Edge<T>>();

        public Graph(bool directed = true, IEqualityComparer<T>? comparer = null)
        {
            IsDirected = directed;
            Comparer = comparer ?? EqualityComparer<T>.Default;
            _adjacency = new Dictionary<T, List<(T, double)>>(Comparer);
        }

        public bool IsDirected { get; }
        public IEqualityComparer<T> Comparer { get; }

        public int VertexCount => _adjacency.Count;
        public int EdgeCount => _edges.Count;
        public IEnumerable<T> Vertices => _adjacency.Keys;
        public IReadOnlyList<Edge<T>> Edges => _edges;

        /// <summary>Adds an isolated vertex. Returns false if it was already present.</summary>
        public bool AddVertex(T vertex)
        {
            if (vertex is null) throw new ArgumentNullException(nameof(vertex));
            if (_adjacency.ContainsKey(vertex)) return false;
            _adjacency[vertex] = new List<(T, double)>();
            return true;
        }

        /// <summary>Adds an edge (creating endpoints as needed). For undirected graphs both directions are traversable.</summary>
        public void AddEdge(T from, T to, double weight = 1.0)
        {
            AddVertex(from);
            AddVertex(to);
            _adjacency[from].Add((to, weight));
            if (!IsDirected && !Comparer.Equals(from, to)) // avoid double-listing a self loop
                _adjacency[to].Add((from, weight));
            _edges.Add(new Edge<T>(from, to, weight));
        }

        public bool ContainsVertex(T vertex) => _adjacency.ContainsKey(vertex);

        /// <summary>The outgoing neighbors of <paramref name="vertex"/> (both directions for undirected graphs).</summary>
        public IEnumerable<(T To, double Weight)> Neighbors(T vertex)
        {
            if (!_adjacency.TryGetValue(vertex, out var list))
                throw new KeyNotFoundException($"Vertex '{vertex}' is not in the graph.");
            return list;
        }

        public int OutDegree(T vertex)
        {
            if (!_adjacency.TryGetValue(vertex, out var list))
                throw new KeyNotFoundException($"Vertex '{vertex}' is not in the graph.");
            return list.Count;
        }

        /// <summary>Breadth-first vertex order from <paramref name="start"/> (start included).</summary>
        public IEnumerable<T> BreadthFirst(T start)
        {
            if (!_adjacency.ContainsKey(start))
                throw new KeyNotFoundException($"Vertex '{start}' is not in the graph.");

            var seen = new HashSet<T>(Comparer) { start };
            var queue = new Queue<T>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                T v = queue.Dequeue();
                yield return v;
                foreach (var (to, _) in _adjacency[v])
                    if (seen.Add(to))
                        queue.Enqueue(to);
            }
        }

        /// <summary>Depth-first vertex order from <paramref name="start"/> (start included, pre-order).</summary>
        public IEnumerable<T> DepthFirst(T start)
        {
            if (!_adjacency.ContainsKey(start))
                throw new KeyNotFoundException($"Vertex '{start}' is not in the graph.");

            var seen = new HashSet<T>(Comparer);
            var stack = new Stack<T>();
            stack.Push(start);
            while (stack.Count > 0)
            {
                T v = stack.Pop();
                if (!seen.Add(v)) continue;
                yield return v;
                // Push neighbors in reverse so the first listed neighbor is visited first.
                var list = _adjacency[v];
                for (int i = list.Count - 1; i >= 0; i--)
                    if (!seen.Contains(list[i].To))
                        stack.Push(list[i].To);
            }
        }
    }
}
