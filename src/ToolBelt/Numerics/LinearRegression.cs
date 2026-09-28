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
    /// and non-constant x values (otherwise the slope is undefined).
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

            double meanX = 0, meanY = 0;
            for (int i = 0; i < n; i++) { meanX += xs[i]; meanY += ys[i]; }
            meanX /= n; meanY /= n;

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
            double rSquared = syy == 0 ? 1.0 : (sxy * sxy) / (sxx * syy);
            return new LinearFit(slope, intercept, rSquared);
        }
    }
}
