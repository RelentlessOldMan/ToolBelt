using System;
using ToolBelt.Numerics;
using ToolBelt.Tests.Framework;
using MnFindRoots = MathNet.Numerics.RootFinding.Brent;

namespace ToolBelt.MathChecks
{
    /// <summary>
    /// Grades ToolBelt's bracketing root-finders (Bisection, Brent) against Math.NET's Brent solver on a
    /// spread of transcendental and polynomial functions.
    /// </summary>
    public sealed class RootFindingCrossCheckTests
    {
        private static readonly (Func<double, double> F, double A, double B)[] Cases =
        {
            (x => x * x - 2, 0, 2),                    // sqrt(2)
            (x => Math.Cos(x) - x, 0, 1),              // Dottie number
            (x => Math.Exp(x) - 3 * x, 0, 1),          // exp(x)=3x lower root
            (x => x * x * x - x - 2, 1, 2),            // real cubic root
            (x => Math.Sin(x) - 0.5, 0, 1),            // asin(0.5)
            (x => Math.Log(x) - 1, 1, 4),              // e
        };

        public void Brent_MatchesMathNet()
        {
            for (int i = 0; i < Cases.Length; i++)
            {
                var (f, a, b) = Cases[i];
                var mine = RootFinding.Brent(f, a, b, 1e-12);
                Check.True(mine.Converged, $"case {i} converged");
                double theirs = MnFindRoots.FindRoot(f, a, b, 1e-12, 200);
                Check.Close(theirs, mine.Root, 1e-8, $"case {i}");
            }
        }

        public void Bisection_MatchesMathNet()
        {
            for (int i = 0; i < Cases.Length; i++)
            {
                var (f, a, b) = Cases[i];
                var mine = RootFinding.Bisection(f, a, b, 1e-12);
                Check.True(mine.Converged, $"case {i} converged");
                double theirs = MnFindRoots.FindRoot(f, a, b, 1e-12, 200);
                Check.Close(theirs, mine.Root, 1e-6, $"case {i}");
            }
        }
    }
}
