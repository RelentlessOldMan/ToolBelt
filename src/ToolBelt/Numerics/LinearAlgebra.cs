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
            if (!Decompose(a, out double[,] lu, out int[] pivot, out _))
                throw new InvalidOperationException("Matrix is singular; the system has no unique solution.");
            return SolveDecomposed(lu, pivot, b);
        }

        public static double Determinant(double[,] a)
        {
            if (a is null) throw new ArgumentNullException(nameof(a));
            int n = RequireSquare(a);
            if (!Decompose(a, out double[,] lu, out _, out int sign))
                return 0.0; // singular
            double det = sign;
            for (int i = 0; i < n; i++) det *= lu[i, i];
            return det;
        }

        public static double[,] Inverse(double[,] a)
        {
            if (a is null) throw new ArgumentNullException(nameof(a));
            int n = RequireSquare(a);
            if (!Decompose(a, out double[,] lu, out int[] pivot, out _))
                throw new InvalidOperationException("Matrix is singular and cannot be inverted.");

            var inverse = new double[n, n];
            var e = new double[n];
            for (int col = 0; col < n; col++)
            {
                Array.Clear(e, 0, n);
                e[col] = 1;
                double[] x = SolveDecomposed(lu, pivot, e);
                for (int row = 0; row < n; row++) inverse[row, col] = x[row];
            }
            return inverse;
        }

        // LU with partial pivoting. Returns false if the matrix is singular.
        private static bool Decompose(double[,] a, out double[,] lu, out int[] pivot, out int sign)
        {
            int n = a.GetLength(0);
            lu = (double[,])a.Clone();
            pivot = new int[n];
            for (int i = 0; i < n; i++) pivot[i] = i;
            sign = 1;

            for (int col = 0; col < n; col++)
            {
                int max = col;
                double best = Math.Abs(lu[col, col]);
                for (int row = col + 1; row < n; row++)
                {
                    double v = Math.Abs(lu[row, col]);
                    if (v > best) { best = v; max = row; }
                }
                if (best < 1e-14) return false; // singular

                if (max != col)
                {
                    for (int j = 0; j < n; j++) (lu[col, j], lu[max, j]) = (lu[max, j], lu[col, j]);
                    (pivot[col], pivot[max]) = (pivot[max], pivot[col]);
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

        private static int RequireSquare(double[,] a)
        {
            int rows = a.GetLength(0), cols = a.GetLength(1);
            if (rows != cols || rows == 0) throw new ArgumentException("Matrix must be square and non-empty.");
            return rows;
        }
    }
}
