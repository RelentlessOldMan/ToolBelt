using System;
using ToolBelt.Numerics;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Numerics
{
    public sealed class LinearAlgebraTests
    {
        public void SolveKnownSystem()
        {
            // 2x + y = 5 ; x + 3y = 10  ->  x = 1, y = 3
            var a = new double[,] { { 2, 1 }, { 1, 3 } };
            var x = LinearAlgebra.Solve(a, new double[] { 5, 10 });
            Check.Close(1, x[0], 1e-9);
            Check.Close(3, x[1], 1e-9);
        }

        public void Determinant()
        {
            Check.Close(-2, LinearAlgebra.Determinant(new double[,] { { 1, 2 }, { 3, 4 } }), 1e-9);
            Check.Close(0, LinearAlgebra.Determinant(new double[,] { { 1, 2 }, { 2, 4 } }), 1e-9); // singular
        }

        public void InverseTimesMatrixIsIdentity()
        {
            var a = new double[,] { { 4, 7 }, { 2, 6 } };
            var inv = LinearAlgebra.Inverse(a);
            var product = LinearAlgebra.Multiply(a, inv);
            Check.Close(1, product[0, 0], 1e-9);
            Check.Close(0, product[0, 1], 1e-9);
            Check.Close(0, product[1, 0], 1e-9);
            Check.Close(1, product[1, 1], 1e-9);
        }

        public void MultiplyAndTranspose()
        {
            var a = new double[,] { { 1, 2, 3 } };           // 1x3
            var b = new double[,] { { 1 }, { 0 }, { -1 } };  // 3x1
            var c = LinearAlgebra.Multiply(a, b);            // 1x1 = 1*1 + 3*-1 = -2
            Check.Close(-2, c[0, 0], 1e-9);
            var t = LinearAlgebra.Transpose(a);
            Check.Equal(3, t.GetLength(0));
            Check.Equal(1, t.GetLength(1));
        }

        public void SingularAndBadDimensions_Throw()
        {
            Check.Throws<InvalidOperationException>(() => LinearAlgebra.Solve(new double[,] { { 1, 2 }, { 2, 4 } }, new double[] { 1, 2 }));
            Check.Throws<InvalidOperationException>(() => LinearAlgebra.Inverse(new double[,] { { 0, 0 }, { 0, 0 } }));
            Check.Throws<ArgumentException>(() => LinearAlgebra.Multiply(new double[,] { { 1, 2 } }, new double[,] { { 1, 2 } }));
            Check.Throws<ArgumentException>(() => LinearAlgebra.Solve(new double[,] { { 1, 2, 3 } }, new double[] { 1 })); // non-square
        }

        // Differential: for a random invertible A and vector x, Solve(A, A·x) recovers x.
        public void Property_SolveRecoversX()
        {
            var rng = new Random(43);
            for (int trial = 0; trial < 1000; trial++)
            {
                int n = rng.Next(2, 6);
                var a = new double[n, n];
                for (int i = 0; i < n; i++)
                {
                    for (int j = 0; j < n; j++) a[i, j] = rng.NextDouble() * 10 - 5;
                    a[i, i] += n * 6; // diagonally dominant -> non-singular
                }
                var x = new double[n];
                for (int i = 0; i < n; i++) x[i] = rng.NextDouble() * 20 - 10;

                var b = LinearAlgebra.Multiply(a, x);
                var solved = LinearAlgebra.Solve(a, b);
                for (int i = 0; i < n; i++) Check.Close(x[i], solved[i], 1e-6, $"trial {trial} component {i}");
            }
        }
    }
}
