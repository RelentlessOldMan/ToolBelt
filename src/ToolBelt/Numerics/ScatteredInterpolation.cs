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
            if (columns < 1) throw new ArgumentOutOfRangeException(nameof(columns), columns, "The grid needs at least one column.");
            if (rows < 1) throw new ArgumentOutOfRangeException(nameof(rows), rows, "The grid needs at least one row.");
            if ((long)rows * columns > 0x7FFFFFC7) // Array.MaxLength: past it the allocation threw OutOfMemoryException
                throw new ArgumentOutOfRangeException(nameof(rows), rows, $"A {rows} × {columns} grid exceeds Array.MaxLength (0x7FFFFFC7) cells.");
            var grid = new double[rows, columns];
            var scratch = new List<(double, double)>(samples.Count);
            for (int r = 0; r < rows; r++)
            {
                double y = GridCoordinate(yMin, yMax, r, rows);
                for (int c = 0; c < columns; c++)
                {
                    double x = GridCoordinate(xMin, xMax, c, columns);
                    grid[r, c] = Evaluate(samples, x, y, power, nearest, radius, scratch);
                }
            }
            return grid;
        }

        // The i-th of n evenly spaced points from min to max (the midpoint when n is 1). A span, sum or span·i that
        // overflows (−1e308 to 1e308, 1e308 + 1e308) is redone with halved ends and the fraction i/(n − 1) taken
        // first; otherwise the plain form is kept.
        private static double GridCoordinate(double min, double max, int i, int n)
        {
            if (n == 1)
            {
                double mid = (min + max) / 2;
                return double.IsInfinity(mid) && !double.IsInfinity(min) && !double.IsInfinity(max) ? 0.5 * min + 0.5 * max : mid;
            }
            // The ends exactly: min + (max − min) could round past max (0.1 to 0.7 ended at 0.7000000000000001).
            if (i == 0) return min;
            if (i == n - 1) return max;
            double plain = min + (max - min) * i / (n - 1);
            // Not-finite, not just infinite: at i = 0 an overflowed span gave ∞ · 0 = NaN.
            if ((double.IsNaN(plain) || double.IsInfinity(plain)) && !double.IsInfinity(min) && !double.IsInfinity(max))
                return 2 * (0.5 * min + (0.5 * max - 0.5 * min) * ((double)i / (n - 1)));
            return plain;
        }

        private static double Evaluate(IReadOnlyList<(double X, double Y, double Value)> samples, double x, double y,
            double power, int? nearest, double? radius, List<(double D, double V)> scratch)
        {
            // Distances, not squared distances: d² over- or underflowed (1e200 away, or 1e-160), and the weights 1/d^p
            // then became ∞/∞ or 0/0. A distance beyond the double range (1e308 to −1e308) redoes them all in quarters,
            // which only scales the weights' common factor: when every one overflowed the result was ∞/∞ = NaN.
            double scale = 1;
            for (int pass = 0; ; pass++)
            {
                scratch.Clear();
                double maxDistance = radius is double rr ? rr * scale : double.PositiveInfinity;
                double minDistance = double.PositiveInfinity;
                bool overflowed = false;
                foreach (var s in samples)
                {
                    if (s.X == x && s.Y == y) return s.Value; // exact at a sample (first one wins for duplicates)
                    double d = Hypot(scale * s.X - scale * x, scale * s.Y - scale * y);
                    if (double.IsInfinity(d)) { overflowed = true; break; }
                    if (d <= maxDistance)
                    {
                        scratch.Add((d, s.Value));
                        if (d < minDistance) minDistance = d;
                    }
                }
                if (overflowed && pass == 0) { scale = 0.25; continue; }
                return Weighted(scratch, minDistance, power, nearest);
            }
        }

        private static double Weighted(List<(double D, double V)> scratch, double minDistance, double power, int? nearest)
        {
            if (scratch.Count == 0) return double.NaN;
            if (minDistance == 0) // distances that underflowed to 0 in quarters: only those nearest count
            {
                double sum = 0; int count = 0;
                foreach (var (d, v) in scratch) if (d == 0) { sum += v / scratch.Count; count++; }
                return Clamp(sum / count * scratch.Count, scratch);
            }
            if (nearest is int k && k < scratch.Count)
            {
                scratch.Sort((a, b) => a.D.CompareTo(b.D));
                scratch.RemoveRange(k, scratch.Count - k);
            }
            // Weights relative to the nearest sample, (dmin/d)^p in (0, 1]: same ratios, no overflow.
            double num = 0, den = 0;
            foreach (var (d, v) in scratch)
            {
                double w = Math.Pow(minDistance / d, power);
                num += w * v;
                den += w;
            }
            if (double.IsInfinity(num))
            {
                // Values near double.MaxValue overflowed the sum: average them divided by the count, then scale back.
                num = 0;
                foreach (var (d, v) in scratch) num += Math.Pow(minDistance / d, power) * (v / scratch.Count);
                return Clamp(num / den * scratch.Count, scratch);
            }
            return Clamp(num / den, scratch);
        }

        // Within the range of the samples used, as promised: rounding left equal values of 0.1 at 0.09999999999999999.
        private static double Clamp(double value, List<(double D, double V)> scratch)
        {
            double lo = double.PositiveInfinity, hi = double.NegativeInfinity;
            foreach (var (_, v) in scratch) { if (v < lo) lo = v; if (v > hi) hi = v; }
            return value < lo ? lo : value > hi ? hi : value;
        }

        private static double Hypot(double a, double b)
        {
            a = Math.Abs(a); b = Math.Abs(b);
            if (a < b) { double t = a; a = b; b = t; }
            if (a == 0 || double.IsInfinity(a)) return a;
            double r = b / a;
            return a * Math.Sqrt(1 + r * r);
        }

        private static void Validate(IReadOnlyList<(double X, double Y, double Value)> samples, double power, int? nearest, double? radius)
        {
            if (samples is null) throw new ArgumentNullException(nameof(samples));
            if (samples.Count == 0) throw new ArgumentException("Need at least one sample.", nameof(samples));
            if (!(power > 0) || double.IsInfinity(power)) throw new ArgumentOutOfRangeException(nameof(power), power, "Power must be positive.");
            if (nearest is int k && k < 1) throw new ArgumentOutOfRangeException(nameof(nearest), k, "Must be at least 1.");
            if (radius is double r && !(r > 0)) throw new ArgumentOutOfRangeException(nameof(radius), r, "Radius must be positive.");
            foreach (var s in samples)
            {
                if (double.IsNaN(s.X) || double.IsNaN(s.Y) || double.IsNaN(s.Value)) throw new ArgumentException("Samples must not contain NaN.", nameof(samples));
                if (double.IsInfinity(s.X) || double.IsInfinity(s.Y)) throw new ArgumentException("Sample coordinates must be finite.", nameof(samples));
                // An infinite value made every weighted mean ∞ or ∞ − ∞ = NaN, far from it as well.
                if (double.IsInfinity(s.Value)) throw new ArgumentException("Sample values must be finite.", nameof(samples));
            }
        }
    }
}
