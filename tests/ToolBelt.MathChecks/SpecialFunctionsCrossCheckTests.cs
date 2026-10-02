using ToolBelt.Numerics;
using ToolBelt.Tests.Framework;
using Mn = MathNet.Numerics.SpecialFunctions;

namespace ToolBelt.MathChecks
{
    /// <summary>
    /// Grades ToolBelt's from-scratch special functions against Math.NET Numerics — the trickiest numerics
    /// in the library, and the foundation under the hypothesis tests.
    /// </summary>
    public sealed class SpecialFunctionsCrossCheckTests
    {
        private static readonly DeterministicRandom Rng = new DeterministicRandom(20261002);

        public void LnGamma_MatchesMathNet()
        {
            for (int t = 0; t < 500; t++)
            {
                double x = Rng.NextDouble() * 50 + 1e-3;
                Check.Close(Mn.GammaLn(x), SpecialFunctions.LnGamma(x), 1e-10, $"x={x}");
            }
        }

        public void Gamma_MatchesMathNet()
        {
            for (int t = 0; t < 500; t++)
            {
                double x = Rng.NextDouble() * 15 + 1e-2;
                Check.Close(Mn.Gamma(x), SpecialFunctions.Gamma(x), 1e-8 * (1 + System.Math.Abs(Mn.Gamma(x))), $"x={x}");
            }
        }

        public void Erf_Erfc_MatchMathNet()
        {
            for (int t = 0; t < 500; t++)
            {
                double x = (Rng.NextDouble() - 0.5) * 8;
                Check.Close(Mn.Erf(x), SpecialFunctions.Erf(x), 1e-10, $"erf x={x}");
                Check.Close(Mn.Erfc(x), SpecialFunctions.Erfc(x), 1e-10, $"erfc x={x}");
            }
        }

        public void RegularizedGamma_MatchesMathNet()
        {
            for (int t = 0; t < 500; t++)
            {
                double a = Rng.NextDouble() * 30 + 0.05;
                double x = Rng.NextDouble() * 40;
                Check.Close(Mn.GammaLowerRegularized(a, x), SpecialFunctions.RegularizedGammaP(a, x), 1e-9, $"P a={a} x={x}");
                Check.Close(Mn.GammaUpperRegularized(a, x), SpecialFunctions.RegularizedGammaQ(a, x), 1e-9, $"Q a={a} x={x}");
            }
        }

        public void RegularizedBeta_MatchesMathNet()
        {
            for (int t = 0; t < 500; t++)
            {
                double a = Rng.NextDouble() * 20 + 0.05;
                double b = Rng.NextDouble() * 20 + 0.05;
                double x = Rng.NextDouble();
                Check.Close(Mn.BetaRegularized(a, b, x), SpecialFunctions.RegularizedBetaI(x, a, b), 1e-9, $"a={a} b={b} x={x}");
            }
        }
    }
}
