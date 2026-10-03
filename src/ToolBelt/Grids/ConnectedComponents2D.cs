// ToolBelt drop-in — depends only on Grids/Cell.cs (BCL otherwise).
using System;
using System.Collections.Generic;

namespace ToolBelt.Grids
{
    /// <summary>A single connected component: its label, the cells it covers, and their bounding box.</summary>
    public sealed class GridComponent
    {
        internal GridComponent(int label, IReadOnlyList<Cell> cells, int minRow, int minCol, int maxRow, int maxCol)
        {
            Label = label;
            Cells = cells;
            MinRow = minRow;
            MinCol = minCol;
            MaxRow = maxRow;
            MaxCol = maxCol;
        }

        /// <summary>The component's 1-based label, matching the value written into <see cref="ComponentLabeling.Labels"/>.</summary>
        public int Label { get; }

        /// <summary>The cells in the component, in breadth-first discovery order.</summary>
        public IReadOnlyList<Cell> Cells { get; }

        /// <summary>The number of cells in the component.</summary>
        public int Size => Cells.Count;

        public int MinRow { get; }
        public int MinCol { get; }
        public int MaxRow { get; }
        public int MaxCol { get; }

        /// <summary>Height of the axis-aligned bounding box (rows).</summary>
        public int Height => MaxRow - MinRow + 1;

        /// <summary>Width of the axis-aligned bounding box (columns).</summary>
        public int Width => MaxCol - MinCol + 1;
    }

    /// <summary>The outcome of labeling a grid's connected components.</summary>
    public sealed class ComponentLabeling
    {
        internal ComponentLabeling(int[,] labels, IReadOnlyList<GridComponent> components)
        {
            Labels = labels;
            Components = components;
        }

        /// <summary>
        /// Per-cell component label. For the boolean-mask overload, background (false) cells are 0 and
        /// foreground components are numbered 1.. in row-major discovery order. For the value overload every
        /// cell belongs to a component, so all labels are &gt;= 1.
        /// </summary>
        public int[,] Labels { get; }

        /// <summary>The components ordered by label; <c>Components[i]</c> has <c>Label == i + 1</c>.</summary>
        public IReadOnlyList<GridComponent> Components { get; }

        /// <summary>The number of components found.</summary>
        public int Count => Components.Count;
    }

    /// <summary>
    /// Connected-component labeling (CCL) for 2-D grids: partitions cells into maximal connected regions.
    /// The scan is row-major and the search breadth-first, so component numbering is deterministic. The
    /// traversal is iterative, so it is safe on grids whose components span the whole array.
    /// </summary>
    public static class ConnectedComponents2D
    {
        /// <summary>
        /// Labels the connected regions of <paramref name="mask"/>'s true ("foreground") cells. False cells
        /// are background: they receive label 0 and belong to no component.
        /// </summary>
        public static ComponentLabeling Label(bool[,] mask, Connectivity connectivity = Connectivity.Four)
        {
            if (mask is null) throw new ArgumentNullException(nameof(mask));
            int rows = mask.GetLength(0), cols = mask.GetLength(1);
            return LabelCore(rows, cols, connectivity, foregroundOnly: true,
                belongs: (r, c) => mask[r, c],
                sameRegion: (r1, c1, r2, c2) => true); // foreground connects to foreground unconditionally
        }

        /// <summary>
        /// Labels maximal regions of equal value: two adjacent cells share a component iff their values are
        /// equal under <paramref name="comparer"/> (or the default comparer). Every cell receives a label
        /// (1..), so this partitions the whole grid. (Named distinctly from <see cref="Label(bool[,], Connectivity)"/>
        /// so that passing a <c>bool[,]</c> is unambiguous.)
        /// </summary>
        public static ComponentLabeling LabelByValue<T>(
            T[,] grid, Connectivity connectivity = Connectivity.Four, IEqualityComparer<T>? comparer = null)
        {
            if (grid is null) throw new ArgumentNullException(nameof(grid));
            int rows = grid.GetLength(0), cols = grid.GetLength(1);
            comparer ??= EqualityComparer<T>.Default;
            return LabelCore(rows, cols, connectivity, foregroundOnly: false,
                belongs: (r, c) => true,
                sameRegion: (r1, c1, r2, c2) => comparer.Equals(grid[r1, c1], grid[r2, c2]));
        }

        private static ComponentLabeling LabelCore(
            int rows, int cols, Connectivity connectivity, bool foregroundOnly,
            Func<int, int, bool> belongs, Func<int, int, int, int, bool> sameRegion)
        {
            var labels = new int[rows, cols];
            var components = new List<GridComponent>();
            int dirs = connectivity == Connectivity.Eight ? 8 : 4;
            var queue = new Queue<Cell>();

            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    if (labels[r, c] != 0) continue;
                    if (foregroundOnly && !belongs(r, c)) continue;

                    int label = components.Count + 1;
                    var cells = new List<Cell>();
                    int minRow = r, minCol = c, maxRow = r, maxCol = c;

                    labels[r, c] = label;
                    queue.Enqueue(new Cell(r, c));
                    while (queue.Count > 0)
                    {
                        Cell cell = queue.Dequeue();
                        cells.Add(cell);
                        if (cell.Row < minRow) minRow = cell.Row;
                        if (cell.Row > maxRow) maxRow = cell.Row;
                        if (cell.Col < minCol) minCol = cell.Col;
                        if (cell.Col > maxCol) maxCol = cell.Col;

                        for (int d = 0; d < dirs; d++)
                        {
                            int nr = cell.Row + DRow[d];
                            int nc = cell.Col + DCol[d];
                            if ((uint)nr >= (uint)rows || (uint)nc >= (uint)cols) continue;
                            if (labels[nr, nc] != 0) continue;
                            if (foregroundOnly && !belongs(nr, nc)) continue;
                            if (!sameRegion(cell.Row, cell.Col, nr, nc)) continue;
                            labels[nr, nc] = label;
                            queue.Enqueue(new Cell(nr, nc));
                        }
                    }
                    components.Add(new GridComponent(label, cells, minRow, minCol, maxRow, maxCol));
                }
            }
            return new ComponentLabeling(labels, components);
        }

        // The first four entries are the orthogonal neighbours; the last four are the diagonals.
        private static readonly int[] DRow = { -1, 1, 0, 0, -1, -1, 1, 1 };
        private static readonly int[] DCol = { 0, 0, -1, 1, -1, 1, -1, 1 };
    }
}
