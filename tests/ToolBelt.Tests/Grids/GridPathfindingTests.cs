using System;
using System.Collections.Generic;
using ToolBelt.Grids;
using ToolBelt.Numerics;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Grids
{
    public sealed class GridPathfindingTests
    {
        private static readonly double Sqrt2 = Math.Sqrt(2.0);

        public void OpenGrid_StraightLine()
        {
            bool[,] open = Open(5, 5);
            var bfs = GridPathfinding.BreadthFirst(open, new Cell(0, 0), new Cell(0, 4));
            Check.True(bfs.Found);
            Check.Close(4.0, bfs.Cost);
            Check.Equal(5, bfs.Cells.Count);
            Check.True(bfs.Cells[0] == new Cell(0, 0) && bfs.Cells[4] == new Cell(0, 4), "endpoints");

            var astar = GridPathfinding.AStar(open, new Cell(0, 0), new Cell(0, 4));
            Check.Close(4.0, astar.Cost);
        }

        public void Eight_Diagonal_CostsSqrt2PerStep()
        {
            bool[,] open = Open(5, 5);
            var astar = GridPathfinding.AStar(open, new Cell(0, 0), new Cell(4, 4), Connectivity.Eight);
            Check.True(astar.Found);
            Check.Close(4 * Sqrt2, astar.Cost);
            Check.Equal(5, astar.Cells.Count); // 4 diagonal steps

            var bfs = GridPathfinding.BreadthFirst(open, new Cell(0, 0), new Cell(4, 4), Connectivity.Eight);
            Check.Close(4.0, bfs.Cost); // fewest steps counts each diagonal as one
        }

        public void StartOrGoalBlocked_NotFound()
        {
            bool[,] grid = Open(3, 3);
            grid[0, 0] = false;
            var r = GridPathfinding.AStar(grid, new Cell(0, 0), new Cell(2, 2));
            Check.False(r.Found);
            Check.Equal(0, r.Cells.Count);
            Check.True(double.IsPositiveInfinity(r.Cost));
        }

        public void WallWithGap_ForcesDetour()
        {
            // 5x5, a full vertical wall at column 2 except a gap at row 4.
            bool[,] grid = Open(5, 5);
            for (int r = 0; r < 4; r++) grid[r, 2] = false;
            var path = GridPathfinding.AStar(grid, new Cell(0, 0), new Cell(0, 4));
            Check.True(path.Found);
            // Must pass through the gap at (4,2).
            Check.True(PathContains(path, new Cell(4, 2)), "path goes through the gap");
            Check.True(path.Cost > 4.0, "detour is longer than the straight-line 4");
        }

        public void FullyWalledOff_NotFound()
        {
            bool[,] grid = Open(3, 3);
            for (int r = 0; r < 3; r++) grid[r, 1] = false; // slice the grid in two
            var path = GridPathfinding.Dijkstra(grid.GetLength(0), grid.GetLength(1),
                new Cell(0, 0), new Cell(0, 2), (r, c) => grid[r, c], (r, c) => 1.0);
            Check.False(path.Found);
        }

        public void StartEqualsGoal_ZeroCost()
        {
            bool[,] grid = Open(3, 3);
            var path = GridPathfinding.AStar(grid, new Cell(1, 1), new Cell(1, 1));
            Check.True(path.Found);
            Check.Close(0.0, path.Cost);
            Check.Equal(1, path.Cells.Count);
        }

        public void CornerCutting_Toggle()
        {
            // Diagonal from (0,0) to (1,1) with both orthogonal squeeze cells blocked.
            bool[,] grid = Open(2, 2);
            grid[0, 1] = false;
            grid[1, 0] = false;

            var cut = GridPathfinding.AStar(grid, new Cell(0, 0), new Cell(1, 1), Connectivity.Eight, allowCornerCutting: true);
            Check.True(cut.Found);
            Check.Close(Sqrt2, cut.Cost);

            var noCut = GridPathfinding.AStar(grid, new Cell(0, 0), new Cell(1, 1), Connectivity.Eight, allowCornerCutting: false);
            Check.False(noCut.Found); // the only route was the squeezed diagonal
        }

        public void Dijkstra_WeightedPrefersCheapRoute()
        {
            // 1x3 corridor; make the middle cell expensive, but there's no alternative, so cost reflects it.
            bool[,] grid = Open(1, 3);
            Func<int, int, double> cost = (r, c) => c == 1 ? 10.0 : 1.0;
            var path = GridPathfinding.Dijkstra(1, 3, new Cell(0, 0), new Cell(0, 2), (r, c) => grid[r, c], cost);
            Check.True(path.Found);
            // Enter (0,1)=10 then (0,2)=1 -> 11.
            Check.Close(11.0, path.Cost);
        }

        public void Validation_Throws()
        {
            bool[,] grid = Open(2, 2);
            Func<int, int, bool> pass = (r, c) => grid[r, c];
            Check.Throws<ArgumentOutOfRangeException>(() => GridPathfinding.AStar(0, 2, new Cell(0, 0), new Cell(0, 1), pass));
            Check.Throws<ArgumentOutOfRangeException>(() => GridPathfinding.AStar(2, 2, new Cell(2, 0), new Cell(0, 1), pass));
            Check.Throws<ArgumentOutOfRangeException>(() => GridPathfinding.AStar(2, 2, new Cell(0, 0), new Cell(0, 5), pass));
            Check.Throws<ArgumentNullException>(() => GridPathfinding.AStar(2, 2, new Cell(0, 0), new Cell(0, 1), null!));
            // rows * cols must fit in an int (index math is int-based).
            Check.Throws<ArgumentOutOfRangeException>(() => GridPathfinding.AStar(100000, 100000, new Cell(0, 0), new Cell(1, 1), pass));
            Check.Throws<ArgumentNullException>(() => GridPathfinding.AStar((bool[,])null!, new Cell(0, 0), new Cell(0, 1)));
            Check.Throws<ArgumentNullException>(() =>
                GridPathfinding.Dijkstra(2, 2, new Cell(0, 0), new Cell(0, 1), pass, null!));
        }

        public void Dijkstra_NegativeCost_Throws()
        {
            bool[,] grid = Open(1, 3);
            Check.Throws<InvalidOperationException>(() =>
                GridPathfinding.Dijkstra(1, 3, new Cell(0, 0), new Cell(0, 2), (r, c) => grid[r, c], (r, c) => -1.0));
        }

        public void AStar_MatchesDijkstraUniform_AndPathIsValid()
        {
            var rng = new DeterministicRandom(55512345);
            foreach (var connectivity in new[] { Connectivity.Four, Connectivity.Eight })
            {
                for (int trial = 0; trial < 300; trial++)
                {
                    int rows = rng.Next(2, 9), cols = rng.Next(2, 9);
                    var grid = RandomGrid(rng, rows, cols, 0.30);
                    var start = new Cell(0, 0);
                    var goal = new Cell(rows - 1, cols - 1);
                    grid[0, 0] = true;
                    grid[rows - 1, cols - 1] = true;
                    Func<int, int, bool> pass = (r, c) => grid[r, c];

                    var astar = GridPathfinding.AStar(rows, cols, start, goal, pass, connectivity);
                    var dijkstra = GridPathfinding.Dijkstra(rows, cols, start, goal, pass, (r, c) => 1.0, connectivity);

                    Check.True(astar.Found == dijkstra.Found, $"reachability agree (trial {trial}, {connectivity})");
                    if (!astar.Found) continue;

                    Check.Close(dijkstra.Cost, astar.Cost, 1e-9, $"A* vs Dijkstra cost (trial {trial}, {connectivity})");
                    ValidatePath(astar, grid, start, goal, connectivity, uniform: true);
                }
            }
        }

        public void Dijkstra_MatchesReferenceOnWeightedGrids()
        {
            var rng = new DeterministicRandom(777111);
            foreach (var connectivity in new[] { Connectivity.Four, Connectivity.Eight })
            {
                for (int trial = 0; trial < 250; trial++)
                {
                    int rows = rng.Next(2, 8), cols = rng.Next(2, 8);
                    var grid = RandomGrid(rng, rows, cols, 0.25);
                    grid[0, 0] = true;
                    grid[rows - 1, cols - 1] = true;
                    var weight = new double[rows, cols];
                    for (int r = 0; r < rows; r++)
                        for (int c = 0; c < cols; c++)
                            weight[r, c] = 1.0 + rng.Next(0, 9); // positive integer weights

                    var start = new Cell(0, 0);
                    var goal = new Cell(rows - 1, cols - 1);
                    Func<int, int, bool> pass = (r, c) => grid[r, c];
                    Func<int, int, double> cost = (r, c) => weight[r, c];

                    var actual = GridPathfinding.Dijkstra(rows, cols, start, goal, pass, cost, connectivity);
                    var reference = RefDijkstra(rows, cols, start, goal, pass, cost, connectivity);

                    Check.True(actual.Found == reference.found, $"reachability (trial {trial}, {connectivity})");
                    if (!actual.Found) continue;
                    Check.Close(reference.cost, actual.Cost, 1e-9, $"Dijkstra cost (trial {trial}, {connectivity})");
                    // Reported Cost must match the path when re-summed.
                    Check.Close(actual.Cost, ReSum(actual, cost, connectivity), 1e-9, $"path re-sum (trial {trial})");
                    ValidatePath(actual, grid, start, goal, connectivity, uniform: false);
                }
            }
        }

        // --- helpers ---

        private static bool PathContains(GridPath path, Cell target)
        {
            foreach (var cell in path.Cells)
                if (cell == target)
                    return true;
            return false;
        }

        private static bool[,] Open(int rows, int cols)
        {
            var g = new bool[rows, cols];
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                    g[r, c] = true;
            return g;
        }

        private static bool[,] RandomGrid(DeterministicRandom rng, int rows, int cols, double blockProb)
        {
            var g = new bool[rows, cols];
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                    g[r, c] = rng.NextDouble() >= blockProb;
            return g;
        }

        private static void ValidatePath(GridPath path, bool[,] grid, Cell start, Cell goal, Connectivity connectivity, bool uniform)
        {
            Check.True(path.Cells.Count >= 1, "non-empty path");
            Check.True(path.Cells[0] == start, "starts at start");
            Check.True(path.Cells[path.Cells.Count - 1] == goal, "ends at goal");
            for (int i = 0; i < path.Cells.Count; i++)
            {
                Cell cell = path.Cells[i];
                Check.True(grid[cell.Row, cell.Col], "every cell walkable");
                if (i == 0) continue;
                Cell prev = path.Cells[i - 1];
                int dr = Math.Abs(cell.Row - prev.Row), dc = Math.Abs(cell.Col - prev.Col);
                bool adjacent = connectivity == Connectivity.Eight
                    ? dr <= 1 && dc <= 1 && (dr + dc) > 0
                    : dr + dc == 1;
                Check.True(adjacent, $"step {i} is a single {connectivity} move");
            }
        }

        private static double ReSum(GridPath path, Func<int, int, double> cost, Connectivity connectivity)
        {
            double total = 0;
            for (int i = 1; i < path.Cells.Count; i++)
            {
                Cell a = path.Cells[i - 1], b = path.Cells[i];
                bool diagonal = a.Row != b.Row && a.Col != b.Col;
                double c = cost(b.Row, b.Col);
                total += diagonal ? c * Sqrt2 : c;
            }
            return total;
        }

        // Independent O(V^2) Dijkstra mirroring the production step/corner rules exactly.
        private static (bool found, double cost) RefDijkstra(
            int rows, int cols, Cell start, Cell goal, Func<int, int, bool> pass, Func<int, int, double> cost, Connectivity connectivity)
        {
            if (!pass(start.Row, start.Col) || !pass(goal.Row, goal.Col))
                return (false, double.PositiveInfinity);

            int n = rows * cols;
            var dist = new double[n];
            var done = new bool[n];
            for (int i = 0; i < n; i++) dist[i] = double.PositiveInfinity;
            int s = start.Row * cols + start.Col, g = goal.Row * cols + goal.Col;
            dist[s] = 0;

            int[] dr = connectivity == Connectivity.Eight ? new[] { -1, 1, 0, 0, -1, -1, 1, 1 } : new[] { -1, 1, 0, 0 };
            int[] dc = connectivity == Connectivity.Eight ? new[] { 0, 0, -1, 1, -1, 1, -1, 1 } : new[] { 0, 0, -1, 1 };

            for (int iter = 0; iter < n; iter++)
            {
                int u = -1;
                double best = double.PositiveInfinity;
                for (int i = 0; i < n; i++)
                    if (!done[i] && dist[i] < best) { best = dist[i]; u = i; }
                if (u == -1) break;
                done[u] = true;
                int ur = u / cols, uc = u % cols;
                for (int d = 0; d < dr.Length; d++)
                {
                    int vr = ur + dr[d], vc = uc + dc[d];
                    if ((uint)vr >= (uint)rows || (uint)vc >= (uint)cols) continue;
                    if (!pass(vr, vc)) continue;
                    bool diagonal = d >= 4;
                    double w = cost(vr, vc);
                    double nd = dist[u] + (diagonal ? w * Sqrt2 : w);
                    int v = vr * cols + vc;
                    if (nd < dist[v]) dist[v] = nd;
                }
            }
            return double.IsPositiveInfinity(dist[g]) ? (false, double.PositiveInfinity) : (true, dist[g]);
        }
    }
}
