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
            int half = windowSize / 2;
            // Keeping all w rows (w² doubles) took 835 MB at w = 10001; each edge row is used once, so only the centre one is kept.
            var fit = new Fit(windowSize, polynomialOrder);
            double[] centre = fit.Row(half, new double[windowSize]), edge = new double[windowSize];
            var result = new double[n];

            for (int i = 0; i < n; i++)
            {
                // Center the window on i, shifting it inward at the ends so it always has windowSize points.
                int start = i - half;
                if (start < 0) start = 0;
                if (start > n - windowSize) start = n - windowSize;

                int e = i - start;
                double[] c = e == half ? centre : fit.Row(e, edge);
                double sum = 0;
                for (int k = 0; k < windowSize; k++) sum += c[k] * data[start + k];
                result[i] = sum;
            }
            return result;
        }

        // Row(e)[k]: the weight of window sample k in the fitted polynomial's value at window position e. The fit is linear in
        // the data, so one least-squares solve per position replaces a fit per sample. The fitted values are the projection of
        // the window onto the polynomials of degree ≤ order sampled at the window's points, so with an orthonormal basis Q of
        // that space (w × m) the weights are row e of Q·Qᵀ. Q is built by Arnoldi (each column is t times the previous one,
        // orthogonalised twice against all before it): a monomial basis, even under QR on an abscissa scaled to [−1, 1], is so
        // ill-conditioned that order 40 on 41 points was off by 0.24 and order 60 on 101 points was wrong at the centre.
        private sealed class Fit
        {
            private readonly int _w, _m;
            private readonly double[,] _q;

            public Fit(int w, int order)
            {
                int m = order + 1, half = w / 2;
                _w = w; _m = m;
                var q = new double[w, m];
                var t = new double[w];
                for (int k = 0; k < w; k++) t[k] = (k - half) / (double)half;
                double c0 = 1 / Math.Sqrt(w);
                for (int k = 0; k < w; k++) q[k, 0] = c0;
                var v = new double[w];
                for (int j = 1; j < m; j++)
                {
                    for (int k = 0; k < w; k++) v[k] = t[k] * q[k, j - 1];
                    for (int pass = 0; pass < 2; pass++)
                        for (int i = 0; i < j; i++)
                        {
                            double dot = 0;
                            for (int k = 0; k < w; k++) dot += q[k, i] * v[k];
                            for (int k = 0; k < w; k++) v[k] -= dot * q[k, i];
                        }
                    double norm = 0;
                    for (int k = 0; k < w; k++) norm = Hypot(norm, v[k]);
                    for (int k = 0; k < w; k++) q[k, j] = v[k] / norm;
                }
                _q = q;
            }

            public double[] Row(int e, double[] c)
            {
                int w = _w, m = _m;
                double[,] q = _q;
                for (int k = 0; k < w; k++)
                {
                    double sum = 0;
                    for (int j = 0; j < m; j++) sum += q[e, j] * q[k, j];
                    c[k] = sum;
                }
                return c;
            }
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
