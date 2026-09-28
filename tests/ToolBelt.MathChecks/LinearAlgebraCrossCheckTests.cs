using System;
using MathNet.Numerics.LinearAlgebra;
using ToolBelt.Numerics;
using ToolBelt.Tests.Framework;
using TbLinearAlgebra = ToolBelt.Numerics.LinearAlgebra;

namespace ToolBelt.MathChecks
{
    /// <summary>
    /// Grades ToolBelt's Gaussian-elimination linear algebra (Solve/Determinant/Inverse) against
    /// Math.NET's LU-based routines over random well-conditioned systems.
    /// </summary>
    public sealed class LinearAlgebraCrossCheckTests
    {
        // A random matrix with a fat diagonal, so it stays well conditioned.
        private static double[,] RandomMatrix(Random rng, int n)
        {
            var a = new double[n, n];
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                    a[i, j] = rng.NextDouble() * 2 - 1;
            for (int i = 0; i < n; i++) a[i, i] += n; // diagonal dominance
            return a;
        }

        public void Solve_MatchesMathNet()
        {
            var rng = new Random(20);
            for (int t = 0; t < 100; t++)
            {
                int n = rng.Next(2, 7);
                var a = RandomMatrix(rng, n);
                var b = new double[n];
                for (int i = 0; i < n; i++) b[i] = rng.NextDouble() * 10 - 5;

                var mine = TbLinearAlgebra.Solve(a, b);

                var mA = Matrix<double>.Build.DenseOfArray(a);
                var mB = Vector<double>.Build.DenseOfArray(b);
                var theirs = mA.Solve(mB);

                for (int i = 0; i < n; i++) Check.Close(theirs[i], mine[i], 1e-8, $"t{t} n={n}[{i}]");
            }
        }

        public void Determinant_MatchesMathNet()
        {
            var rng = new Random(21);
            for (int t = 0; t < 100; t++)
            {
                int n = rng.Next(2, 7);
                var a = RandomMatrix(rng, n);
                double mine = TbLinearAlgebra.Determinant(a);
                double theirs = Matrix<double>.Build.DenseOfArray(a).Determinant();
                // Determinants of dominant matrices grow with n; use a relative tolerance.
                Check.Close(theirs, mine, Math.Abs(theirs) * 1e-8 + 1e-8, $"t{t} n={n}");
            }
        }

        public void Inverse_MatchesMathNet()
        {
            var rng = new Random(22);
            for (int t = 0; t < 100; t++)
            {
                int n = rng.Next(2, 7);
                var a = RandomMatrix(rng, n);
                var mine = TbLinearAlgebra.Inverse(a);
                var theirs = Matrix<double>.Build.DenseOfArray(a).Inverse();
                for (int i = 0; i < n; i++)
                    for (int j = 0; j < n; j++)
                        Check.Close(theirs[i, j], mine[i, j], 1e-8, $"t{t} n={n}[{i},{j}]");
            }
        }
    }
}
