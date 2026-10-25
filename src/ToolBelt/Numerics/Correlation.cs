// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;

namespace ToolBelt.Numerics
{
    /// <summary>
    /// Correlation between paired samples. <see cref="Pearson"/> returns the Pearson correlation
    /// coefficient in [-1, 1] (1 = perfect positive linear relationship, -1 = perfect negative, 0 = none).
    /// If either variable is constant the coefficient is undefined and <see cref="double.NaN"/> is
    /// returned. Values must be finite.
    /// </summary>
    public static class Correlation
    {
        public static double Pearson(IReadOnlyList<double> xs, IReadOnlyList<double> ys)
        {
            if (xs is null) throw new ArgumentNullException(nameof(xs));
            if (ys is null) throw new ArgumentNullException(nameof(ys));
            if (xs.Count != ys.Count)
                throw new ArgumentException("xs and ys must have the same length.", nameof(ys));
            int n = xs.Count;
            if (n < 2)
                throw new ArgumentException("At least two points are required.", nameof(xs));

            // Means about the first point: a constant variable then has exactly that mean and zero spread, instead of
            // round-off crumbs (0.1 + 0.1 + 0.1 ≠ 0.3) that read as a real, tiny variance.
            double x0 = xs[0], y0 = ys[0], shiftX = 0, shiftY = 0;
            for (int i = 0; i < n; i++)
            {
                if (double.IsNaN(xs[i]) || double.IsInfinity(xs[i])) throw new ArgumentException($"Value [{i}] is not finite.", nameof(xs));
                if (double.IsNaN(ys[i]) || double.IsInfinity(ys[i])) throw new ArgumentException($"Value [{i}] is not finite.", nameof(ys));
                shiftX += xs[i] - x0;
                shiftY += ys[i] - y0;
            }
            double meanX = x0 + shiftX / n, meanY = y0 + shiftY / n;

            // Deviations relative to each variable's largest: squared outright they underflowed to zero (or overflowed)
            // for data near 1e±160, reading a perfect correlation as NaN.
            double maxX = 0, maxY = 0;
            for (int i = 0; i < n; i++)
            {
                maxX = Math.Max(maxX, Math.Abs(xs[i] - meanX));
                maxY = Math.Max(maxY, Math.Abs(ys[i] - meanY));
            }
            double sx = Pow2Scale(maxX), sy = Pow2Scale(maxY);

            double sxx = 0, syy = 0, sxy = 0;
            for (int i = 0; i < n; i++)
            {
                double dx = (xs[i] - meanX) / sx;
                double dy = (ys[i] - meanY) / sy;
                sxx += dx * dx;
                syy += dy * dy;
                sxy += dx * dy;
            }

            if (sxx == 0 || syy == 0)
                return double.NaN; // a constant variable has no linear correlation

            double r = sxy / Math.Sqrt(sxx * syy);   // scaled, so the product neither over- nor underflows
            // Clamp tiny floating-point excursions past the mathematical bounds.
            if (r > 1) r = 1;
            else if (r < -1) r = -1;
            return r;
        }

        // The power of two at or below m (1 for 0): dividing by it is exact, so data in the normal range give the same
        // bits, while data near 1e±160 no longer over- or underflow when squared.
        private static double Pow2Scale(double m)
        {
            if (m == 0 || double.IsInfinity(m)) return 1;
            double p = Math.Pow(2, Math.Floor(Math.Log(m) / Math.Log(2)));
            // The logarithm can land a hair either side of an integer; settle on exactly the power at or below m.
            while (p > m) p /= 2;
            while (p * 2 <= m) p *= 2;
            return p;
        }
    }
}
