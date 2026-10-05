// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;

namespace ToolBelt.Numerics
{
    /// <summary>
    /// Interpolation of scattered 2-D samples (x, y, value) — readings taken wherever a probe happened to be — at
    /// arbitrary points or onto a regular grid for a heat map, using inverse-distance weighting (Shepard's method):
    /// value = Σ wᵢvᵢ / Σ wᵢ with wᵢ = 1/dᵢᵖ. Exact at the sample points, never outside the range of the samples (so it
    /// cannot invent extremes), and smooth for p ≥ 2. Optionally restricted to the <c>nearest</c> k samples and/or a
    /// search <c>radius</c> (beyond which a cell gets NaN — "no data here"). Cost is O(samples) per query point.
    /// </summary>
    public static class ScatteredInterpolation
    {
        /// <summary>The inverse-distance-weighted value at (<paramref name="x"/>, <paramref name="y"/>).</summary>
        public static double InverseDistance(IReadOnlyList<(double X, double Y, double Value)> samples, double x, double y,
            double power = 2, int? nearest = null, double? radius = null)
        {
            Validate(samples, power, nearest, radius);
            return Evaluate(samples, x, y, power, nearest, radius, new List<(double, double)>());
        }

        /// <summary>
        /// Evaluates on a <paramref name="rows"/> × <paramref name="columns"/> grid spanning the given ranges (cell centres
        /// include both ends). Row 0 is <paramref name="yMin"/> and column 0 is <paramref name="xMin"/>; flip rows to draw with
        /// y increasing upward.
        /// </summary>
        public static double[,] ToGrid(IReadOnlyList<(double X, double Y, double Value)> samples,
            double xMin, double xMax, int columns, double yMin, double yMax, int rows,
            double power = 2, int? nearest = null, double? radius = null)
        {
            Validate(samples, power, nearest, radius);
            if (columns < 1 || rows < 1) throw new ArgumentOutOfRangeException(nameof(rows), "The grid needs at least one row and column.");
            var grid = new double[rows, columns];
            var scratch = new List<(double, double)>(samples.Count);
            for (int r = 0; r < rows; r++)
            {
                double y = rows == 1 ? (yMin + yMax) / 2 : yMin + (yMax - yMin) * r / (rows - 1);
                for (int c = 0; c < columns; c++)
                {
                    double x = columns == 1 ? (xMin + xMax) / 2 : xMin + (xMax - xMin) * c / (columns - 1);
                    grid[r, c] = Evaluate(samples, x, y, power, nearest, radius, scratch);
                }
            }
            return grid;
        }

        private static double Evaluate(IReadOnlyList<(double X, double Y, double Value)> samples, double x, double y,
            double power, int? nearest, double? radius, List<(double D2, double V)> scratch)
        {
            scratch.Clear();
            double r2 = radius is double rr ? rr * rr : double.PositiveInfinity;
            foreach (var s in samples)
            {
                double dx = s.X - x, dy = s.Y - y, d2 = dx * dx + dy * dy;
                if (d2 == 0) return s.Value; // exact at a sample (first one wins for duplicates)
                if (d2 <= r2) scratch.Add((d2, s.Value));
            }
            if (scratch.Count == 0) return double.NaN;
            if (nearest is int k && k < scratch.Count)
            {
                scratch.Sort((a, b) => a.D2.CompareTo(b.D2));
                scratch.RemoveRange(k, scratch.Count - k);
            }
            double num = 0, den = 0, half = power / 2;
            foreach (var (d2, v) in scratch)
            {
                double w = 1 / Math.Pow(d2, half);
                num += w * v;
                den += w;
            }
            return num / den;
        }

        private static void Validate(IReadOnlyList<(double X, double Y, double Value)> samples, double power, int? nearest, double? radius)
        {
            if (samples is null) throw new ArgumentNullException(nameof(samples));
            if (samples.Count == 0) throw new ArgumentException("Need at least one sample.", nameof(samples));
            if (!(power > 0) || double.IsInfinity(power)) throw new ArgumentOutOfRangeException(nameof(power), power, "Power must be positive.");
            if (nearest is int k && k < 1) throw new ArgumentOutOfRangeException(nameof(nearest), k, "Must be at least 1.");
            if (radius is double r && !(r > 0)) throw new ArgumentOutOfRangeException(nameof(radius), r, "Radius must be positive.");
            foreach (var s in samples)
                if (double.IsNaN(s.X) || double.IsNaN(s.Y) || double.IsNaN(s.Value)) throw new ArgumentException("Samples must not contain NaN.", nameof(samples));
        }
    }
}
