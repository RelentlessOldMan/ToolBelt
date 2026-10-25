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
            if (n < 1) throw new ArgumentOutOfRangeException(nameof(n), n, "Size must be positive.");
            var m = new double[n, n];
            for (int i = 0; i < n; i++) m[i, i] = 1;
            return m;
        }

        public static double[,] Transpose(double[,] a)
        {
            if (a is null) throw new ArgumentNullException(nameof(a));
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
            int ar = a.GetLength(0), ac = a.GetLength(1), br = b.GetLength(0), bc = b.GetLength(1);
            if (ac != br) throw new ArgumentException($"Cannot multiply {ar}x{ac} by {br}x{bc}.");
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
            int rows = a.GetLength(0), cols = a.GetLength(1);
            if (cols != x.Length) throw new ArgumentException($"Cannot multiply {rows}x{cols} matrix by length-{x.Length} vector.");
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
            if (b.Length != n) throw new ArgumentException("Right-hand side length must match the matrix size.");
            if (!Decompose(a, out double[,] lu, out int[] pivot, out _, out int[] shift))
                throw new InvalidOperationException("Matrix is singular; the system has no unique solution.");
            var scaled = new double[n];
            for (int i = 0; i < n; i++) scaled[i] = ScaleB(b[i], -shift[i]); // the rows were scaled by 2^−shift
            return SolveDecomposed(lu, pivot, scaled);
        }

        public static double Determinant(double[,] a)
        {
            if (a is null) throw new ArgumentNullException(nameof(a));
            int n = RequireSquare(a);
            // Only an exactly zero pivot makes the determinant 0; a small one is a small (but real) determinant.
            if (!Decompose(a, out double[,] lu, out _, out int sign, out int[] shift, exactZeroOnly: true))
                return 0.0;
            // The running product kept as mantissa · 2^exponent: multiplied directly, 1e200 · 1e200 · 1e-200 · 1e-200
            // overflowed to ∞ on the way to 1. Only the final scale saturates (to ±∞ or 0). Each pivot is split too:
            // a mantissa in [1, 2) times a pivot of 1.5e308 overflowed before it was rescaled.
            double mantissa = sign;
            int exponent = 0;
            foreach (int s in shift) exponent += s; // det A = det(row-scaled A) · 2^Σshift
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
            if (!Decompose(a, out double[,] lu, out int[] pivot, out _, out int[] shift))
                throw new InvalidOperationException("Matrix is singular and cannot be inverted.");

            var inverse = new double[n, n];
            var e = new double[n];
            for (int col = 0; col < n; col++)
            {
                Array.Clear(e, 0, n);
                e[col] = ScaleB(1, -shift[col]); // A⁻¹ = (DA)⁻¹·D for the row scaling D
                double[] x = SolveDecomposed(lu, pivot, e);
                for (int row = 0; row < n; row++) inverse[row, col] = x[row];
            }
            return inverse;
        }

        // LU with partial pivoting. Returns false if the matrix is singular: a pivot at or below n·ε times the largest
        // entry of its own original row, i.e. what is left once elimination has cancelled that row away (relative per
        // row, so neither a uniformly tiny matrix nor diag(1e20, 1) is "singular"; one global scale rejected the
        // latter), or exactly zero when exactZeroOnly. Non-finite entries throw. A row whose largest entry is beyond 2⁹⁶⁰
        // or below 2⁻⁹⁶⁰ is first scaled by 2^−shift[row] (exactly) to bring it nearer 1: elimination overflowed on rows
        // near double.MaxValue (1e308 − (−1e308)) and lost subnormal ones.
        private static bool Decompose(double[,] a, out double[,] lu, out int[] pivot, out int sign, out int[] shift,
            bool exactZeroOnly = false)
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
            for (int i = 0; i < n; i++)
            {
                if (rowMax[i] == 0) continue;
                int e = Exponent(rowMax[i]);
                if (e >= 960) shift[i] = e - 959;   // largest entry into [2⁹⁵⁹, 2⁹⁶⁰): room for elimination to grow
                else if (e < -960) shift[i] = e;    // largest entry into [1, 2)
                else continue;
                for (int j = 0; j < n; j++) lu[i, j] = ScaleB(lu[i, j], -shift[i]);
                rowMax[i] = ScaleB(rowMax[i], -shift[i]);
            }
            var colMax = new double[n];
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++) colMax[j] = Math.Max(colMax[j], Math.Abs(lu[i, j]));
            double relative = exactZeroOnly ? 0 : n * 2.220446049250313e-16;

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

                if (max != col)
                {
                    for (int j = 0; j < n; j++) (lu[col, j], lu[max, j]) = (lu[max, j], lu[col, j]);
                    (pivot[col], pivot[max]) = (pivot[max], pivot[col]);
                    (rowMax[col], rowMax[max]) = (rowMax[max], rowMax[col]);
                    sign = -sign;
                }

                for (int row = col + 1; row < n; row++)
                {
                    double factor = lu[row, col] / lu[col, col];
                    lu[row, col] = factor;
                    for (int j = col + 1; j < n; j++) lu[row, j] -= factor * lu[col, j];
                }
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

        // x·2^k without overflowing the factor itself (2^k alone is out of range for |k| > 1023).
        private static double ScaleB(double x, int k)
        {
            while (k > 1023) { x *= Pow2(1023); k -= 1023; }
            while (k < -1022) { x *= Pow2(-1022); k += 1022; }
            return x * Pow2(k);
        }

        private static double Pow2(int k) => BitConverter.Int64BitsToDouble((long)(k + 1023) << 52);

        private static int RequireSquare(double[,] a)
        {
            int rows = a.GetLength(0), cols = a.GetLength(1);
            if (rows != cols || rows == 0) throw new ArgumentException("Matrix must be square and non-empty.", nameof(a));
            return rows;
        }
    }
}
