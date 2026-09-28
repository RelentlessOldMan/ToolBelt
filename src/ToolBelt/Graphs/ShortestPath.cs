// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;
using ToolBelt.Collections;

namespace ToolBelt.Graphs
{
    /// <summary>A shortest-path query result: whether a path exists, the path itself, and its total cost.</summary>
    public sealed class ShortestPathResult<T>
    {
        internal ShortestPathResult(bool found, IReadOnlyList<T> path, double distance)
        {
            Found = found;
            Path = path;
            Distance = distance;
        }

        /// <summary>True if the target is reachable from the source.</summary>
        public bool Found { get; }

        /// <summary>The path from source to target inclusive (empty if unreachable).</summary>
        public IReadOnlyList<T> Path { get; }

        /// <summary>Total cost of the path; <see cref="double.PositiveInfinity"/> if unreachable.</summary>
        public double Distance { get; }
    }

    /// <summary>
    /// Shortest-path search: breadth-first for unweighted graphs (fewest edges) and Dijkstra for
    /// non-negative weighted graphs (least total weight, using the shared <see cref="BinaryHeap{T}"/> as
    /// its priority queue). Negative edge weights are rejected — Dijkstra is invalid for them.
    /// </summary>
    public static class ShortestPath
    {
        /// <summary>Fewest-edges path, treating every edge as unit cost.</summary>
        public static ShortestPathResult<T> BreadthFirst<T>(Graph<T> graph, T source, T target) where T : notnull
        {
            if (graph is null) throw new ArgumentNullException(nameof(graph));
            if (!graph.ContainsVertex(source)) throw new KeyNotFoundException($"Source '{source}' is not in the graph.");
            if (!graph.ContainsVertex(target)) throw new KeyNotFoundException($"Target '{target}' is not in the graph.");

            var cmp = graph.Comparer;
            var prev = new Dictionary<T, T>(cmp);
            var seen = new HashSet<T>(cmp) { source };
            var queue = new Queue<T>();
            queue.Enqueue(source);

            while (queue.Count > 0)
            {
                T v = queue.Dequeue();
                if (cmp.Equals(v, target))
                    return Build(source, target, prev, cmp, HopCount(source, target, prev, cmp));
                foreach (var (to, _) in graph.Neighbors(v))
                {
                    if (seen.Add(to))
                    {
                        prev[to] = v;
                        queue.Enqueue(to);
                    }
                }
            }
            return new ShortestPathResult<T>(false, Array.Empty<T>(), double.PositiveInfinity);
        }

        /// <summary>Least-total-weight path over non-negative weights.</summary>
        public static ShortestPathResult<T> Dijkstra<T>(Graph<T> graph, T source, T target) where T : notnull
        {
            if (graph is null) throw new ArgumentNullException(nameof(graph));
            if (!graph.ContainsVertex(source)) throw new KeyNotFoundException($"Source '{source}' is not in the graph.");
            if (!graph.ContainsVertex(target)) throw new KeyNotFoundException($"Target '{target}' is not in the graph.");

            var cmp = graph.Comparer;
            var dist = new Dictionary<T, double>(cmp) { [source] = 0.0 };
            var prev = new Dictionary<T, T>(cmp);
            var heap = new BinaryHeap<Entry<T>>(Comparer<Entry<T>>.Create((a, b) => a.Distance.CompareTo(b.Distance)));
            heap.Push(new Entry<T>(source, 0.0));

            while (heap.TryPop(out var entry))
            {
                T u = entry.Node;
                // Skip stale heap entries (lazy decrease-key).
                if (entry.Distance > dist[u]) continue;
                if (cmp.Equals(u, target))
                    return Build(source, target, prev, cmp, dist[target]);

                foreach (var (to, weight) in graph.Neighbors(u))
                {
                    if (weight < 0)
                        throw new InvalidOperationException("Dijkstra does not support negative edge weights.");
                    double nd = entry.Distance + weight;
                    if (!dist.TryGetValue(to, out double old) || nd < old)
                    {
                        dist[to] = nd;
                        prev[to] = u;
                        heap.Push(new Entry<T>(to, nd));
                    }
                }
            }
            return new ShortestPathResult<T>(false, Array.Empty<T>(), double.PositiveInfinity);
        }

        private readonly struct Entry<T>
        {
            public readonly T Node;
            public readonly double Distance;
            public Entry(T node, double distance) { Node = node; Distance = distance; }
        }

        private static ShortestPathResult<T> Build<T>(
            T source, T target, Dictionary<T, T> prev, IEqualityComparer<T> cmp, double distance) where T : notnull
        {
            var path = new List<T>();
            T current = target;
            path.Add(current);
            while (!cmp.Equals(current, source))
            {
                current = prev[current];
                path.Add(current);
            }
            path.Reverse();
            return new ShortestPathResult<T>(true, path, distance);
        }

        private static double HopCount<T>(T source, T target, Dictionary<T, T> prev, IEqualityComparer<T> cmp) where T : notnull
        {
            double hops = 0;
            T current = target;
            while (!cmp.Equals(current, source))
            {
                current = prev[current];
                hops++;
            }
            return hops;
        }
    }
}
