using System;
using System.Collections.Generic;
using System.Linq;
using ToolBelt.Graphs;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Graphs
{
    public sealed class ShortestPathTests
    {
        public void DijkstraKnownGraph()
        {
            var g = new Graph<string>(directed: true);
            g.AddEdge("A", "B", 1);
            g.AddEdge("B", "C", 2);
            g.AddEdge("A", "C", 5);
            g.AddEdge("C", "D", 1);

            var r = ShortestPath.Dijkstra(g, "A", "D");
            Check.True(r.Found);
            Check.Close(4, r.Distance, 1e-9);              // A->B->C->D = 1+2+1
            Check.True(r.Path.SequenceEqual(new[] { "A", "B", "C", "D" }));
        }

        public void BreadthFirstCountsHops()
        {
            var g = new Graph<int>(directed: true);
            g.AddEdge(1, 2, 100); g.AddEdge(2, 3, 100); g.AddEdge(1, 3, 100);
            var r = ShortestPath.BreadthFirst(g, 1, 3);
            Check.True(r.Found);
            Check.Close(1, r.Distance, 1e-9);               // direct edge = 1 hop, weights ignored
            Check.True(r.Path.SequenceEqual(new[] { 1, 3 }));
        }

        public void SourceEqualsTarget()
        {
            var g = new Graph<int>();
            g.AddVertex(1);
            var r = ShortestPath.Dijkstra(g, 1, 1);
            Check.True(r.Found);
            Check.Close(0, r.Distance, 1e-9);
            Check.True(r.Path.SequenceEqual(new[] { 1 }));
        }

        public void Unreachable()
        {
            var g = new Graph<int>(directed: true);
            g.AddEdge(1, 2);
            g.AddVertex(3);
            var r = ShortestPath.Dijkstra(g, 1, 3);
            Check.False(r.Found);
            Check.True(double.IsPositiveInfinity(r.Distance));
            Check.Equal(0, r.Path.Count);
        }

        public void NegativeWeight_Throws()
        {
            var g = new Graph<int>(directed: true);
            g.AddEdge(1, 2, -1);
            Check.Throws<InvalidOperationException>(() => ShortestPath.Dijkstra(g, 1, 2));
        }

        public void MissingEndpoint_Throws()
        {
            var g = new Graph<int>();
            g.AddVertex(1);
            Check.Throws<KeyNotFoundException>(() => ShortestPath.Dijkstra(g, 1, 99));
            Check.Throws<KeyNotFoundException>(() => ShortestPath.BreadthFirst(g, 99, 1));
        }

        // Differential: Dijkstra distance must match Floyd-Warshall on random non-negative graphs, and the
        // returned path's summed weight must equal the reported distance.
        public void Property_MatchesFloydWarshall()
        {
            var rng = new Random(7);
            for (int trial = 0; trial < 400; trial++)
            {
                int n = rng.Next(2, 10);
                var g = new Graph<int>(directed: true);
                for (int i = 0; i < n; i++) g.AddVertex(i);

                const double INF = double.PositiveInfinity;
                var d = new double[n, n];
                for (int i = 0; i < n; i++)
                    for (int j = 0; j < n; j++)
                        d[i, j] = i == j ? 0 : INF;

                for (int i = 0; i < n; i++)
                    for (int j = 0; j < n; j++)
                        if (i != j && rng.NextDouble() < 0.35)
                        {
                            double w = rng.Next(1, 20);
                            g.AddEdge(i, j, w);
                            if (w < d[i, j]) d[i, j] = w;
                        }

                for (int k = 0; k < n; k++)
                    for (int i = 0; i < n; i++)
                        for (int j = 0; j < n; j++)
                            if (d[i, k] + d[k, j] < d[i, j])
                                d[i, j] = d[i, k] + d[k, j];

                int s = rng.Next(n), t = rng.Next(n);
                var r = ShortestPath.Dijkstra(g, s, t);

                if (double.IsPositiveInfinity(d[s, t]))
                {
                    Check.False(r.Found, $"trial {trial}: ({s}->{t}) should be unreachable");
                }
                else
                {
                    Check.True(r.Found, $"trial {trial}: ({s}->{t}) should be reachable");
                    Check.Close(d[s, t], r.Distance, 1e-9, $"trial {trial}: ({s}->{t}) distance");
                    Check.Close(r.Distance, PathWeight(g, r.Path), 1e-9, $"trial {trial}: path sum");
                }
            }
        }

        private static double PathWeight(Graph<int> g, IReadOnlyList<int> path)
        {
            double sum = 0;
            for (int i = 0; i + 1 < path.Count; i++)
            {
                double best = double.PositiveInfinity;
                foreach (var (to, w) in g.Neighbors(path[i]))
                    if (to == path[i + 1] && w < best) best = w;
                sum += best;
            }
            return sum;
        }
    }
}
