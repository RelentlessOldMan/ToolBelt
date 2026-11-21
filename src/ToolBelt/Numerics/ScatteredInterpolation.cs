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
            // A NaN or infinite point came back NaN, which here means "no samples within the radius".
            RequireFinite(x, nameof(x));
            RequireFinite(y, nameof(y));
            return Evaluate(samples, x, y, power, nearest, radius, new List<(double, bool, double)>());
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
            RequireFinite(xMin, nameof(xMin));
            RequireFinite(xMax, nameof(xMax));
            RequireFinite(yMin, nameof(yMin));
            RequireFinite(yMax, nameof(yMax));
            if ((long)rows * columns > 0x7FFFFFC7) // Array.MaxLength: past it the allocation threw OutOfMemoryException
                throw new ArgumentOutOfRangeException(nameof(rows), rows, $"A {rows} × {columns} grid exceeds Array.MaxLength (0x7FFFFFC7) cells.");
            var grid = new double[rows, columns];
            var scratch = new List<(double, bool, double)>(samples.Count);
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
            double power, int? nearest, double? radius, List<(double D, bool Far, double V)> scratch)
        {
            // Distances, not squared distances: d² over- or underflowed (1e200 away, or 1e-160), and the weights 1/d^p
            // then became ∞/∞ or 0/0. A distance beyond the double range (1e308 to −1e308) is taken in quarters and marked
            // far; the others stay as they are. (Redoing every distance in quarters rounded distinct subnormal distances
            // to 0, and samples ε and 2ε away were then averaged as equally near: 6.5 for 4.4.) A far one is beyond any
            // finite radius.
            scratch.Clear();
            foreach (var s in samples)
            {
                if (s.X == x && s.Y == y) return s.Value; // exact at a sample (first one wins for duplicates)
                double d = Hypot(s.X - x, s.Y - y);
                bool far = double.IsInfinity(d);
                if (far) d = Hypot(0.25 * s.X - 0.25 * x, 0.25 * s.Y - 0.25 * y);
                // An infinite radius keeps every sample, far ones too (it dropped those past double.MaxValue, and gave NaN).
                if (radius is double r && !double.IsPositiveInfinity(r) && (far || d > r)) continue;
                scratch.Add((d, far, s.Value));
            }
            return Weighted(scratch, power, nearest);
        }

        private static double Weighted(List<(double D, bool Far, double V)> scratch, double power, int? nearest)
        {
            if (scratch.Count == 0) return double.NaN;
            // Nearest first: every near distance is below every far one.
            if (nearest is int k && k < scratch.Count)
            {
                scratch.Sort((a, b) => a.Far != b.Far ? (a.Far ? 1 : -1) : a.D.CompareTo(b.D));
                scratch.RemoveRange(k, scratch.Count - k);
            }
            double minDistance = double.PositiveInfinity;
            bool minFar = true;
            foreach (var (d, far, _) in scratch)
                if (far == minFar ? d < minDistance : !far) { minDistance = d; minFar = far; }
            // Weights relative to the nearest sample, (dmin/d)^p in (0, 1]: same ratios, no overflow.
            double num = 0, den = 0;
            foreach (var (d, far, v) in scratch)
            {
                double w = Weight(minDistance, minFar, d, far, power);
                num += w * v;
                den += w;
            }
            if (double.IsInfinity(num))
            {
                // Values near double.MaxValue overflowed the sum: average them divided by the count, then scale back.
                num = 0;
                foreach (var (d, far, v) in scratch) num += Weight(minDistance, minFar, d, far, power) * (v / scratch.Count);
                return Clamp(num / den * scratch.Count, scratch);
            }
            return Clamp(num / den, scratch);
        }

        // (dmin/d)^p, with d in quarters when far. A ratio that underflows is taken through logarithms: with a small
        // power it is still a real weight ((1e-330)^0.001 is 0.47, not 0).
        private static double Weight(double minDistance, bool minFar, double d, bool far, double power)
        {
            double r = minDistance / d;
            if (far && !minFar) r *= 0.25;
            if (r >= 2.2250738585072014e-308) return Math.Pow(r, power);
            double log = Math.Log(minDistance) - Math.Log(d) - (far && !minFar ? Math.Log(4) : 0);
            return Math.Exp(power * log);
        }

        // Within the range of the samples used, as promised: rounding left equal values of 0.1 at 0.09999999999999999.
        private static double Clamp(double value, List<(double D, bool Far, double V)> scratch)
        {
            double lo = double.PositiveInfinity, hi = double.NegativeInfinity;
            foreach (var (_, _, v) in scratch) { if (v < lo) lo = v; if (v > hi) hi = v; }
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

        private static void RequireFinite(double v, string paramName)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) throw new ArgumentOutOfRangeException(paramName, v, "Must be finite.");
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
