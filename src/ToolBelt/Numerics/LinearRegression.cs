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

            // Each variable in units of its largest magnitude (a power of two, so normal-range data give the same bits):
            // the sum of deviations overflowed for data near 1e308, and the result came out NaN.
            double bigX = 0, bigY = 0;
            for (int i = 0; i < n; i++)
            {
                if (double.IsNaN(xs[i]) || double.IsInfinity(xs[i])) throw new ArgumentException($"Value [{i}] is not finite.", nameof(xs));
                if (double.IsNaN(ys[i]) || double.IsInfinity(ys[i])) throw new ArgumentException($"Value [{i}] is not finite.", nameof(ys));
                bigX = Math.Max(bigX, Math.Abs(xs[i]));
                bigY = Math.Max(bigY, Math.Abs(ys[i]));
            }
            double px = Pow2Scale(bigX), py = Pow2Scale(bigY);

            // Means about the first point: a constant variable then has exactly that mean and zero spread, instead of
            // round-off crumbs (0.1 + 0.1 + 0.1 ≠ 0.3) that read as a real, tiny variance.
            double x0 = xs[0] / px, y0 = ys[0] / py, shiftX = 0, shiftY = 0;
            for (int i = 0; i < n; i++)
            {
                shiftX += xs[i] / px - x0;
                shiftY += ys[i] / py - y0;
            }
            double mx = shiftX / n, my = shiftY / n, meanX = x0 + mx, meanY = y0 + my;

            // Deviations are the offsets from the first point less their mean: x − meanX lost them when the mean rounded
            // onto the data's own grid ({1, 1 + 2^-52} gave half the slope and R² 0.5). They are taken relative to each
            // variable's largest: squared outright they underflowed to zero (or overflowed) for data near 1e±160, reading
            // a varying x as constant.
            double maxX = 0, maxY = 0;
            for (int i = 0; i < n; i++)
            {
                maxX = Math.Max(maxX, Math.Abs((xs[i] / px - x0) - mx));
                maxY = Math.Max(maxY, Math.Abs((ys[i] / py - y0) - my));
            }
            double sx = Pow2Scale(maxX), sy = Pow2Scale(maxY);

            double sxx = 0, sxy = 0, syy = 0;
            for (int i = 0; i < n; i++)
            {
                double dx = ((xs[i] / px - x0) - mx) / sx;
                double dy = ((ys[i] / py - y0) - my) / sy;
                sxx += dx * dx;
                sxy += dx * dy;
                syy += dy * dy;
            }

            if (sxx == 0)
                throw new ArgumentException("All x values are identical; the slope is undefined.", nameof(xs));

            // In scaled units, then the ratio of the powers of two: sx·px underflowed for subnormal x ([0, ε] gave slope ∞,
            // intercept NaN) and sy·py overflowed for y near ±double.MaxValue.
            double scaledSlope = sxy / sxx * (sy / sx);
            double slope = MulRatio(scaledSlope, py, px);
            double intercept = (meanY - scaledSlope * meanX) * py;
            // R² = explained variance / total variance. If y is constant (syy == 0) the fit is exact.
            double r = syy == 0 ? 1.0 : sxy / Math.Sqrt(sxx * syy);   // scaled, so the product neither over- nor underflows
            double rSquared = Math.Min(1.0, r * r);
            return new LinearFit(slope, intercept, rSquared);
        }

        // a · num / den for powers of two num and den. num / den overflowed for y near 1e300 and x near 1e-300, and a zero
        // slope came back 0 · ∞ = NaN; this orders the steps so a representable result is reached.
        private static double MulRatio(double a, double num, double den)
        {
            if (a == 0) return 0;
            double ratio = num / den;
            if (ratio != 0 && !double.IsInfinity(ratio)) return a * ratio;
            double t = a * num;
            if (t != 0 && !double.IsInfinity(t)) return t / den;
            return a / den * num;
        }

        // The power of two at or below m (1 for 0): dividing by it is exact, so data in the normal range give the same
        // bits, while data near 1e±160 no longer over- or underflow when squared.
        private static double Pow2Scale(double m)
        {
            if (m == 0 || double.IsInfinity(m)) return 1;
            // At most 2^1023: the logarithm of a value near double.MaxValue rounds up to 1024, p was ∞ and p /= 2 never ended.
            double p = Math.Pow(2, Math.Min(1023, Math.Floor(Math.Log(m) / Math.Log(2))));
            // The logarithm can land a hair either side of an integer; settle on exactly the power at or below m.
            while (p > m) p /= 2;
            while (p * 2 <= m) p *= 2;
            return p;
        }
    }
}
