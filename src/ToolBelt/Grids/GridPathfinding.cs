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
    /// <see cref="Connectivity.Eight"/>, the <c>allowCornerCutting</c> flag controls diagonal moves: when
    /// false, a diagonal step is disallowed if <em>either</em> of the two orthogonal cells it passes between
    /// is blocked (so the path never cuts a wall corner); when true (the default), diagonals are always
    /// allowed as long as the destination is walkable. Search state is per-cell arrays on a grid of up to 2^24
    /// cells; on a larger one it is kept only for the cells the search reaches, so a short path on a huge grid
    /// stays cheap.
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

            var state = new SearchState(rows * cols);
            int startIdx = start.Row * cols + start.Col;
            int goalIdx = goal.Row * cols + goal.Col;

            var queue = new Queue<int>();
            state.SetDone(startIdx);
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
                    if (vr < 0 || vr >= rows || vc < 0 || vc >= cols) continue;
                    if (!passable(vr, vc)) continue;
                    if (d >= 4 && !allowCornerCutting && (!passable(ur, vc) || !passable(vr, uc))) continue;

                    int v = vr * cols + vc;
                    if (state.Done(v)) continue;
                    state.SetDone(v);
                    state.SetPrev(v, u);
                    if (v == goalIdx) { found = true; break; }
                    queue.Enqueue(v);
                }
            }

            if (!state.Done(goalIdx))
                return NotFound();
            var path = Reconstruct(state, goalIdx, cols);
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
        /// throws <see cref="InvalidOperationException"/> when the search reaches that cell (Dijkstra requires
        /// non-negative weights); cells the search never reaches are not asked for their cost.
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

            var state = new SearchState(rows * cols);
            int startIdx = start.Row * cols + start.Col;
            int goalIdx = goal.Row * cols + goal.Col;
            state.SetDist(startIdx, 0.0);

            var heap = new MinHeap(16);
            heap.Push(startIdx, heuristic?.Invoke(start.Row, start.Col) ?? 0.0);

            int dirs = connectivity == Connectivity.Eight ? 8 : 4;
            while (heap.TryPop(out int u, out _))
            {
                if (state.Done(u)) continue; // stale heap entry (lazy decrease-key)
                state.SetDone(u);
                if (u == goalIdx) break;

                int ur = u / cols, uc = u % cols;
                for (int d = 0; d < dirs; d++)
                {
                    int vr = ur + DRow[d], vc = uc + DCol[d];
                    if (vr < 0 || vr >= rows || vc < 0 || vc >= cols) continue;
                    if (!passable(vr, vc)) continue;
                    if (d >= 4 && !allowCornerCutting && (!passable(ur, vc) || !passable(vr, uc))) continue;

                    int v = vr * cols + vc;
                    if (state.Done(v)) continue;
                    double nd = state.Dist(u) + step(ur, uc, vr, vc);
                    if (nd < state.Dist(v))
                    {
                        state.SetDist(v, nd);
                        state.SetPrev(v, u);
                        heap.Push(v, nd + (heuristic?.Invoke(vr, vc) ?? 0.0));
                    }
                }
            }

            if (double.IsPositiveInfinity(state.Dist(goalIdx)))
                return NotFound();
            return new GridPath(true, Reconstruct(state, goalIdx, cols), state.Dist(goalIdx));
        }

        private static List<Cell> Reconstruct(SearchState state, int goalIdx, int cols)
        {
            var path = new List<Cell>();
            for (int cur = goalIdx; cur != -1; cur = state.Prev(cur))
                path.Add(new Cell(cur / cols, cur % cols));
            path.Reverse();
            return path;
        }

        private static GridPath NotFound() => new GridPath(false, Array.Empty<Cell>(), double.PositiveInfinity);

        private static void Validate(int rows, int cols, Cell start, Cell goal, Func<int, int, bool> passable)
        {
            if (rows <= 0) throw new ArgumentOutOfRangeException(nameof(rows), rows, "Grid must have at least one row.");
            if (cols <= 0) throw new ArgumentOutOfRangeException(nameof(cols), cols, "Grid must have at least one column.");
            if ((long)rows * cols > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(rows), "Grid is too large: rows * cols must fit in an int.");
            if (passable is null) throw new ArgumentNullException(nameof(passable));
            if (start.Row < 0 || start.Row >= rows || start.Col < 0 || start.Col >= cols)
                throw new ArgumentOutOfRangeException(nameof(start), start, "Start is outside the grid.");
            if (goal.Row < 0 || goal.Row >= rows || goal.Col < 0 || goal.Col >= cols)
                throw new ArgumentOutOfRangeException(nameof(goal), goal, "Goal is outside the grid.");
        }

        /// <summary>
        /// Per-cell search state: the predecessor, the best distance, and a done flag (seen for breadth-first, closed
        /// for the cost searches). Arrays for up to 2^24 cells; past that, dictionaries holding only the cells reached:
        /// arrays over a 46340² grid took 12 GB for a one-step path.
        /// </summary>
        private sealed class SearchState
        {
            private const int DenseLimit = 1 << 24;
            private readonly int[]? _prev;
            private double[]? _dist; // allocated on first use: breadth-first never needs it
            private readonly int _n;
            private readonly bool[]? _done;
            private readonly Dictionary<int, int>? _prevMap;
            private readonly Dictionary<int, double>? _distMap;
            private readonly HashSet<int>? _doneSet;

            public SearchState(int n)
            {
                if (n <= DenseLimit)
                {
                    _prev = new int[n];
                    for (int i = 0; i < n; i++) _prev[i] = -1;
                    _done = new bool[n];
                    _n = n;
                }
                else
                {
                    _prevMap = new Dictionary<int, int>();
                    _distMap = new Dictionary<int, double>();
                    _doneSet = new HashSet<int>();
                }
            }

            public int Prev(int i) => _prev != null ? _prev[i] : _prevMap!.TryGetValue(i, out int p) ? p : -1;

            public void SetPrev(int i, int p)
            {
                if (_prev != null) _prev[i] = p;
                else _prevMap![i] = p;
            }

            public double Dist(int i) =>
                _distMap is null ? (_dist is null ? double.PositiveInfinity : _dist[i])
                : _distMap.TryGetValue(i, out double d) ? d : double.PositiveInfinity;

            public void SetDist(int i, double d)
            {
                if (_distMap != null) { _distMap[i] = d; return; }
                if (_dist is null)
                {
                    _dist = new double[_n];
                    for (int k = 0; k < _n; k++) _dist[k] = double.PositiveInfinity;
                }
                _dist[i] = d;
            }

            public bool Done(int i) => _done != null ? _done[i] : _doneSet!.Contains(i);

            public void SetDone(int i)
            {
                if (_done != null) _done[i] = true;
                else _doneSet!.Add(i);
            }
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
