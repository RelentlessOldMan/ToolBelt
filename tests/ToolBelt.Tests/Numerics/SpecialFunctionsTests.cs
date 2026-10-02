using System;
using ToolBelt.Numerics;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Numerics
{
    public sealed class SpecialFunctionsTests
    {
        public void LnGamma_KnownValues()
        {
            Check.Close(0.0, SpecialFunctions.LnGamma(1), 1e-12);         // 0! = 1
            Check.Close(0.0, SpecialFunctions.LnGamma(2), 1e-12);         // 1! = 1
            Check.Close(Math.Log(2), SpecialFunctions.LnGamma(3), 1e-12); // 2! = 2
            Check.Close(Math.Log(24), SpecialFunctions.LnGamma(5), 1e-12);// 4! = 24
            Check.Close(0.5 * Math.Log(Math.PI), SpecialFunctions.LnGamma(0.5), 1e-12); // Γ(1/2)=√π
        }

        public void Gamma_KnownValues()
        {
            Check.Close(1.0, SpecialFunctions.Gamma(1), 1e-11);
            Check.Close(24.0, SpecialFunctions.Gamma(5), 1e-9);
            Check.Close(120.0, SpecialFunctions.Gamma(6), 1e-8);
            Check.Close(Math.Sqrt(Math.PI), SpecialFunctions.Gamma(0.5), 1e-11);
            Check.Close(-2 * Math.Sqrt(Math.PI), SpecialFunctions.Gamma(-0.5), 1e-10); // reflection
        }

        public void Gamma_NonPositiveInteger_Throws()
        {
            Check.Throws<ArgumentException>(() => SpecialFunctions.Gamma(0));
            Check.Throws<ArgumentException>(() => SpecialFunctions.Gamma(-3));
        }

        public void Erf_KnownValuesAndSymmetry()
        {
            Check.Close(0.0, SpecialFunctions.Erf(0), 1e-12);
            Check.Close(0.8427007929497149, SpecialFunctions.Erf(1), 1e-10);
            Check.Close(-SpecialFunctions.Erf(1), SpecialFunctions.Erf(-1), 1e-12); // odd function
            Check.Close(1.0, SpecialFunctions.Erf(6), 1e-12);                        // saturates
            Check.Close(1.0, SpecialFunctions.Erfc(0), 1e-12);
            Check.Close(1.0, SpecialFunctions.Erf(2) + SpecialFunctions.Erfc(2), 1e-12);
        }

        public void RegularizedGammaP_MatchesExponentialClosedForm()
        {
            // P(1, x) = 1 - e^{-x}.
            foreach (double x in new[] { 0.5, 1.0, 2.5, 7.0 })
                Check.Close(1 - Math.Exp(-x), SpecialFunctions.RegularizedGammaP(1, x), 1e-12, $"x={x}");
            Check.Equal(0.0, SpecialFunctions.RegularizedGammaP(3, 0));  // P(a,0)=0
            Check.Close(1.0, SpecialFunctions.RegularizedGammaP(3, 200), 1e-12); // saturates
        }

        public void RegularizedGammaP_And_Q_SumToOne()
        {
            foreach (double a in new[] { 0.5, 2.0, 7.5 })
                foreach (double x in new[] { 0.1, 3.0, 12.0 })
                    Check.Close(1.0, SpecialFunctions.RegularizedGammaP(a, x) + SpecialFunctions.RegularizedGammaQ(a, x), 1e-12, $"a={a} x={x}");
        }

        public void RegularizedBetaI_ClosedForms()
        {
            // I_x(1,1) = x ; I_x(2,1) = x^2 ; I_x(1,2) = 1-(1-x)^2.
            foreach (double x in new[] { 0.0, 0.25, 0.5, 0.75, 1.0 })
            {
                Check.Close(x, SpecialFunctions.RegularizedBetaI(x, 1, 1), 1e-12, $"I(x,1,1) x={x}");
                Check.Close(x * x, SpecialFunctions.RegularizedBetaI(x, 2, 1), 1e-12, $"I(x,2,1) x={x}");
                Check.Close(1 - (1 - x) * (1 - x), SpecialFunctions.RegularizedBetaI(x, 1, 2), 1e-12, $"I(x,1,2) x={x}");
            }
        }

        public void RegularizedBetaI_ReflectionSymmetry()
        {
            // I_x(a,b) + I_{1-x}(b,a) = 1.
            foreach (double x in new[] { 0.2, 0.5, 0.9 })
                Check.Close(1.0,
                    SpecialFunctions.RegularizedBetaI(x, 2.5, 4.0) + SpecialFunctions.RegularizedBetaI(1 - x, 4.0, 2.5),
                    1e-12, $"x={x}");
        }

        public void Beta_KnownValue()
        {
            Check.Close(1.0 / 12.0, SpecialFunctions.Beta(2, 3), 1e-12); // Γ2Γ3/Γ5 = 1·2/24
        }

        public void Validation_Throws()
        {
            Check.Throws<ArgumentOutOfRangeException>(() => SpecialFunctions.LnGamma(0));
            Check.Throws<ArgumentOutOfRangeException>(() => SpecialFunctions.RegularizedGammaP(0, 1));
            Check.Throws<ArgumentOutOfRangeException>(() => SpecialFunctions.RegularizedGammaP(1, -1));
            Check.Throws<ArgumentOutOfRangeException>(() => SpecialFunctions.RegularizedBetaI(0.5, 0, 1));
            Check.Throws<ArgumentOutOfRangeException>(() => SpecialFunctions.RegularizedBetaI(1.5, 1, 1));
        }
    }
}
