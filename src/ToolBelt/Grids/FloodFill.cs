// ToolBelt drop-in — depends only on Grids/Cell.cs (BCL otherwise).
using System;
using System.Collections.Generic;

namespace ToolBelt.Grids
{
    /// <summary>
    /// Flood fill ("paint bucket"): finds or recolours the maximal region of equal-valued cells connected
    /// to a seed cell. The traversal is iterative (an explicit queue), so it never overflows the call stack
    /// on large regions. Not thread-safe against concurrent mutation of the grid.
    /// </summary>
    public static class FloodFill
    {
        /// <summary>
        /// Returns every cell reachable from (<paramref name="row"/>, <paramref name="col"/>) by stepping
        /// only between cells whose value equals the seed cell's value, under the given
        /// <paramref name="connectivity"/>. The seed cell is always included. Cells are returned in
        /// breadth-first discovery order.
        /// </summary>
        public static IReadOnlyList<Cell> Region<T>(
            T[,] grid, int row, int col,
            Connectivity connectivity = Connectivity.Four,
            IEqualityComparer<T>? comparer = null)
        {
            if (grid is null) throw new ArgumentNullException(nameof(grid));
            int rows = grid.GetLength(0), cols = grid.GetLength(1);
            if ((uint)row >= (uint)rows) throw new ArgumentOutOfRangeException(nameof(row), row, "Row is outside the grid.");
            if ((uint)col >= (uint)cols) throw new ArgumentOutOfRangeException(nameof(col), col, "Column is outside the grid.");
            comparer ??= EqualityComparer<T>.Default;

            T target = grid[row, col];
            return Collect(rows, cols, row, col, connectivity, (r, c) => comparer.Equals(grid[r, c], target));
        }

        /// <summary>
        /// Flood-fills in place: replaces the value of every cell in the seed region (see
        /// <see cref="Region{T}(T[,], int, int, Connectivity, IEqualityComparer{T})"/>) with
        /// <paramref name="newValue"/> and returns the number of cells changed. If the seed cell already
        /// holds <paramref name="newValue"/> nothing is written and 0 is returned.
        /// </summary>
        public static int Fill<T>(
            T[,] grid, int row, int col, T newValue,
            Connectivity connectivity = Connectivity.Four,
            IEqualityComparer<T>? comparer = null)
        {
            if (grid is null) throw new ArgumentNullException(nameof(grid));
            int rows = grid.GetLength(0), cols = grid.GetLength(1);
            if ((uint)row >= (uint)rows) throw new ArgumentOutOfRangeException(nameof(row), row, "Row is outside the grid.");
            if ((uint)col >= (uint)cols) throw new ArgumentOutOfRangeException(nameof(col), col, "Column is outside the grid.");
            comparer ??= EqualityComparer<T>.Default;

            T target = grid[row, col];
            if (comparer.Equals(target, newValue))
                return 0; // Already the target colour: nothing to change, and skipping avoids a pointless rescan.

            var region = Collect(rows, cols, row, col, connectivity, (r, c) => comparer.Equals(grid[r, c], target));
            foreach (var cell in region)
                grid[cell.Row, cell.Col] = newValue;
            return region.Count;
        }

        /// <summary>
        /// General region traversal: returns every cell reachable from the seed under
        /// <paramref name="connectivity"/> for which <paramref name="belongs"/> returns true. The seed cell
        /// itself must satisfy <paramref name="belongs"/>, otherwise the result is empty. Useful for filling
        /// over a boolean passability map or any predicate without allocating a value grid.
        /// </summary>
        public static IReadOnlyList<Cell> Region(
            int rows, int cols, int row, int col,
            Func<int, int, bool> belongs,
            Connectivity connectivity = Connectivity.Four)
        {
            if (belongs is null) throw new ArgumentNullException(nameof(belongs));
            if (rows < 0) throw new ArgumentOutOfRangeException(nameof(rows), rows, "Row count must not be negative.");
            if (cols < 0) throw new ArgumentOutOfRangeException(nameof(cols), cols, "Column count must not be negative.");
            if ((uint)row >= (uint)rows) throw new ArgumentOutOfRangeException(nameof(row), row, "Row is outside the grid.");
            if ((uint)col >= (uint)cols) throw new ArgumentOutOfRangeException(nameof(col), col, "Column is outside the grid.");
            return Collect(rows, cols, row, col, connectivity, belongs);
        }

        private static IReadOnlyList<Cell> Collect(
            int rows, int cols, int row, int col, Connectivity connectivity, Func<int, int, bool> belongs)
        {
            var result = new List<Cell>();
            if (!belongs(row, col))
                return result;

            var visited = new bool[rows, cols];
            var queue = new Queue<Cell>();
            visited[row, col] = true;
            queue.Enqueue(new Cell(row, col));

            int dirs = connectivity == Connectivity.Eight ? 8 : 4;
            while (queue.Count > 0)
            {
                Cell cell = queue.Dequeue();
                result.Add(cell);
                for (int d = 0; d < dirs; d++)
                {
                    int nr = cell.Row + DRow[d];
                    int nc = cell.Col + DCol[d];
                    if ((uint)nr >= (uint)rows || (uint)nc >= (uint)cols) continue;
                    if (visited[nr, nc]) continue;
                    if (!belongs(nr, nc)) continue;
                    visited[nr, nc] = true;
                    queue.Enqueue(new Cell(nr, nc));
                }
            }
            return result;
        }

        // The first four entries are the orthogonal neighbours; the last four are the diagonals.
        private static readonly int[] DRow = { -1, 1, 0, 0, -1, -1, 1, 1 };
        private static readonly int[] DCol = { 0, 0, -1, 1, -1, 1, -1, 1 };
    }
}
