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

            double sxx = 0, syy = 0, sxy = 0;
            for (int i = 0; i < n; i++)
            {
                double dx = xs[i] - meanX;
                double dy = ys[i] - meanY;
                sxx += dx * dx;
                syy += dy * dy;
                sxy += dx * dy;
            }

            if (sxx == 0 || syy == 0)
                return double.NaN; // a constant variable has no linear correlation

            // Separate roots: sxx·syy over- or underflows for data near 1e±80.
            double r = sxy / Math.Sqrt(sxx) / Math.Sqrt(syy);
            // Clamp tiny floating-point excursions past the mathematical bounds.
            if (r > 1) r = 1;
            else if (r < -1) r = -1;
            return r;
        }
    }
}
