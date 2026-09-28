// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;

namespace ToolBelt.Graphs
{
    /// <summary>
    /// Finds the strongly connected components of a directed graph (maximal sets where every vertex
    /// reaches every other) using Tarjan's algorithm. This surfaces the whole dependency knot rather than
    /// a single cycle. The implementation is iterative, so it is safe on deep graphs. Components are
    /// returned in reverse topological order of the condensation.
    /// </summary>
    public static class StronglyConnectedComponents
    {
        public static IReadOnlyList<IReadOnlyList<T>> Find<T>(Graph<T> graph) where T : notnull
        {
            if (graph is null) throw new ArgumentNullException(nameof(graph));
            if (!graph.IsDirected)
                throw new ArgumentException("Strongly connected components require a directed graph.", nameof(graph));

            var index = new Dictionary<T, int>(graph.Comparer);
            var lowLink = new Dictionary<T, int>(graph.Comparer);
            var onStack = new HashSet<T>(graph.Comparer);
            var tarjanStack = new Stack<T>();
            var components = new List<IReadOnlyList<T>>();
            int counter = 0;

            // Explicit DFS stack of (vertex, its neighbor enumerator) frames.
            var work = new Stack<(T Node, IEnumerator<(T To, double Weight)> It)>();

            foreach (T start in graph.Vertices)
            {
                if (index.ContainsKey(start)) continue;

                Open(start);
                while (work.Count > 0)
                {
                    var frame = work.Peek();
                    if (frame.It.MoveNext())
                    {
                        T w = frame.It.Current.To;
                        if (!index.ContainsKey(w))
                        {
                            Open(w); // recurse into w
                        }
                        else if (onStack.Contains(w))
                        {
                            // Back/cross edge to a vertex still on the stack.
                            if (index[w] < lowLink[frame.Node])
                                lowLink[frame.Node] = index[w];
                        }
                    }
                    else
                    {
                        // Finished this vertex: if it is a root, pop its component.
                        work.Pop();
                        T v = frame.Node;
                        if (lowLink[v] == index[v])
                        {
                            var component = new List<T>();
                            T popped;
                            do
                            {
                                popped = tarjanStack.Pop();
                                onStack.Remove(popped);
                                component.Add(popped);
                            }
                            while (!graph.Comparer.Equals(popped, v));
                            components.Add(component);
                        }

                        // Propagate low-link up to the parent frame.
                        if (work.Count > 0)
                        {
                            T parent = work.Peek().Node;
                            if (lowLink[v] < lowLink[parent])
                                lowLink[parent] = lowLink[v];
                        }
                    }
                }
            }

            return components;

            void Open(T v)
            {
                index[v] = counter;
                lowLink[v] = counter;
                counter++;
                onStack.Add(v);
                tarjanStack.Push(v);
                work.Push((v, graph.Neighbors(v).GetEnumerator()));
            }
        }
    }
}
