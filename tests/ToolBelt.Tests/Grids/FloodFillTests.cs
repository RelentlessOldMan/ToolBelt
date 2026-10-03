using System;
using System.Collections.Generic;
using ToolBelt.Grids;
using ToolBelt.Numerics;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Grids
{
    public sealed class FloodFillTests
    {
        public void Fill_UniformGrid_FillsEverything()
        {
            var grid = new int[3, 3]; // all zero
            int filled = FloodFill.Fill(grid, 1, 1, 7);
            Check.Equal(9, filled);
            for (int r = 0; r < 3; r++)
                for (int c = 0; c < 3; c++)
                    Check.Equal(7, grid[r, c]);
        }

        public void Fill_SameValue_IsNoOp()
        {
            var grid = new int[2, 2] { { 5, 5 }, { 5, 5 } };
            int filled = FloodFill.Fill(grid, 0, 0, 5);
            Check.Equal(0, filled);
            for (int r = 0; r < 2; r++)
                for (int c = 0; c < 2; c++)
                    Check.Equal(5, grid[r, c]);
        }

        public void Fill_RespectsBoundary()
        {
            // Left column 1, right column 0 — filling the left region must not cross into the right.
            var grid = new int[2, 2] { { 1, 0 }, { 1, 0 } };
            int filled = FloodFill.Fill(grid, 0, 0, 9);
            Check.Equal(2, filled);
            Check.Equal(9, grid[0, 0]);
            Check.Equal(9, grid[1, 0]);
            Check.Equal(0, grid[0, 1]);
            Check.Equal(0, grid[1, 1]);
        }

        public void Region_FourVsEight_DiagonalConnectivity()
        {
            // Two 1-cells touching only at a diagonal.
            var grid = new int[2, 2] { { 1, 0 }, { 0, 1 } };
            Check.Equal(1, FloodFill.Region(grid, 0, 0, Connectivity.Four).Count);
            Check.Equal(2, FloodFill.Region(grid, 0, 0, Connectivity.Eight).Count);
        }

        public void PredicateRegion_SeedMustMatch()
        {
            // belongs is false at the seed -> empty region.
            var empty = FloodFill.Region(3, 3, 0, 0, (r, c) => false);
            Check.Equal(0, empty.Count);

            // Whole grid belongs -> every cell, 4-connected.
            var all = FloodFill.Region(3, 3, 1, 1, (r, c) => true);
            Check.Equal(9, all.Count);
        }

        public void Validation_Throws()
        {
            var grid = new int[2, 2];
            Check.Throws<ArgumentNullException>(() => FloodFill.Fill<int>(null!, 0, 0, 1));
            Check.Throws<ArgumentNullException>(() => FloodFill.Region<int>(null!, 0, 0));
            Check.Throws<ArgumentOutOfRangeException>(() => FloodFill.Region(grid, 2, 0));
            Check.Throws<ArgumentOutOfRangeException>(() => FloodFill.Region(grid, 0, -1));
            Check.Throws<ArgumentNullException>(() => FloodFill.Region(2, 2, 0, 0, null!));
        }

        public void Region_DifferentialVsNaiveRelaxation()
        {
            var rng = new DeterministicRandom(20260303);
            foreach (var connectivity in new[] { Connectivity.Four, Connectivity.Eight })
            {
                for (int trial = 0; trial < 400; trial++)
                {
                    int rows = rng.Next(1, 9), cols = rng.Next(1, 9);
                    var grid = new int[rows, cols];
                    for (int r = 0; r < rows; r++)
                        for (int c = 0; c < cols; c++)
                            grid[r, c] = rng.Next(0, 3); // small palette -> real regions

                    int sr = rng.Next(0, rows), sc = rng.Next(0, cols);
                    var actual = ToSet(FloodFill.Region(grid, sr, sc, connectivity));
                    var expected = NaiveRegion(grid, sr, sc, connectivity == Connectivity.Eight);
                    Check.True(actual.SetEquals(expected),
                        $"trial {trial} ({connectivity}) seed ({sr},{sc}): expected {expected.Count}, got {actual.Count}");
                }
            }
        }

        private static HashSet<(int, int)> ToSet(IReadOnlyList<Cell> cells)
        {
            var set = new HashSet<(int, int)>();
            foreach (var cell in cells)
                set.Add((cell.Row, cell.Col));
            return set;
        }

        // Reference: repeatedly relax the whole grid, growing the region one ring per pass until stable.
        private static HashSet<(int, int)> NaiveRegion(int[,] grid, int sr, int sc, bool eight)
        {
            int rows = grid.GetLength(0), cols = grid.GetLength(1);
            int target = grid[sr, sc];
            var inRegion = new bool[rows, cols];
            inRegion[sr, sc] = true;
            int[] dr = eight ? new[] { -1, 1, 0, 0, -1, -1, 1, 1 } : new[] { -1, 1, 0, 0 };
            int[] dc = eight ? new[] { 0, 0, -1, 1, -1, 1, -1, 1 } : new[] { 0, 0, -1, 1 };

            bool changed = true;
            while (changed)
            {
                changed = false;
                for (int r = 0; r < rows; r++)
                    for (int c = 0; c < cols; c++)
                    {
                        if (!inRegion[r, c]) continue;
                        for (int d = 0; d < dr.Length; d++)
                        {
                            int nr = r + dr[d], nc = c + dc[d];
                            if ((uint)nr >= (uint)rows || (uint)nc >= (uint)cols) continue;
                            if (inRegion[nr, nc]) continue;
                            if (grid[nr, nc] != target) continue;
                            inRegion[nr, nc] = true;
                            changed = true;
                        }
                    }
            }

            var set = new HashSet<(int, int)>();
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                    if (inRegion[r, c])
                        set.Add((r, c));
            return set;
        }
    }
}
