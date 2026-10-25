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
            double plain = min + (max - min) * i / (n - 1);
            // Not-finite, not just infinite: at i = 0 an overflowed span gave ∞ · 0 = NaN.
            if ((double.IsNaN(plain) || double.IsInfinity(plain)) && !double.IsInfinity(min) && !double.IsInfinity(max))
                return 2 * (0.5 * min + (0.5 * max - 0.5 * min) * ((double)i / (n - 1)));
            return plain;
        }

        private static double Evaluate(IReadOnlyList<(double X, double Y, double Value)> samples, double x, double y,
            double power, int? nearest, double? radius, List<(double D, double V)> scratch)
        {
            scratch.Clear();
            double maxDistance = radius is double rr ? rr : double.PositiveInfinity;
            double minDistance = double.PositiveInfinity;
            foreach (var s in samples)
            {
                // Distances, not squared distances: d² over- or underflowed (1e200 away, or 1e-160), and the
                // weights 1/d^p then became ∞/∞ or 0/0.
                double d = Hypot(s.X - x, s.Y - y);
                if (d == 0) return s.Value; // exact at a sample (first one wins for duplicates)
                if (d <= maxDistance)
                {
                    scratch.Add((d, s.Value));
                    if (d < minDistance) minDistance = d;
                }
            }
            if (scratch.Count == 0) return double.NaN;
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
            return num / den;
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
                if (double.IsNaN(s.X) || double.IsNaN(s.Y) || double.IsNaN(s.Value)) throw new ArgumentException("Samples must not contain NaN.", nameof(samples));
        }
    }
}
