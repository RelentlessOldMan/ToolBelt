// ToolBelt drop-in — also copy Numerics/Distributions.cs (Student-t for p-values and intervals).
using System;
using System.Collections.Generic;

namespace ToolBelt.Numerics
{
    /// <summary>The result of an ordinary least-squares fit with inference.</summary>
    public sealed class RegressionResult
    {
        private readonly double[,] _xtxInverse;

        internal RegressionResult(double[] coefficients, double[] standardErrors, double[] fitted, double[] residuals,
            double rSquared, double adjustedRSquared, double residualStandardError, int degreesOfFreedom, bool hasIntercept, double[,] xtxInverse)
        {
            Coefficients = coefficients;
            StandardErrors = standardErrors;
            var t = new double[coefficients.Length];
            var p = new double[coefficients.Length];
            for (int i = 0; i < t.Length; i++)
            {
                t[i] = standardErrors[i] > 0 ? coefficients[i] / standardErrors[i] : double.NaN;
                p[i] = degreesOfFreedom > 0 && !double.IsNaN(t[i])
                    ? 2 * Distributions.StudentTCdf(-Math.Abs(t[i]), degreesOfFreedom) : double.NaN;
            }
            TStatistics = t;
            PValues = p;
            Fitted = fitted;
            Residuals = residuals;
            RSquared = rSquared;
            AdjustedRSquared = adjustedRSquared;
            ResidualStandardError = residualStandardError;
            DegreesOfFreedom = degreesOfFreedom;
            HasIntercept = hasIntercept;
            _xtxInverse = xtxInverse;
        }

        /// <summary>Coefficients; the intercept first when <see cref="HasIntercept"/>, then one per predictor.</summary>
        public IReadOnlyList<double> Coefficients { get; }
        public IReadOnlyList<double> StandardErrors { get; }
        public IReadOnlyList<double> TStatistics { get; }

        /// <summary>Two-sided p-values for each coefficient being zero (Student-t, residual df).</summary>
        public IReadOnlyList<double> PValues { get; }

        public IReadOnlyList<double> Fitted { get; }
        public IReadOnlyList<double> Residuals { get; }
        public double RSquared { get; }
        public double AdjustedRSquared { get; }

        /// <summary>√(SSE / df): the estimated standard deviation of the noise.</summary>
        public double ResidualStandardError { get; }

        /// <summary>Residual degrees of freedom: observations − coefficients.</summary>
        public int DegreesOfFreedom { get; }
        public bool HasIntercept { get; }

        /// <summary>The fitted value for one new observation of the predictors.</summary>
        public double Predict(IReadOnlyList<double> predictors)
        {
            double[] row = Row(predictors);
            double y = 0;
            for (int j = 0; j < row.Length; j++) y += row[j] * Coefficients[j];
            return y;
        }

        /// <summary>Interval for one new observation at <paramref name="predictors"/> (noise included).</summary>
        public (double Lower, double Upper) PredictionInterval(IReadOnlyList<double> predictors, double confidence = 0.95)
            => Interval(predictors, confidence, includeNoise: true);

        /// <summary>Interval for the mean response at <paramref name="predictors"/>.</summary>
        public (double Lower, double Upper) ConfidenceInterval(IReadOnlyList<double> predictors, double confidence = 0.95)
            => Interval(predictors, confidence, includeNoise: false);

        private (double, double) Interval(IReadOnlyList<double> predictors, double confidence, bool includeNoise)
        {
            if (!(confidence > 0 && confidence < 1)) throw new ArgumentOutOfRangeException(nameof(confidence), confidence, "Confidence must be in (0, 1).");
            if (DegreesOfFreedom < 1) throw new InvalidOperationException("No residual degrees of freedom: intervals are undefined.");
            double[] row = Row(predictors);
            double leverage = 0;
            for (int i = 0; i < row.Length; i++)
                for (int j = 0; j < row.Length; j++)
                    leverage += row[i] * _xtxInverse[i, j] * row[j];
            double se = ResidualStandardError * Math.Sqrt((includeNoise ? 1 : 0) + leverage);
            double t = Distributions.StudentTQuantile((1 + confidence) / 2, DegreesOfFreedom);
            double y = Predict(predictors);
            return (y - t * se, y + t * se);
        }

        private double[] Row(IReadOnlyList<double> predictors)
        {
            if (predictors is null) throw new ArgumentNullException(nameof(predictors));
            int expected = Coefficients.Count - (HasIntercept ? 1 : 0);
            if (predictors.Count != expected) throw new ArgumentException($"Expected {expected} predictor values.", nameof(predictors));
            var row = new double[Coefficients.Count];
            int o = 0;
            if (HasIntercept) row[o++] = 1;
            for (int j = 0; j < predictors.Count; j++) row[o + j] = predictors[j];
            return row;
        }
    }

    /// <summary>
    /// Ordinary least squares with several predictors, solved by Householder QR (numerically sounder than the normal
    /// equations), with standard errors, t-statistics, p-values, R² and adjusted R², and confidence/prediction intervals.
    /// Exactly collinear predictors are rejected rather than silently producing garbage. The one-predictor line fit is the
    /// special case; for data with outliers see <see cref="RobustRegression"/>.
    /// </summary>
    public static class MultipleRegression
    {
        /// <summary>Fits y on the columns of <paramref name="x"/> (rows are observations).</summary>
        public static RegressionResult Fit(double[,] x, IReadOnlyList<double> y, bool intercept = true)
        {
            if (x is null) throw new ArgumentNullException(nameof(x));
            if (y is null) throw new ArgumentNullException(nameof(y));
            int n = x.GetLength(0), k = x.GetLength(1);
            if (n != y.Count) throw new ArgumentException("x must have one row per y value.", nameof(y));
            int p = k + (intercept ? 1 : 0);
            if (p == 0) throw new ArgumentException("Need at least one predictor or an intercept.", nameof(x));
            if (n < p) throw new ArgumentException($"Need at least {p} observations for {p} coefficients.", nameof(y));

            var a = new double[n, p];
            for (int i = 0; i < n; i++)
            {
                int o = 0;
                if (intercept) a[i, o++] = 1;
                for (int j = 0; j < k; j++) a[i, o + j] = x[i, j];
            }
            var b = new double[n];
            for (int i = 0; i < n; i++) b[i] = y[i];

            // Each column's own length: reflections are orthogonal, so |R_cc| compared with it measures how much of the column
            // is independent of the earlier ones, whatever its units (a global threshold flags 1e-7-scale predictors next to 1e6).
            var columnNorm = new double[p];
            for (int c = 0; c < p; c++)
                for (int i = 0; i < n; i++) columnNorm[c] = Hypot(columnNorm[c], a[i, c]);

            // Householder QR in place: R in the upper triangle, Qᵀy accumulated into b.
            var rdiag = new double[p];
            for (int c = 0; c < p; c++)
            {
                double norm = 0;
                for (int i = c; i < n; i++) norm = Hypot(norm, a[i, c]);
                if (norm == 0) { rdiag[c] = 0; continue; }
                if (a[c, c] < 0) norm = -norm;
                for (int i = c; i < n; i++) a[i, c] /= norm;
                a[c, c] += 1;
                for (int j = c + 1; j < p; j++)
                {
                    double s = 0;
                    for (int i = c; i < n; i++) s += a[i, c] * a[i, j];
                    s = -s / a[c, c];
                    for (int i = c; i < n; i++) a[i, j] += s * a[i, c];
                }
                double sb = 0;
                for (int i = c; i < n; i++) sb += a[i, c] * b[i];
                sb = -sb / a[c, c];
                for (int i = c; i < n; i++) b[i] += sb * a[i, c];
                rdiag[c] = -norm;
            }
            for (int c = 0; c < p; c++)
                if (Math.Abs(rdiag[c]) <= 1e-10 * columnNorm[c])
                    throw new InvalidOperationException("The predictors are collinear (or a column is constant alongside the intercept); coefficients are not identifiable.");

            // R (p×p): diagonal rdiag, strict upper triangle in a.
            var r = new double[p, p];
            for (int i = 0; i < p; i++)
            {
                r[i, i] = rdiag[i];
                for (int j = i + 1; j < p; j++) r[i, j] = a[i, j];
            }
            var beta = new double[p];
            for (int i = p - 1; i >= 0; i--)
            {
                double s = b[i];
                for (int j = i + 1; j < p; j++) s -= r[i, j] * beta[j];
                beta[i] = s / r[i, i];
            }

            // (XᵀX)⁻¹ = R⁻¹ R⁻ᵀ
            var rinv = new double[p, p];
            for (int col = 0; col < p; col++)
            {
                rinv[col, col] = 1 / r[col, col];
                for (int i = col - 1; i >= 0; i--)
                {
                    double s = 0;
                    for (int j = i + 1; j <= col; j++) s += r[i, j] * rinv[j, col];
                    rinv[i, col] = -s / r[i, i];
                }
            }
            var xtxInv = new double[p, p];
            for (int i = 0; i < p; i++)
                for (int j = 0; j < p; j++)
                {
                    double s = 0;
                    for (int m = Math.Max(i, j); m < p; m++) s += rinv[i, m] * rinv[j, m];
                    xtxInv[i, j] = s;
                }

            var fitted = new double[n];
            var residuals = new double[n];
            double sse = 0, ybar = 0;
            for (int i = 0; i < n; i++) ybar += y[i];
            ybar /= n;
            double sst = 0;
            for (int i = 0; i < n; i++)
            {
                double f = intercept ? beta[0] : 0;
                for (int j = 0; j < k; j++) f += beta[j + (intercept ? 1 : 0)] * x[i, j];
                fitted[i] = f;
                residuals[i] = y[i] - f;
                sse += residuals[i] * residuals[i];
                double centered = intercept ? y[i] - ybar : y[i];
                sst += centered * centered;
            }
            int df = n - p;
            double sigma2 = df > 0 ? sse / df : double.NaN;
            var se = new double[p];
            for (int i = 0; i < p; i++) se[i] = df > 0 ? Math.Sqrt(sigma2 * xtxInv[i, i]) : double.NaN;
            double r2 = sst > 0 ? 1 - sse / sst : 1;
            int dfTotal = intercept ? n - 1 : n;
            double adj = df > 0 && sst > 0 ? 1 - (sse / df) / (sst / dfTotal) : double.NaN;
            return new RegressionResult(beta, se, fitted, residuals, r2, adj, Math.Sqrt(sigma2), df, intercept, xtxInv);
        }

        /// <summary>Fits y on predictor rows given as lists (each inner list is one observation).</summary>
        public static RegressionResult Fit(IReadOnlyList<IReadOnlyList<double>> rows, IReadOnlyList<double> y, bool intercept = true)
        {
            if (rows is null) throw new ArgumentNullException(nameof(rows));
            int n = rows.Count, k = n == 0 ? 0 : rows[0].Count;
            var x = new double[n, k];
            for (int i = 0; i < n; i++)
            {
                if (rows[i] is null || rows[i].Count != k) throw new ArgumentException("Every row needs the same number of predictors.", nameof(rows));
                for (int j = 0; j < k; j++) x[i, j] = rows[i][j];
            }
            return Fit(x, y, intercept);
        }

        private static double Hypot(double a, double b)
        {
            double x = Math.Abs(a), y = Math.Abs(b);
            if (x < y) (x, y) = (y, x);
            if (x == 0) return 0;
            double r = y / x;
            return x * Math.Sqrt(1 + r * r);
        }
    }

    /// <summary>Line fits that resist outliers.</summary>
    public static class RobustRegression
    {
        /// <summary>
        /// The Theil–Sen estimator: the median of all pairwise slopes, intercept = median of y − slope·x. Tolerates up to
        /// ~29% arbitrarily bad points (a single wild reading barely moves it, unlike least squares). O(n²) pairs — fine
        /// for thousands of points.
        /// </summary>
        public static (double Slope, double Intercept) TheilSen(IReadOnlyList<double> x, IReadOnlyList<double> y)
        {
            if (x is null) throw new ArgumentNullException(nameof(x));
            if (y is null) throw new ArgumentNullException(nameof(y));
            if (x.Count != y.Count) throw new ArgumentException("x and y must have the same length.");
            var slopes = new List<double>();
            for (int i = 0; i < x.Count; i++)
                for (int j = i + 1; j < x.Count; j++)
                    if (x[j] != x[i]) slopes.Add((y[j] - y[i]) / (x[j] - x[i]));
            if (slopes.Count == 0) throw new ArgumentException("Need at least two distinct x values.");
            double slope = Median(slopes);
            var intercepts = new List<double>(x.Count);
            for (int i = 0; i < x.Count; i++) intercepts.Add(y[i] - slope * x[i]);
            return (slope, Median(intercepts));
        }

        private static double Median(List<double> v)
        {
            v.Sort();
            int m = v.Count / 2;
            return v.Count % 2 == 1 ? v[m] : 0.5 * (v[m - 1] + v[m]);
        }
    }
}
