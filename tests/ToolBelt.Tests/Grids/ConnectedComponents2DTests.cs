using System;
using System.Collections.Generic;
using ToolBelt.Collections;
using ToolBelt.Grids;
using ToolBelt.Numerics;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Grids
{
    public sealed class ConnectedComponents2DTests
    {
        public void Mask_AllTrue_SingleComponent()
        {
            var mask = new bool[3, 4];
            for (int r = 0; r < 3; r++)
                for (int c = 0; c < 4; c++)
                    mask[r, c] = true;

            var result = ConnectedComponents2D.Label(mask);
            Check.Equal(1, result.Count);
            Check.Equal(12, result.Components[0].Size);
            Check.Equal(0, result.Components[0].MinRow);
            Check.Equal(2, result.Components[0].MaxRow);
            Check.Equal(3, result.Components[0].MaxCol);
            Check.Equal(3, result.Components[0].Height);
            Check.Equal(4, result.Components[0].Width);
        }

        public void Mask_AllFalse_NoComponents()
        {
            var result = ConnectedComponents2D.Label(new bool[3, 3]);
            Check.Equal(0, result.Count);
            for (int r = 0; r < 3; r++)
                for (int c = 0; c < 3; c++)
                    Check.Equal(0, result.Labels[r, c]);
        }

        public void Mask_Checkerboard_ConnectivityMatters()
        {
            // true where (r + c) is even, on a 3x3 -> 5 true cells.
            var mask = new bool[3, 3];
            int trueCount = 0;
            for (int r = 0; r < 3; r++)
                for (int c = 0; c < 3; c++)
                    if (((r + c) & 1) == 0) { mask[r, c] = true; trueCount++; }

            // 4-connected: no two true cells are orthogonally adjacent -> each its own component.
            Check.Equal(trueCount, ConnectedComponents2D.Label(mask, Connectivity.Four).Count);
            // 8-connected: diagonals join them all into one.
            Check.Equal(1, ConnectedComponents2D.Label(mask, Connectivity.Eight).Count);
        }

        public void ValueLabel_PartitionsWholeGrid()
        {
            // Two vertical stripes of distinct values -> two components, every cell labelled.
            var grid = new int[2, 2] { { 1, 2 }, { 1, 2 } };
            var result = ConnectedComponents2D.LabelByValue(grid);
            Check.Equal(2, result.Count);
            Check.True(result.Labels[0, 0] == result.Labels[1, 0], "left column same component");
            Check.True(result.Labels[0, 1] == result.Labels[1, 1], "right column same component");
            Check.True(result.Labels[0, 0] != result.Labels[0, 1], "columns differ");
        }

        public void Validation_Throws()
        {
            Check.Throws<ArgumentNullException>(() => ConnectedComponents2D.Label((bool[,])null!));
            Check.Throws<ArgumentNullException>(() => ConnectedComponents2D.LabelByValue((int[,])null!));
        }

        public void Mask_DifferentialVsUnionFind()
        {
            var rng = new DeterministicRandom(99887766);
            foreach (var connectivity in new[] { Connectivity.Four, Connectivity.Eight })
            {
                for (int trial = 0; trial < 300; trial++)
                {
                    int rows = rng.Next(1, 9), cols = rng.Next(1, 9);
                    var mask = new bool[rows, cols];
                    for (int r = 0; r < rows; r++)
                        for (int c = 0; c < cols; c++)
                            mask[r, c] = rng.NextDouble() < 0.55;

                    var result = ConnectedComponents2D.Label(mask, connectivity);
                    CheckAgainstUnionFind(rows, cols, (r, c) => mask[r, c], foreground: true, connectivity, result);
                }
            }
        }

        public void ValueLabel_DifferentialVsUnionFind()
        {
            var rng = new DeterministicRandom(1222333);
            foreach (var connectivity in new[] { Connectivity.Four, Connectivity.Eight })
            {
                for (int trial = 0; trial < 300; trial++)
                {
                    int rows = rng.Next(1, 9), cols = rng.Next(1, 9);
                    var grid = new int[rows, cols];
                    for (int r = 0; r < rows; r++)
                        for (int c = 0; c < cols; c++)
                            grid[r, c] = rng.Next(0, 3);

                    var result = ConnectedComponents2D.LabelByValue(grid, connectivity);
                    CheckValueAgainstUnionFind(grid, connectivity, result);
                }
            }
        }

        // Builds the ground-truth partition with DisjointSet and checks count + label consistency.
        private static void CheckAgainstUnionFind(
            int rows, int cols, Func<int, int, bool> fg, bool foreground, Connectivity connectivity, ComponentLabeling result)
        {
            var dsu = new DisjointSet(rows * cols);
            int[] dr = connectivity == Connectivity.Eight ? new[] { -1, 1, 0, 0, -1, -1, 1, 1 } : new[] { -1, 1, 0, 0 };
            int[] dc = connectivity == Connectivity.Eight ? new[] { 0, 0, -1, 1, -1, 1, -1, 1 } : new[] { 0, 0, -1, 1 };

            int fgCount = 0;
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                {
                    if (!fg(r, c)) continue;
                    fgCount++;
                    for (int d = 0; d < dr.Length; d++)
                    {
                        int nr = r + dr[d], nc = c + dc[d];
                        if ((uint)nr >= (uint)rows || (uint)nc >= (uint)cols) continue;
                        if (!fg(nr, nc)) continue;
                        dsu.Union(r * cols + c, nr * cols + nc);
                    }
                }

            // Expected component count = distinct roots among foreground cells.
            var roots = new HashSet<int>();
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                    if (fg(r, c))
                        roots.Add(dsu.Find(r * cols + c));
            Check.Equal(roots.Count, result.Count);

            // Label consistency: two foreground cells share a label iff they share a DSU root.
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                {
                    if (!fg(r, c)) { Check.Equal(0, result.Labels[r, c]); continue; }
                    for (int r2 = 0; r2 < rows; r2++)
                        for (int c2 = 0; c2 < cols; c2++)
                        {
                            if (!fg(r2, c2)) continue;
                            bool sameRoot = dsu.Find(r * cols + c) == dsu.Find(r2 * cols + c2);
                            bool sameLabel = result.Labels[r, c] == result.Labels[r2, c2];
                            Check.True(sameRoot == sameLabel, $"label/root mismatch at ({r},{c}) vs ({r2},{c2})");
                        }
                }
        }

        private static void CheckValueAgainstUnionFind(int[,] grid, Connectivity connectivity, ComponentLabeling result)
        {
            int rows = grid.GetLength(0), cols = grid.GetLength(1);
            var dsu = new DisjointSet(rows * cols);
            int[] dr = connectivity == Connectivity.Eight ? new[] { -1, 1, 0, 0, -1, -1, 1, 1 } : new[] { -1, 1, 0, 0 };
            int[] dc = connectivity == Connectivity.Eight ? new[] { 0, 0, -1, 1, -1, 1, -1, 1 } : new[] { 0, 0, -1, 1 };

            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                    for (int d = 0; d < dr.Length; d++)
                    {
                        int nr = r + dr[d], nc = c + dc[d];
                        if ((uint)nr >= (uint)rows || (uint)nc >= (uint)cols) continue;
                        if (grid[nr, nc] != grid[r, c]) continue;
                        dsu.Union(r * cols + c, nr * cols + nc);
                    }

            var roots = new HashSet<int>();
            for (int i = 0; i < rows * cols; i++)
                roots.Add(dsu.Find(i));
            Check.Equal(roots.Count, result.Count);

            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                {
                    Check.True(result.Labels[r, c] >= 1, "value labeling covers every cell");
                    for (int r2 = 0; r2 < rows; r2++)
                        for (int c2 = 0; c2 < cols; c2++)
                        {
                            bool sameRoot = dsu.Find(r * cols + c) == dsu.Find(r2 * cols + c2);
                            bool sameLabel = result.Labels[r, c] == result.Labels[r2, c2];
                            Check.True(sameRoot == sameLabel, $"label/root mismatch at ({r},{c}) vs ({r2},{c2})");
                        }
                }
        }
    }
}
