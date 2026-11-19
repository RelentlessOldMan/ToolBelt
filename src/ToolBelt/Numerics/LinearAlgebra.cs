// ToolBelt drop-in — fully self-contained (BCL only).
using System;

namespace ToolBelt.Numerics
{
    /// <summary>
    /// Small dense linear algebra over <c>double[,]</c> matrices and <c>double[]</c> vectors: matrix
    /// multiply/transpose/identity, and LU-decomposition-with-partial-pivoting for solving systems,
    /// determinants and inverses. Deliberately small and honest — for small dense systems, not a
    /// general linear-algebra library.
    /// </summary>
    public static class LinearAlgebra
    {
        public static double[,] Identity(int n)
        {
            // n² past Array.MaxLength threw OutOfMemoryException ("array dimensions exceeded"); 46,339² is the largest that fits.
            if (n < 1 || (long)n * n > 0x7FFFFFC7) throw new ArgumentOutOfRangeException(nameof(n), n, "Size must be 1 to 46,339 (n² at most Array.MaxLength).");
            var m = new double[n, n];
            for (int i = 0; i < n; i++) m[i, i] = 1;
            return m;
        }

        public static double[,] Transpose(double[,] a)
        {
            if (a is null) throw new ArgumentNullException(nameof(a));
            RequireZeroBased(a, nameof(a));
            int rows = a.GetLength(0), cols = a.GetLength(1);
            var t = new double[cols, rows];
            for (int i = 0; i < rows; i++)
                for (int j = 0; j < cols; j++)
                    t[j, i] = a[i, j];
            return t;
        }

        public static double[,] Multiply(double[,] a, double[,] b)
        {
            if (a is null) throw new ArgumentNullException(nameof(a));
            if (b is null) throw new ArgumentNullException(nameof(b));
            RequireZeroBased(a, nameof(a));
            RequireZeroBased(b, nameof(b));
            int ar = a.GetLength(0), ac = a.GetLength(1), br = b.GetLength(0), bc = b.GetLength(1);
            if (ac != br) throw new ArgumentException($"Cannot multiply {ar}x{ac} by {br}x{bc}.", nameof(b));
            // 100000x0 by 0x100000 threw OutOfMemoryException: the product has more elements than an array can hold.
            if ((long)ar * bc > 0x7FFFFFC7)
                throw new ArgumentException($"The {ar}x{bc} product has more than Array.MaxLength elements.", nameof(b));
            var result = new double[ar, bc];
            for (int i = 0; i < ar; i++)
                for (int k = 0; k < ac; k++)
                {
                    double aik = a[i, k];
                    for (int j = 0; j < bc; j++)
                        result[i, j] += aik * b[k, j];
                }
            return result;
        }

        public static double[] Multiply(double[,] a, double[] x)
        {
            if (a is null) throw new ArgumentNullException(nameof(a));
            if (x is null) throw new ArgumentNullException(nameof(x));
            RequireZeroBased(a, nameof(a));
            int rows = a.GetLength(0), cols = a.GetLength(1);
            if (cols != x.Length) throw new ArgumentException($"Cannot multiply {rows}x{cols} matrix by length-{x.Length} vector.", nameof(x));
            var result = new double[rows];
            for (int i = 0; i < rows; i++)
            {
                double sum = 0;
                for (int j = 0; j < cols; j++) sum += a[i, j] * x[j];
                result[i] = sum;
            }
            return result;
        }

        /// <summary>Solves A·x = b for a square, non-singular A. Throws if A is singular.</summary>
        public static double[] Solve(double[,] a, double[] b)
        {
            if (a is null) throw new ArgumentNullException(nameof(a));
            if (b is null) throw new ArgumentNullException(nameof(b));
            int n = RequireSquare(a);
            if (b.Length != n) throw new ArgumentException("Right-hand side length must match the matrix size.", nameof(b));
            if (!Decompose(a, out double[,] lu, out int[] pivot, out _, out int[] shift, out int[] colShift))
                throw new InvalidOperationException("Matrix is singular; the system has no unique solution.");
            // The rows were scaled by 2^−shift, so b is too. A common 2^k (taken out of x after) keeps the scaled b near 1:
            // scaling b alone underflowed a small b on a huge row and overflowed a large b on a tiny one.
            int k = int.MinValue;
            for (int i = 0; i < n; i++)
                if (b[i] != 0) k = Math.Max(k, Exponent(Math.Abs(b[i])) - shift[i]);
            if (k == int.MinValue) k = 0;
            var scaled = new double[n];
            for (int i = 0; i < n; i++) scaled[i] = ScaleB(b[i], -shift[i] - k);
            double[] x = SolveDecomposed(lu, pivot, scaled);
            for (int i = 0; i < n; i++) x[i] = ScaleB(x[i], k - colShift[i]); // the columns were scaled by 2^−colShift
            return x;
        }

        public static double Determinant(double[,] a)
        {
            if (a is null) throw new ArgumentNullException(nameof(a));
            int n = RequireSquare(a);
            // Only an exactly zero pivot makes the determinant 0; a small one is a small (but real) determinant.
            if (!Decompose(a, out double[,] lu, out _, out int sign, out int[] shift, out int[] colShift, exactZeroOnly: true))
                return 0.0;
            // The running product kept as mantissa · 2^exponent: multiplied directly, 1e200 · 1e200 · 1e-200 · 1e-200
            // overflowed to ∞ on the way to 1. Only the final scale saturates (to ±∞ or 0). Each pivot is split too:
            // a mantissa in [1, 2) times a pivot of 1.5e308 overflowed before it was rescaled.
            double mantissa = sign;
            int exponent = 0;
            foreach (int s in shift) exponent += s; // det A = det(scaled A) · 2^(Σshift + ΣcolShift)
            foreach (int s in colShift) exponent += s;
            for (int i = 0; i < n; i++)
            {
                double pivot = lu[i, i];
                int pe = Exponent(Math.Abs(pivot));
                mantissa *= ScaleB(pivot, -pe);
                exponent += pe;
                int e = Exponent(Math.Abs(mantissa));
                mantissa = ScaleB(mantissa, -e);
                exponent += e;
            }
            return ScaleB(mantissa, exponent);
        }

        public static double[,] Inverse(double[,] a)
        {
            if (a is null) throw new ArgumentNullException(nameof(a));
            int n = RequireSquare(a);
            if (!Decompose(a, out double[,] lu, out int[] pivot, out _, out int[] shift, out int[] colShift))
                throw new InvalidOperationException("Matrix is singular and cannot be inverted.");

            var inverse = new double[n, n];
            var e = new double[n];
            for (int col = 0; col < n; col++)
            {
                Array.Clear(e, 0, n);
                e[col] = 1; // A⁻¹ = C·(DAC)⁻¹·D for the row and column scalings D and C, applied after the solve so e stays 1
                double[] x = SolveDecomposed(lu, pivot, e);
                for (int row = 0; row < n; row++) inverse[row, col] = ScaleB(x[row], -shift[col] - colShift[row]);
            }
            return inverse;
        }

        // LU with partial pivoting. Returns false if the matrix is singular: a pivot at or below n·ε times the largest
        // entry of its own original row, i.e. what is left once elimination has cancelled that row away (relative per
        // row, so neither a uniformly tiny matrix nor diag(1e20, 1) is "singular"; one global scale rejected the
        // latter), or exactly zero when exactZeroOnly. Non-finite entries throw. A row whose largest entry is within
        // 2^(n + 24) of the overflow threshold, or below 2⁻⁹⁶⁰, is first scaled by 2^−shift[row] (exactly) so that entry
        // is in [1, 2): elimination overflowed on rows near double.MaxValue (1e308 − (−1e308)) and lost subnormal ones,
        // and partial pivoting can grow entries by 2^(n−1) (a fixed 2⁹⁶⁰ threshold left a 70×70 Wilkinson matrix of
        // 1e288s too little headroom, an all-NaN answer). Ordinary rows are left alone, so their pivots are unchanged.
        // An elimination that overflows anyway throws rather than return NaN.
        // A matrix whose non-zero entries span more than 2^52 is instead balanced: rows and columns are both scaled by
        // powers of two (shift and colShift; see Balance) so that no entry is swamped by one it outweighs. Scaling rows alone
        // turned {{1e305, 1e-223}, {1e239, 1e-287}}'s small entries into 0 (determinant 0, "singular"), and unscaled, the
        // multiplier 1e-108 / 1e233 of {{1e-108, 1e-224}, {1e233, 1e212}} underflowed to 0 (determinant −1e9, not 1e104).
        private static bool Decompose(double[,] a, out double[,] lu, out int[] pivot, out int sign, out int[] shift,
            out int[] colShift, bool exactZeroOnly = false)
        {
            int n = a.GetLength(0);
            lu = (double[,])a.Clone();
            pivot = new int[n];
            for (int i = 0; i < n; i++) pivot[i] = i;
            sign = 1;

            var rowMax = new double[n]; // follows its row through the swaps
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                {
                    double v = a[i, j];
                    if (double.IsNaN(v) || double.IsInfinity(v))
                        throw new ArgumentException($"Matrix entry [{i}, {j}] is not finite.", nameof(a));
                    rowMax[i] = Math.Max(rowMax[i], Math.Abs(v));
                }
            shift = new int[n];
            colShift = new int[n];
            if (Balance(a, shift, colShift))
            {
                for (int i = 0; i < n; i++)
                {
                    rowMax[i] = 0;
                    for (int j = 0; j < n; j++)
                    {
                        lu[i, j] = ScaleB(a[i, j], -shift[i] - colShift[j]);
                        rowMax[i] = Math.Max(rowMax[i], Math.Abs(lu[i, j]));
                    }
                }
            }
            else for (int i = 0; i < n; i++)
            {
                if (rowMax[i] == 0) continue;
                int e = Exponent(rowMax[i]);
                if (e >= 1000 - Math.Min(n, 1000) || e < -960) shift[i] = e; // largest entry into [1, 2)
                else continue;
                for (int j = 0; j < n; j++) lu[i, j] = ScaleB(lu[i, j], -shift[i]);
                rowMax[i] = ScaleB(rowMax[i], -shift[i]);
            }
            var colMax = new double[n];
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++) colMax[j] = Math.Max(colMax[j], Math.Abs(lu[i, j]));
            double relative = exactZeroOnly ? 0 : n * 2.220446049250313e-16;
            // Each entry's bound on what elimination has combined into it, |a| + Σ|l|·|u|: its rounding error is within
            // about nε of that. Against the original row and column alone, the rounding left in the last pivot of an
            // exactly singular matrix passed as a pivot ({{6, 7, -9}, {-5, -6, 4}, {-3, -4, -6}} solved to x ≈ 3e15; 721
            // of 20,000 random rank-deficient integer matrices, and 86 of 20,000 with rows and columns scaled, were
            // accepted).
            var mag = new double[n, n];
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++) mag[i, j] = Math.Abs(lu[i, j]);

            for (int col = 0; col < n; col++)
            {
                int max = col;
                double best = Math.Abs(lu[col, col]);
                for (int row = col + 1; row < n; row++)
                {
                    double v = Math.Abs(lu[row, col]);
                    if (v > best) { best = v; max = row; }
                }
                // Singular only when the pivot is negligible against both its row and its column: columns in mixed units
                // ({{1e-20, 1e20}, {2e-20, 3e20}}, determinant 1) are not singular.
                if (!(best > relative * rowMax[max]) && !(best > relative * colMax[col])) return false;
                if (!exactZeroOnly && !(best > 8 * relative * mag[max, col])) return false;

                if (max != col)
                {
                    for (int j = 0; j < n; j++) (lu[col, j], lu[max, j]) = (lu[max, j], lu[col, j]);
                    for (int j = 0; j < n; j++) (mag[col, j], mag[max, j]) = (mag[max, j], mag[col, j]);
                    (pivot[col], pivot[max]) = (pivot[max], pivot[col]);
                    (rowMax[col], rowMax[max]) = (rowMax[max], rowMax[col]);
                    sign = -sign;
                }

                for (int row = col + 1; row < n; row++)
                {
                    double factor = lu[row, col] / lu[col, col];
                    lu[row, col] = factor;
                    for (int j = col + 1; j < n; j++) lu[row, j] -= factor * lu[col, j];
                    double f = Math.Abs(factor);
                    for (int j = col + 1; j < n; j++) mag[row, j] += f * mag[col, j];
                }
            }
            foreach (double v in lu)
                if (double.IsNaN(v) || double.IsInfinity(v))
                    throw new InvalidOperationException("The elimination overflowed: pivot growth took an entry beyond the double range.");
            return true;
        }

        // Power-of-two row and column scalings (entry [i, j] times 2^−(shift[i] + colShift[j])) for a matrix whose
        // non-zero entries span more than 2^52; false, and nothing set, otherwise, or when no permutation picks only
        // non-zero entries (the matrix is then singular, and elimination finds that). Olschowka and Neumaier's scaling:
        // the permutation with the largest product of entries, found as an assignment problem on the exponents (the
        // Hungarian method), and its dual potentials, which put that permutation's entries in [1, 2) and every entry below
        // 2. Worked on exponents, so nothing overflows. Scaling to make each row's and column's largest entry about 1
        // (Ruiz) still let partial pivoting swamp the dominant product: ten of 15,000 random 3×3 and 4×4 determinants with
        // one term outweighing all the others came out wrong, some 0.
        private static bool Balance(double[,] a, int[] shift, int[] colShift)
        {
            int n = a.GetLength(0);
            var e = new int[n, n];
            int lo = int.MaxValue, hi = int.MinValue;
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                {
                    if (a[i, j] == 0) { e[i, j] = int.MinValue; continue; }
                    e[i, j] = Exponent(Math.Abs(a[i, j]));
                    lo = Math.Min(lo, e[i, j]);
                    hi = Math.Max(hi, e[i, j]);
                }
            if (lo == int.MaxValue || hi - lo <= 52) return false;
            // Minimise Σ −e over permutations, zero entries excluded. Rows and columns from 1; column 0 is the method's
            // dummy. u and v end with u[i] + v[j] ≤ −e[i, j], equal on the chosen entries.
            var u = new long[n + 1];
            var v = new long[n + 1];
            var p = new int[n + 1];
            var way = new int[n + 1];
            var minv = new long[n + 1];
            var used = new bool[n + 1];
            for (int i = 1; i <= n; i++)
            {
                p[0] = i;
                int j0 = 0;
                for (int j = 0; j <= n; j++) { minv[j] = long.MaxValue; used[j] = false; }
                do
                {
                    used[j0] = true;
                    int i0 = p[j0], j1 = -1;
                    long delta = long.MaxValue;
                    for (int j = 1; j <= n; j++)
                    {
                        if (used[j]) continue;
                        if (e[i0 - 1, j - 1] != int.MinValue)
                        {
                            long cur = -(long)e[i0 - 1, j - 1] - u[i0] - v[j];
                            if (cur < minv[j]) { minv[j] = cur; way[j] = j0; }
                        }
                        if (minv[j] < delta) { delta = minv[j]; j1 = j; }
                    }
                    if (j1 < 0) return false; // no permutation of non-zero entries
                    for (int j = 0; j <= n; j++)
                        if (used[j]) { u[p[j]] += delta; v[j] -= delta; }
                        else if (minv[j] != long.MaxValue) minv[j] -= delta;
                    j0 = j1;
                } while (p[j0] != 0);
                do { int j1 = way[j0]; p[j0] = p[j1]; j0 = j1; } while (j0 != 0);
            }
            for (int i = 1; i <= n; i++)
                if (Math.Abs(u[i]) > int.MaxValue / 4 || Math.Abs(v[i]) > int.MaxValue / 4) return false;
            for (int i = 0; i < n; i++)
            {
                shift[i] = (int)-u[i + 1]; // e − shift − colShift = e + u + v ≤ 0
                colShift[i] = (int)-v[i + 1];
            }
            return true;
        }

        private static double[] SolveDecomposed(double[,] lu, int[] pivot, double[] b)
        {
            int n = lu.GetLength(0);
            var y = new double[n];
            for (int i = 0; i < n; i++)
            {
                double sum = b[pivot[i]];
                for (int j = 0; j < i; j++) sum -= lu[i, j] * y[j];
                y[i] = sum; // unit lower-triangular diagonal
            }
            var x = new double[n];
            for (int i = n - 1; i >= 0; i--)
            {
                double sum = y[i];
                for (int j = i + 1; j < n; j++) sum -= lu[i, j] * x[j];
                x[i] = sum / lu[i, i];
            }
            return x;
        }

        // floor(log2 x) for a positive finite x, subnormals included.
        private static int Exponent(double x)
        {
            int e = (int)((BitConverter.DoubleToInt64Bits(x) >> 52) & 0x7FF);
            return e == 0 ? Exponent(x * 18446744073709551616.0) - 64 : e - 1023;
        }

        // x·2^k without overflowing the factor itself (2^k alone is out of range for |k| > 1023), rounded once. Steps of
        // 2^±1022 are exact while the result is normal; one that went subnormal and was scaled again rounded twice, so a
        // result below the normal range is reached from 2^-1022 in a single multiplication.
        private static double ScaleB(double x, int k)
        {
            if (x == 0 || double.IsNaN(x) || double.IsInfinity(x)) return x;
            long kk = k;
            if (Math.Abs(x) < 2.2250738585072014E-308) { x *= 18446744073709551616.0; kk -= 64; } // subnormal: made normal, exactly
            int e = Exponent(Math.Abs(x));
            long target = e + kk; // the result's exponent
            if (target > 1023) return x > 0 ? double.PositiveInfinity : double.NegativeInfinity;
            if (target >= -1022) return ScaleSteps(x, kk);
            x = ScaleSteps(x, -1022 - e); // exactly ±[1, 2)·2^-1022
            long rest = target + 1022;
            return rest < -1022 ? x * 0.0 : x * Pow2((int)rest);
        }

        // Exact: x and x·2^k are normal, and so is every step between them.
        private static double ScaleSteps(double x, long k)
        {
            while (k > 1023) { x *= Pow2(1023); k -= 1023; }
            while (k < -1022) { x *= Pow2(-1022); k += 1022; }
            return x * Pow2((int)k);
        }

        private static double Pow2(int k) => BitConverter.Int64BitsToDouble((long)(k + 1023) << 52);

        // An array made with Array.CreateInstance and non-zero lower bounds was indexed from 0 and threw
        // IndexOutOfRangeException.
        private static void RequireZeroBased(double[,] a, string paramName)
        {
            if (a.GetLowerBound(0) != 0 || a.GetLowerBound(1) != 0)
                throw new ArgumentException("Matrix must be indexed from 0.", paramName);
        }

        private static int RequireSquare(double[,] a)
        {
            RequireZeroBased(a, nameof(a));
            int rows = a.GetLength(0), cols = a.GetLength(1);
            if (rows != cols || rows == 0) throw new ArgumentException("Matrix must be square and non-empty.", nameof(a));
            return rows;
        }
    }
}
