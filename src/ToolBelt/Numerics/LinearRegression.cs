// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;

namespace ToolBelt.Numerics
{
    /// <summary>The result of an ordinary-least-squares fit: <c>y = Slope·x + Intercept</c>.</summary>
    public readonly struct LinearFit
    {
        public LinearFit(double slope, double intercept, double rSquared)
        {
            Slope = slope;
            Intercept = intercept;
            RSquared = rSquared;
        }

        /// <summary>The fitted slope.</summary>
        public double Slope { get; }

        /// <summary>The fitted y-intercept.</summary>
        public double Intercept { get; }

        /// <summary>Coefficient of determination (R²) in [0, 1]; 1 means a perfect fit.</summary>
        public double RSquared { get; }

        /// <summary>Predicts y for a given x from the fitted line.</summary>
        public double Predict(double x) => Slope * x + Intercept;
    }

    /// <summary>
    /// Ordinary least-squares linear regression of paired (x, y) samples. Requires at least two points
    /// and non-constant x values (otherwise the slope is undefined); values must be finite.
    /// </summary>
    public static class LinearRegression
    {
        public static LinearFit Fit(IReadOnlyList<double> xs, IReadOnlyList<double> ys)
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

            double sxx = 0, sxy = 0, syy = 0;
            for (int i = 0; i < n; i++)
            {
                double dx = xs[i] - meanX;
                double dy = ys[i] - meanY;
                sxx += dx * dx;
                sxy += dx * dy;
                syy += dy * dy;
            }

            if (sxx == 0)
                throw new ArgumentException("All x values are identical; the slope is undefined.", nameof(xs));

            double slope = sxy / sxx;
            double intercept = meanY - slope * meanX;
            // R² = explained variance / total variance. If y is constant (syy == 0) the fit is exact.
            // Formed as r² with separate roots: sxy² and sxx·syy over- or underflow for data near 1e±80.
            double r = syy == 0 ? 1.0 : sxy / Math.Sqrt(sxx) / Math.Sqrt(syy);
            double rSquared = Math.Min(1.0, r * r);
            return new LinearFit(slope, intercept, rSquared);
        }
    }
}
