using System;
using System.Collections.Generic;
using System.Linq;
using ToolBelt.Graphs;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Graphs
{
    public sealed class GraphTests
    {
        public void DirectedEdgesAreOneWay()
        {
            var g = new Graph<int>(directed: true);
            g.AddEdge(1, 2);
            Check.True(g.Neighbors(1).Any(n => n.To == 2));
            Check.False(g.Neighbors(2).Any(n => n.To == 1));
            Check.Equal(2, g.VertexCount);
            Check.Equal(1, g.EdgeCount);
        }

        public void UndirectedEdgesAreSymmetric()
        {
            var g = new Graph<int>(directed: false);
            g.AddEdge(1, 2, 3.5);
            Check.True(g.Neighbors(1).Any(n => n.To == 2 && n.Weight == 3.5));
            Check.True(g.Neighbors(2).Any(n => n.To == 1 && n.Weight == 3.5));
            Check.Equal(1, g.EdgeCount); // recorded once
        }

        public void UndirectedSelfLoopListedOnce()
        {
            var g = new Graph<int>(directed: false);
            g.AddEdge(1, 1);
            Check.Equal(1, g.Neighbors(1).Count());
            Check.Equal(1, g.EdgeCount);
        }

        public void AddVertexDedupes()
        {
            var g = new Graph<int>();
            Check.True(g.AddVertex(1));
            Check.False(g.AddVertex(1));
        }

        public void NeighborsOfMissingVertex_Throws()
        {
            var g = new Graph<int>();
            Check.Throws<KeyNotFoundException>(() => g.Neighbors(99).ToList());
            var sg = new Graph<string>();
            Check.Throws<ArgumentNullException>(() => sg.AddVertex(null!));
        }

        public void BreadthFirstOrder()
        {
            var g = new Graph<int>();
            g.AddEdge(1, 2); g.AddEdge(1, 3); g.AddEdge(2, 4); g.AddEdge(3, 4);
            Check.True(g.BreadthFirst(1).SequenceEqual(new[] { 1, 2, 3, 4 }));
        }

        public void DepthFirstOrder()
        {
            var g = new Graph<int>();
            g.AddEdge(1, 2); g.AddEdge(1, 3); g.AddEdge(2, 4); g.AddEdge(3, 4);
            Check.True(g.DepthFirst(1).SequenceEqual(new[] { 1, 2, 4, 3 }));
        }

        public void StringVerticesWithComparer()
        {
            var g = new Graph<string>(directed: true, StringComparer.OrdinalIgnoreCase);
            g.AddEdge("A", "b");
            Check.True(g.ContainsVertex("a"));
            Check.Equal(2, g.VertexCount);
        }
    }
}
