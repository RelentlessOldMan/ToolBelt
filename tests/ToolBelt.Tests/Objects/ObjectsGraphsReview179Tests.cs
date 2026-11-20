using System;
using System.Collections.Generic;
using ToolBelt.Graphs;
using ToolBelt.Objects;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Objects
{
    // Round 179 (objects, functional, grids, graphs, intervals, eleventh pass): each failed on the code before its fix.
    public sealed class ObjectsGraphsReview179Tests
    {
        // Random graphs with some +inf edges: BreadthFirst's hop count matches Dijkstra run on the same graph with every
        // finite weight set to 1 (and the +inf ones kept), so an infinite edge is no edge for both.
        public void BreadthFirst_InfiniteEdgesAreNoEdges()
        {
            var rng = new Random(179);
            int infTaken = 0;
            for (int trial = 0; trial < 400; trial++)
            {
                bool directed = rng.Next(2) == 0;
                int n = 2 + rng.Next(7);
                var g = new Graph<int>(directed);
                var unit = new Graph<int>(directed);
                for (int v = 0; v < n; v++) { g.AddVertex(v); unit.AddVertex(v); }
                int edges = rng.Next(2 * n);
                for (int e = 0; e < edges; e++)
                {
                    int a = rng.Next(n), b = rng.Next(n);
                    bool inf = rng.Next(3) == 0;
                    g.AddEdge(a, b, inf ? double.PositiveInfinity : 1 + rng.Next(5));
                    unit.AddEdge(a, b, inf ? double.PositiveInfinity : 1);
                }
                int s = rng.Next(n), t = rng.Next(n);
                var bfs = ShortestPath.BreadthFirst(g, s, t);
                var reference = ShortestPath.Dijkstra(unit, s, t);
                Check.Equal(reference.Found, bfs.Found);
                Check.Equal(reference.Distance, bfs.Distance);
                Check.Equal(bfs.Found ? (int)bfs.Distance + 1 : 0, bfs.Path.Count);
                if (!reference.Found && ShortestPath.BreadthFirst(Finite(g, n, directed), s, t).Found) infTaken++;
            }
            Check.True(infTaken > 0); // some trials are reachable only through an infinite edge

            var single = new Graph<string>(directed: true);
            single.AddEdge("a", "b", double.PositiveInfinity);
            var r = ShortestPath.BreadthFirst(single, "a", "b");
            Check.True(!r.Found);
            Check.Equal(double.PositiveInfinity, r.Distance);
            Check.Equal(0, r.Path.Count);
        }

        // g with every edge, infinite ones included, at weight 1.
        private static Graph<int> Finite(Graph<int> g, int n, bool directed)
        {
            var all = new Graph<int>(directed);
            for (int v = 0; v < n; v++) all.AddVertex(v);
            foreach (var e in g.Edges) all.AddEdge(e.From, e.To, 1);
            return all;
        }

        public void ExpressionAccessor_VoidType_ThrowsArgumentExceptionForType()
        {
            var types = new List<Type> { typeof(void), typeof(int).MakePointerType(), typeof(int).MakeByRefType(), typeof(List<>) };
            foreach (Type type in types)
            {
                var ex = Check.Throws<ArgumentException>(() => ExpressionAccessor.Getter(type, "X"));
                Check.Equal("type", ex.ParamName);
                Check.True(!(ex is ArgumentNullException));
                ex = Check.Throws<ArgumentException>(() => ExpressionAccessor.Setter(type, "X"));
                Check.Equal("type", ex.ParamName);
            }
        }
    }
}
