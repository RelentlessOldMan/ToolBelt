// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;

namespace ToolBelt.Signal
{
    /// <summary>
    /// Savitzky-Golay smoothing: fits a low-order polynomial over a sliding window and takes its value at
    /// the point. Unlike a moving average it preserves peak height and width instead of flattening them, so
    /// it is what you want for a noisy measurement trace. Polynomials up to the chosen order pass through
    /// unchanged. Windows near the ends shift to stay in range (SciPy's <c>savgol_filter(mode='interp')</c>).
    /// Building the fit takes O(w·m²) time and w·m doubles for a window of w points and m = order + 1 (order w − 1 is the
    /// identity and costs nothing), then smoothing takes O(n·w).
    /// </summary>
    public static class SavitzkyGolay
    {
        public static double[] Smooth(IReadOnlyList<double> data, int windowSize, int polynomialOrder)
        {
            if (data is null) throw new ArgumentNullException(nameof(data));
            if (windowSize < 3 || (windowSize & 1) == 0)
                throw new ArgumentException("Window size must be an odd number of at least 3.", nameof(windowSize));
            if (polynomialOrder < 0 || polynomialOrder >= windowSize)
                throw new ArgumentOutOfRangeException(nameof(polynomialOrder), polynomialOrder, "Order must be in [0, windowSize).");
            if (data.Count < windowSize)
                throw new ArgumentException("Data length must be at least the window size.", nameof(data));

            int n = data.Count;
            // Past Array.MaxLength the result threw OutOfMemoryException ("array dimensions exceeded").
            if (n > 0x7FFFFFC7) throw new ArgumentException($"The data has {n} samples, more than an array holds.", nameof(data));
            // Order w − 1 fits every window exactly, so the output is the input. Building the w × w basis for it took 120 s
            // at w = 2001 (and w² doubles).
            if (polynomialOrder == windowSize - 1)
            {
                var copy = new double[n];
                for (int i = 0; i < n; i++) copy[i] = data[i];
                return copy;
            }
            int half = windowSize / 2;
            // Keeping all w rows (w² doubles) took 835 MB at w = 10001; each edge row is used once, so only the centre one is kept.
            var fit = new Fit(windowSize, polynomialOrder);
            double[] centre = fit.Row(half, new double[windowSize]), edge = new double[windowSize];
            var result = new double[n];

            // The window is centred on i, shifted inward at the ends so it always has windowSize points.
            for (int i = half; i < n - half; i++)
            {
                int start = i - half;
                double sum = 0;
                for (int k = 0; k < windowSize; k++) sum += centre[k] * data[start + k];
                if (double.IsInfinity(sum) || double.IsNaN(sum)) sum = Rescaled(centre, data, start, sum);
                else if (Math.Abs(sum) < Tiny) sum = ScaledUp(centre, data, start, sum);
                result[i] = sum;
            }
            Edges(fit, data, 0, 0, half, result, edge);
            Edges(fit, data, n - windowSize, half + 1, windowSize, result, edge);
            return result;
        }

        // Window positions from..to−1 of the window at start. Their weight rows are rows of Q·Qᵀ, so the fitted values are
        // Q·(Qᵀ·window): the window is projected once and the projection evaluated at each position. A weight row built and
        // summed per position made the edges O(w²·m), 19.9 s at w = 40001. The window is scaled to a peak near 1 by a power of
        // two first (exact, but for what it flushes below 2^-1074 of the peak), so neither overflow nor subnormals touch the
        // sums, and scaled back. A window holding ∞ or NaN keeps the plain sums, which say where they fall.
        private static void Edges(Fit fit, IReadOnlyList<double> data, int start, int from, int to, double[] result, double[] row)
        {
            int w = row.Length;
            double peak = 0;
            for (int k = 0; k < w; k++) peak = Math.Max(peak, Math.Abs(data[start + k]));
            if (double.IsInfinity(peak) || double.IsNaN(peak))
            {
                for (int e = from; e < to; e++)
                {
                    double[] c = fit.Row(e, row);
                    double sum = 0;
                    for (int k = 0; k < w; k++) sum += c[k] * data[start + k];
                    result[start + e] = sum;
                }
                return;
            }
            int exponent = peak > 0 ? (int)Math.Ceiling(Math.Log(peak, 2)) : 0;
            double downHalf = Math.Pow(2, -(exponent / 2)), downRest = Math.Pow(2, -(exponent - exponent / 2));
            double upHalf = Math.Pow(2, exponent / 2), upRest = Math.Pow(2, exponent - exponent / 2);
            double[] p = fit.Project(data, start, downHalf, downRest);
            for (int e = from; e < to; e++) result[start + e] = Back(fit.Evaluate(e, p), upHalf, upRest);
        }

        // The scaled value times 2^e. A value a rounding error over the largest double overflowed: the weights sum to a
        // hair over 1, so a constant double.MaxValue came out ∞. Within 2^-40 of it, the value is taken as the largest
        // double; a fit that truly overshoots it is still ∞.
        private static double Back(double s, double upHalf, double upRest)
        {
            double r = s * upHalf * upRest;
            if (!double.IsInfinity(r) || double.IsInfinity(s) || double.IsNaN(s)) return r;
            double limit = double.MaxValue / upRest / upHalf;                // exact: powers of two
            return Math.Abs(s) <= limit * (1 + 9.094947017729282E-13) ? Math.Sign(s) * double.MaxValue : r;
        }

        // Row(e)[k]: the weight of window sample k in the fitted polynomial's value at window position e. The fit is linear in
        // the data, so one least-squares solve per position replaces a fit per sample. The fitted values are the projection of
        // the window onto the polynomials of degree ≤ order sampled at the window's points, so with an orthonormal basis Q of
        // that space (w × m) the weights are row e of Q·Qᵀ. Q is built by Arnoldi (each column is t times the previous one,
        // orthogonalised twice against all before it): a monomial basis, even under QR on an abscissa scaled to [−1, 1], is so
        // ill-conditioned that order 40 on 41 points was off by 0.24 and order 60 on 101 points was wrong at the centre.
        // Q is kept a column to an array: walking a w × m array down its columns made the build cache-bound (order 1999 on
        // 2001 points took minutes).
        private sealed class Fit
        {
            private readonly int _w, _m;
            private readonly double[][] _q;                                  // _q[j][k]: column j at window position k

            public Fit(int w, int order)
            {
                int m = order + 1, half = w / 2;
                _w = w; _m = m;
                var q = new double[m][];
                var t = new double[w];
                for (int k = 0; k < w; k++) t[k] = (k - half) / (double)half;
                double c0 = 1 / Math.Sqrt(w);
                q[0] = new double[w];
                for (int k = 0; k < w; k++) q[0][k] = c0;
                for (int j = 1; j < m; j++)
                {
                    var v = new double[w];
                    double[] previous = q[j - 1];
                    for (int k = 0; k < w; k++) v[k] = t[k] * previous[k];
                    for (int pass = 0; pass < 2; pass++)
                        for (int i = 0; i < j; i++)
                        {
                            double[] qi = q[i];
                            double dot = 0;
                            for (int k = 0; k < w; k++) dot += qi[k] * v[k];
                            for (int k = 0; k < w; k++) v[k] -= dot * qi[k];
                        }
                    double norm = 0;
                    for (int k = 0; k < w; k++) norm = Hypot(norm, v[k]);
                    for (int k = 0; k < w; k++) v[k] /= norm;
                    q[j] = v;
                }
                _q = q;
            }

            // Qᵀ·window, the window's samples each multiplied by down1 then down2.
            public double[] Project(IReadOnlyList<double> data, int start, double down1, double down2)
            {
                var x = new double[_w];
                for (int k = 0; k < _w; k++) x[k] = data[start + k] * down1 * down2;
                var p = new double[_m];
                for (int j = 0; j < _m; j++)
                {
                    double[] qj = _q[j];
                    double sum = 0;
                    for (int k = 0; k < _w; k++) sum += qj[k] * x[k];
                    p[j] = sum;
                }
                return p;
            }

            // The fitted value at window position e from the projection.
            public double Evaluate(int e, double[] p)
            {
                double sum = 0;
                for (int j = 0; j < _m; j++) sum += _q[j][e] * p[j];
                return sum;
            }

            public double[] Row(int e, double[] c)
            {
                int w = _w;
                Array.Clear(c, 0, w);
                for (int j = 0; j < _m; j++)
                {
                    double[] qj = _q[j];
                    double qe = qj[e];
                    for (int k = 0; k < w; k++) c[k] += qe * qj[k];
                }
                return c;
            }
        }

        // Edge rows' partial sums pass 1, so large data overflowed (five samples of 1.7e308 gave ∞ at the ends, although a
        // constant passes through unchanged): the window is summed again scaled to a peak of at most 1 by a power of two
        // (exact), then scaled back. A window holding ∞ or NaN keeps the plain sum.
        private static double Rescaled(double[] c, IReadOnlyList<double> data, int start, double sum)
        {
            double peak = 0;
            for (int k = 0; k < c.Length; k++) peak = Math.Max(peak, Math.Abs(data[start + k]));
            if (double.IsInfinity(peak) || double.IsNaN(peak) || !(peak > 1)) return sum;
            int e = (int)Math.Ceiling(Math.Log(peak, 2));
            double down = Math.Pow(2, -e), s = 0;
            for (int k = 0; k < c.Length; k++) s += c[k] * (data[start + k] * down);
            return Back(s, Math.Pow(2, e / 2), Math.Pow(2, e - e / 2));
        }

        // Subnormal data: each weighted sample was rounded on the subnormal grid, so a constant ε came out 0 inside the
        // record and 3ε as 4ε at the edges. A window whose peak is below 2^-400 is summed again scaled up by 2^600
        // (exact) and scaled back, one rounding.
        private const double Tiny = 1.0020841800044864E-292;                 // 2^-969
        private static readonly double Up = Math.Pow(2, 600), Down = Math.Pow(2, -600), Small = Math.Pow(2, -400);

        private static double ScaledUp(double[] c, IReadOnlyList<double> data, int start, double sum)
        {
            double peak = 0;
            for (int k = 0; k < c.Length; k++) peak = Math.Max(peak, Math.Abs(data[start + k]));
            if (peak == 0 || !(peak < Small)) return sum;
            double s = 0;
            for (int k = 0; k < c.Length; k++) s += c[k] * (data[start + k] * Up);
            return s * Down;
        }

        private static double Hypot(double x, double y)
        {
            x = Math.Abs(x); y = Math.Abs(y);
            if (x < y) { var t = x; x = y; y = t; }
            if (x == 0) return 0;
            double r = y / x;
            return x * Math.Sqrt(1 + r * r);
        }
    }
}
