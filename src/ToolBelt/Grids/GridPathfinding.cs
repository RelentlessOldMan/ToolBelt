// ToolBelt drop-in — depends only on Grids/Cell.cs (BCL otherwise).
using System;
using System.Collections.Generic;

namespace ToolBelt.Grids
{
    /// <summary>The result of a grid path search: whether a path exists, the path itself, and its cost.</summary>
    public sealed class GridPath
    {
        internal GridPath(bool found, IReadOnlyList<Cell> cells, double cost)
        {
            Found = found;
            Cells = cells;
            Cost = cost;
        }

        /// <summary>True if the goal is reachable from the start.</summary>
        public bool Found { get; }

        /// <summary>The path from start to goal inclusive (empty if unreachable).</summary>
        public IReadOnlyList<Cell> Cells { get; }

        /// <summary>
        /// The path cost: a step count for <see cref="GridPathfinding.BreadthFirst(int, int, Cell, Cell, Func{int, int, bool}, Connectivity, bool)"/>,
        /// the summed movement cost otherwise; <see cref="double.PositiveInfinity"/> if unreachable.
        /// </summary>
        public double Cost { get; }
    }

    /// <summary>
    /// Path search on a 2-D grid whose walkable cells are described by a predicate (or a boolean map).
    /// <see cref="BreadthFirst(int, int, Cell, Cell, Func{int, int, bool}, Connectivity, bool)"/> finds a
    /// fewest-steps path (every move counts as one step); <see cref="AStar(int, int, Cell, Cell, Func{int, int, bool}, Connectivity, bool)"/>
    /// finds a shortest geometric path on a uniform grid (orthogonal step 1, diagonal step √2) using an
    /// admissible heuristic; <see cref="Dijkstra(int, int, Cell, Cell, Func{int, int, bool}, Func{int, int, double}, Connectivity, bool)"/>
    /// finds a least-cost path for arbitrary non-negative per-cell entry costs. With
    /// <see cref="Connectivity.Eight"/>, the <c>allowCornerCutting</c> flag controls whether a diagonal
    /// move may squeeze between two blocked orthogonal cells.
    /// </summary>
    public static class GridPathfinding
    {
        /// <summary>Fewest-steps path (breadth-first); every orthogonal or diagonal move counts as one step.</summary>
        public static GridPath BreadthFirst(
            int rows, int cols, Cell start, Cell goal, Func<int, int, bool> passable,
            Connectivity connectivity = Connectivity.Four, bool allowCornerCutting = true)
        {
            Validate(rows, cols, start, goal, passable);
            if (!passable(start.Row, start.Col) || !passable(goal.Row, goal.Col))
                return NotFound();

            int n = rows * cols;
            var prev = NewPrev(n);
            var seen = new bool[n];
            int startIdx = start.Row * cols + start.Col;
            int goalIdx = goal.Row * cols + goal.Col;

            var queue = new Queue<int>();
            seen[startIdx] = true;
            queue.Enqueue(startIdx);
            bool found = startIdx == goalIdx;

            int dirs = connectivity == Connectivity.Eight ? 8 : 4;
            while (queue.Count > 0 && !found)
            {
                int u = queue.Dequeue();
                int ur = u / cols, uc = u % cols;
                for (int d = 0; d < dirs; d++)
                {
                    int vr = ur + DRow[d], vc = uc + DCol[d];
                    if ((uint)vr >= (uint)rows || (uint)vc >= (uint)cols) continue;
                    if (!passable(vr, vc)) continue;
                    if (d >= 4 && !allowCornerCutting && (!passable(ur, vc) || !passable(vr, uc))) continue;

                    int v = vr * cols + vc;
                    if (seen[v]) continue;
                    seen[v] = true;
                    prev[v] = u;
                    if (v == goalIdx) { found = true; break; }
                    queue.Enqueue(v);
                }
            }

            if (!seen[goalIdx])
                return NotFound();
            var path = Reconstruct(prev, goalIdx, cols);
            return new GridPath(true, path, path.Count - 1);
        }

        /// <summary>
        /// Shortest geometric path on a uniform grid using A* with an admissible/consistent heuristic
        /// (Manhattan for <see cref="Connectivity.Four"/>, octile for <see cref="Connectivity.Eight"/>).
        /// Orthogonal moves cost 1 and diagonal moves cost √2.
        /// </summary>
        public static GridPath AStar(
            int rows, int cols, Cell start, Cell goal, Func<int, int, bool> passable,
            Connectivity connectivity = Connectivity.Four, bool allowCornerCutting = true)
        {
            Validate(rows, cols, start, goal, passable);
            double sqrt2 = Math.Sqrt(2.0);
            Func<int, int, int, int, double> step = (fr, fc, tr, tc) => (fr != tr && fc != tc) ? sqrt2 : 1.0;

            Func<int, int, double> heuristic;
            if (connectivity == Connectivity.Eight)
                heuristic = (r, c) =>
                {
                    int dr = Math.Abs(r - goal.Row), dc = Math.Abs(c - goal.Col);
                    int lo = Math.Min(dr, dc), hi = Math.Max(dr, dc);
                    return hi + (sqrt2 - 1.0) * lo; // octile distance
                };
            else
                heuristic = (r, c) => Math.Abs(r - goal.Row) + Math.Abs(c - goal.Col); // Manhattan

            return Search(rows, cols, start, goal, passable, step, heuristic, connectivity, allowCornerCutting);
        }

        /// <summary>
        /// Least-cost path for arbitrary non-negative entry costs. <paramref name="enterCost"/> gives the
        /// cost to step onto a cell; a diagonal move additionally multiplies that by √2. A negative cost
        /// throws <see cref="InvalidOperationException"/> (Dijkstra requires non-negative weights).
        /// </summary>
        public static GridPath Dijkstra(
            int rows, int cols, Cell start, Cell goal, Func<int, int, bool> passable, Func<int, int, double> enterCost,
            Connectivity connectivity = Connectivity.Four, bool allowCornerCutting = true)
        {
            Validate(rows, cols, start, goal, passable);
            if (enterCost is null) throw new ArgumentNullException(nameof(enterCost));
            double sqrt2 = Math.Sqrt(2.0);
            Func<int, int, int, int, double> step = (fr, fc, tr, tc) =>
            {
                double c = enterCost(tr, tc);
                if (c < 0) throw new InvalidOperationException("Dijkstra does not support negative entry costs.");
                return (fr != tr && fc != tc) ? c * sqrt2 : c;
            };
            return Search(rows, cols, start, goal, passable, step, null, connectivity, allowCornerCutting);
        }

        // --- boolean-map convenience overloads (common "walls" case) ---

        /// <summary>Fewest-steps path over a boolean passability map (true = walkable).</summary>
        public static GridPath BreadthFirst(
            bool[,] passable, Cell start, Cell goal,
            Connectivity connectivity = Connectivity.Four, bool allowCornerCutting = true)
        {
            if (passable is null) throw new ArgumentNullException(nameof(passable));
            return BreadthFirst(passable.GetLength(0), passable.GetLength(1), start, goal,
                (r, c) => passable[r, c], connectivity, allowCornerCutting);
        }

        /// <summary>Shortest geometric path over a boolean passability map (true = walkable).</summary>
        public static GridPath AStar(
            bool[,] passable, Cell start, Cell goal,
            Connectivity connectivity = Connectivity.Four, bool allowCornerCutting = true)
        {
            if (passable is null) throw new ArgumentNullException(nameof(passable));
            return AStar(passable.GetLength(0), passable.GetLength(1), start, goal,
                (r, c) => passable[r, c], connectivity, allowCornerCutting);
        }

        private static GridPath Search(
            int rows, int cols, Cell start, Cell goal, Func<int, int, bool> passable,
            Func<int, int, int, int, double> step, Func<int, int, double>? heuristic,
            Connectivity connectivity, bool allowCornerCutting)
        {
            if (!passable(start.Row, start.Col) || !passable(goal.Row, goal.Col))
                return NotFound();

            int n = rows * cols;
            var dist = new double[n];
            for (int i = 0; i < n; i++) dist[i] = double.PositiveInfinity;
            var prev = NewPrev(n);
            var closed = new bool[n];

            int startIdx = start.Row * cols + start.Col;
            int goalIdx = goal.Row * cols + goal.Col;
            dist[startIdx] = 0.0;

            var heap = new MinHeap(Math.Min(n, 16));
            heap.Push(startIdx, heuristic?.Invoke(start.Row, start.Col) ?? 0.0);

            int dirs = connectivity == Connectivity.Eight ? 8 : 4;
            while (heap.TryPop(out int u, out _))
            {
                if (closed[u]) continue; // stale heap entry (lazy decrease-key)
                closed[u] = true;
                if (u == goalIdx) break;

                int ur = u / cols, uc = u % cols;
                for (int d = 0; d < dirs; d++)
                {
                    int vr = ur + DRow[d], vc = uc + DCol[d];
                    if ((uint)vr >= (uint)rows || (uint)vc >= (uint)cols) continue;
                    if (!passable(vr, vc)) continue;
                    if (d >= 4 && !allowCornerCutting && (!passable(ur, vc) || !passable(vr, uc))) continue;

                    int v = vr * cols + vc;
                    if (closed[v]) continue;
                    double nd = dist[u] + step(ur, uc, vr, vc);
                    if (nd < dist[v])
                    {
                        dist[v] = nd;
                        prev[v] = u;
                        heap.Push(v, nd + (heuristic?.Invoke(vr, vc) ?? 0.0));
                    }
                }
            }

            if (double.IsPositiveInfinity(dist[goalIdx]))
                return NotFound();
            return new GridPath(true, Reconstruct(prev, goalIdx, cols), dist[goalIdx]);
        }

        private static int[] NewPrev(int n)
        {
            var prev = new int[n];
            for (int i = 0; i < n; i++) prev[i] = -1;
            return prev;
        }

        private static List<Cell> Reconstruct(int[] prev, int goalIdx, int cols)
        {
            var path = new List<Cell>();
            for (int cur = goalIdx; cur != -1; cur = prev[cur])
                path.Add(new Cell(cur / cols, cur % cols));
            path.Reverse();
            return path;
        }

        private static GridPath NotFound() => new GridPath(false, Array.Empty<Cell>(), double.PositiveInfinity);

        private static void Validate(int rows, int cols, Cell start, Cell goal, Func<int, int, bool> passable)
        {
            if (rows <= 0) throw new ArgumentOutOfRangeException(nameof(rows), rows, "Grid must have at least one row.");
            if (cols <= 0) throw new ArgumentOutOfRangeException(nameof(cols), cols, "Grid must have at least one column.");
            if (passable is null) throw new ArgumentNullException(nameof(passable));
            if ((uint)start.Row >= (uint)rows || (uint)start.Col >= (uint)cols)
                throw new ArgumentOutOfRangeException(nameof(start), start, "Start is outside the grid.");
            if ((uint)goal.Row >= (uint)rows || (uint)goal.Col >= (uint)cols)
                throw new ArgumentOutOfRangeException(nameof(goal), goal, "Goal is outside the grid.");
        }

        // The first four entries are the orthogonal neighbours; the last four are the diagonals.
        private static readonly int[] DRow = { -1, 1, 0, 0, -1, -1, 1, 1 };
        private static readonly int[] DCol = { 0, 0, -1, 1, -1, 1, -1, 1 };

        /// <summary>
        /// A tiny binary min-heap over (cell index, priority) pairs. Inlined so this file carries no
        /// dependency on the Collections division; supports the lazy decrease-key the search relies on.
        /// </summary>
        private sealed class MinHeap
        {
            private int[] _idx;
            private double[] _key;
            private int _count;

            public MinHeap(int capacity)
            {
                if (capacity < 1) capacity = 1;
                _idx = new int[capacity];
                _key = new double[capacity];
            }

            public void Push(int idx, double key)
            {
                if (_count == _idx.Length)
                {
                    Array.Resize(ref _idx, _count * 2);
                    Array.Resize(ref _key, _count * 2);
                }
                _idx[_count] = idx;
                _key[_count] = key;
                SiftUp(_count);
                _count++;
            }

            public bool TryPop(out int idx, out double key)
            {
                if (_count == 0)
                {
                    idx = 0;
                    key = 0.0;
                    return false;
                }
                idx = _idx[0];
                key = _key[0];
                _count--;
                _idx[0] = _idx[_count];
                _key[0] = _key[_count];
                SiftDown(0);
                return true;
            }

            private void SiftUp(int i)
            {
                while (i > 0)
                {
                    int p = (i - 1) / 2;
                    if (_key[p] <= _key[i]) break;
                    Swap(i, p);
                    i = p;
                }
            }

            private void SiftDown(int i)
            {
                while (true)
                {
                    int l = 2 * i + 1, r = 2 * i + 2, smallest = i;
                    if (l < _count && _key[l] < _key[smallest]) smallest = l;
                    if (r < _count && _key[r] < _key[smallest]) smallest = r;
                    if (smallest == i) break;
                    Swap(i, smallest);
                    i = smallest;
                }
            }

            private void Swap(int a, int b)
            {
                (_idx[a], _idx[b]) = (_idx[b], _idx[a]);
                (_key[a], _key[b]) = (_key[b], _key[a]);
            }
        }
    }
}
