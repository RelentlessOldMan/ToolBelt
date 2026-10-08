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
            double[][] coefficients = Coefficients(windowSize, polynomialOrder);
            var result = new double[n];

            for (int i = 0; i < n; i++)
            {
                // Center the window on i, shifting it inward at the ends so it always has windowSize points.
                int start = i - half;
                if (start < 0) start = 0;
                if (start > n - windowSize) start = n - windowSize;

                double[] c = coefficients[i - start];
                double sum = 0;
                for (int k = 0; k < windowSize; k++) sum += c[k] * data[start + k];
                result[i] = sum;
            }
            return result;
        }

        // coefficients[e][k]: the weight of window sample k in the fitted polynomial's value at window position e. The fit
        // is linear in the data, so one least-squares solve per position replaces a fit per sample. It uses Householder QR
        // on an abscissa scaled to [−1, 1]: normal equations on raw positions 0..w−1 lose every digit at moderate orders.
        private static double[][] Coefficients(int w, int order)
        {
            int m = order + 1, half = w / 2;
            var a = new double[w, m];
            for (int k = 0; k < w; k++)
            {
                double t = (k - half) / (double)half, p = 1;
                for (int j = 0; j < m; j++) { a[k, j] = p; p *= t; }
            }

            // In-place Householder QR: reflectors below the diagonal (with their leading entries in v0), R on and above it.
            var v0 = new double[m];
            for (int j = 0; j < m; j++)
            {
                double norm = 0;
                for (int k = j; k < w; k++) norm = Hypot(norm, a[k, j]);
                double alpha = a[j, j] > 0 ? -norm : norm;
                v0[j] = a[j, j] - alpha;
                a[j, j] = alpha;
                double vv = v0[j] * v0[j];
                for (int k = j + 1; k < w; k++) vv += a[k, j] * a[k, j];
                if (vv == 0) continue;
                for (int c = j + 1; c < m; c++)
                {
                    double dot = v0[j] * a[j, c];
                    for (int k = j + 1; k < w; k++) dot += a[k, j] * a[k, c];
                    double s = 2 * dot / vv;
                    a[j, c] -= s * v0[j];
                    for (int k = j + 1; k < w; k++) a[k, c] -= s * a[k, j];
                }
            }

            var result = new double[w][];
            var z = new double[m];
            for (int e = 0; e < w; e++)
            {
                // Value at t_e is p(t_e)ᵀ·R⁻¹·Qᵀ·y, so the weights are Q·(R⁻ᵀ·p(t_e)): forward-substitute, then apply Q.
                double t = (e - half) / (double)half, p = 1;
                for (int j = 0; j < m; j++)
                {
                    double s = p;
                    for (int i = 0; i < j; i++) s -= a[i, j] * z[i];
                    z[j] = s / a[j, j];
                    p *= t;
                }
                var c = new double[w];
                for (int j = 0; j < m; j++) c[j] = z[j];
                for (int j = m - 1; j >= 0; j--)                        // Q = H₀·H₁·…·H_{m−1}
                {
                    double vv = v0[j] * v0[j];
                    for (int k = j + 1; k < w; k++) vv += a[k, j] * a[k, j];
                    if (vv == 0) continue;
                    double dot = v0[j] * c[j];
                    for (int k = j + 1; k < w; k++) dot += a[k, j] * c[k];
                    double s = 2 * dot / vv;
                    c[j] -= s * v0[j];
                    for (int k = j + 1; k < w; k++) c[k] -= s * a[k, j];
                }
                result[e] = c;
            }
            return result;
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
