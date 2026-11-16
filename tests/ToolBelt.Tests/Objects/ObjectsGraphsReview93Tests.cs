using System;
using System.Collections.Generic;
using ToolBelt.Graphs;
using ToolBelt.Objects;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Objects
{
    // Round 93 (objects, functional, grids, graphs, intervals, sixth pass): each failed on the code before its fix.
    public sealed class ObjectsGraphsReview93Tests
    {
#if NET8_0_OR_GREATER
        // The key budget was counted when a child was walked, after every child's full path had been built: one long
        // key above a big array built all its children's keys (4 GB here) before the limit was noticed.
        public void Flatten_ALongPathAboveABigArrayStopsAtTheKeyBudget()
        {
            var root = new Dictionary<string, object?> { [new string('k', 2000)] = new object?[1_000_000] };
            long before = GC.GetAllocatedBytesForCurrentThread();
            Check.Throws<InvalidOperationException>(() => FlattenObject.Flatten(root));
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Check.True(allocated < 2L << 30, $"allocated {allocated:N0} bytes before the key budget threw");
        }
#endif

        public void Flatten_KeysJustUnderTheBudgetStillFlatten()
        {
            var root = new Dictionary<string, object?> { ["a"] = new object?[] { 1, 2, 3 }, ["b"] = new { C = 4 } };
            IDictionary<string, object?> flat = FlattenObject.Flatten(root);
            Check.Equal(4, flat.Count);
            Check.Equal((object?)2, flat["[a][1]"]);
            Check.Equal((object?)4, flat["[b].C"]);
        }

        // Edges and Neighbors returned the graph's own lists: cast back and changed, they put Edges, EdgeCount and the
        // adjacency lists out of step.
        public void Graph_EdgesAndNeighborsCannotBeChangedThroughACast()
        {
            var g = new Graph<int>();
            g.AddEdge(1, 2);
            Check.False(g.Edges is List<Edge<int>>, "Edges is the internal list");
            Check.False(g.Neighbors(1) is List<(int, double)>, "Neighbors is the internal list");
            Check.True(((ICollection<Edge<int>>)g.Edges).IsReadOnly);
            Check.True(((ICollection<(int, double)>)g.Neighbors(1)).IsReadOnly);
            Check.Throws<NotSupportedException>(() => ((ICollection<(int, double)>)g.Neighbors(1)).Add((3, 1.0)));
            Check.Equal(1, g.EdgeCount);
            Check.Equal(1, g.OutDegree(1));
        }

        public void Graph_ViewsFollowLaterEdges()
        {
            var g = new Graph<int>(directed: false);
            IReadOnlyList<Edge<int>> edges = g.Edges;
            g.AddVertex(1);
            IEnumerable<(int To, double Weight)> n = g.Neighbors(1);
            g.AddEdge(1, 2, 2.5);
            g.AddEdge(3, 1);
            Check.Equal(2, edges.Count);
            var seen = new List<int>();
            foreach (var (to, _) in n) seen.Add(to);
            Check.Equal("2,3", string.Join(",", seen));
        }

        // Guards for the documented behaviour the reviewers queried.
        public void DeepEquals_ToleranceIsForTwoDoublesOrTwoFloats()
        {
            var opt = new ObjectComparisonOptions { FloatTolerance = 0.1 };
            Check.True(DeepEquals.Equals(1.0, 1.05, opt));
            Check.True(DeepEquals.Equals(1.0f, 1.05f, opt));
            Check.False(DeepEquals.Equals(1.0, 1.0f, opt));
            Check.False(DeepEquals.Equals(1.0m, 1.05m, opt));
        }

        public void Unflatten_ARootListComesBackAsADictionary()
        {
            Dictionary<string, object?> back = UnflattenObject.Unflatten(FlattenObject.Flatten(new List<int> { 7, 8 }));
            Check.Equal(2, back.Count);
            Check.Equal((object?)7, back["0"]);
            Check.Equal((object?)8, back["1"]);
        }
    }
}
