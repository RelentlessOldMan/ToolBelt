using System;
using ToolBelt.Numerics;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Numerics
{
    /// <summary>
    /// Regressions from round 47's differential harness (tests/ToolBelt.MathChecks, graded against mpmath in units of the
    /// function's condition number). Each test failed before its fix. References from mpmath at 50 digits.
    /// </summary>
    public sealed class DifferentialHarnessTests
    {
        private static void Rel(double expected, double actual, double tol) =>
            Check.Close(expected, actual, tol * Math.Abs(expected));

        // With a < 1 and P > ½, P came from the series, whose prefix carries lnΓ(a)·eps (lnΓ(1e-300) = 690): P(1e-300,
        // 1e-10) was 0.9999999999999791, 94 eps from a value whose condition number is ~0. P is now 1 − Q.
        public void GammaP_TinyShapeNearOneIsExact()
        {
            Check.Equal(1.0, SpecialFunctions.RegularizedGammaP(1e-300, 1e-10));          // 1 − 2.24e-299
            Check.Equal(1.0, SpecialFunctions.RegularizedGammaP(1e-20, 1e-10));           // 1 − 2.24e-19
            Rel(0.99977553876029667291, SpecialFunctions.RegularizedGammaP(1e-5, 1e-10), 4e-16);
            Rel(2.2448635265138924506e-299, SpecialFunctions.RegularizedGammaQ(1e-300, 1e-10), 4e-15);
            Check.Equal(1.0, Distributions.ChiSquareCdf(2e-10, 2e-300));
        }

        // A small b puts the mass near 1, so above the split I_x(a, b) is the small tail, and it was formed as 1 − the
        // complement: I_0.7(1, 1e-20) came out 2e-14, I_0.7(1e-5, 1e-20) ten times too big. Now summed directly.
        public void BetaI_SmallTailOfATinyShapeIsDirect()
        {
            Rel(1.2039728043259357786e-20, SpecialFunctions.RegularizedBetaI(0.7, 1, 1e-20), 1e-15);
            Rel(1.0000084728530842968e-15, SpecialFunctions.RegularizedBetaI(0.7, 1e-5, 1e-20), 1e-15);
            Rel(5.6281241533420569642e-301, SpecialFunctions.RegularizedBetaI(0.99, 50, 1e-300), 1e-14);
            Rel(1.0732456253820705374e-299, SpecialFunctions.RegularizedBetaI(0.7, 0.1, 1e-300), 1e-14);
            Rel(1.0935724717212489788e-299, SpecialFunctions.RegularizedBetaI(0.9999999999, 1e5, 1e-300), 1e-14);  // BGRAT
            Rel(0.0058112696719893811198, SpecialFunctions.RegularizedBetaI(0.8982035928143713, 5, 0.01), 4e-15);
            // The mirror: a tiny a puts the mass near 0, and the upper tail is the small one.
            // FSurvival(f, 2e-20, 2) = I_y(1, 1e-20) at y = 2/(2e-20·f + 2) = 0.7 (to a few ulps, condition number ~2).
            Rel(1.2039728043259357786e-20, Distributions.FSurvival(3.0 / 7 * 1e20, 2e-20, 2), 1e-14);
        }

        // 1/B(a, b) carries a factor min(a, b), and inside the exponential ln(1e-300) = −690.8 rounded to ~250 eps of
        // the result: I_0.01(1e-300, 50) was 1 − 2.8e-14, Q(a, x) and BGRAT's terms likewise. Now applied outside.
        public void TinyShapePrefixIsExact()
        {
            Check.Equal(1.0, SpecialFunctions.RegularizedBetaI(0.01, 1e-300, 50));
            Rel(4.8900510708061120793e-302, SpecialFunctions.RegularizedGammaQ(1e-300, 2), 2e-15);
            Rel(3.783264029550458951e-224, SpecialFunctions.RegularizedGammaQ(1e-200, 50), 2e-15);
            Rel(1.3048381094197037673e-102, SpecialFunctions.RegularizedGammaQ(1e-100, 3), 2e-15);
            Rel(6.065306597126334388e-301, Distributions.ChiSquarePdf(1, 2e-300), 2e-15);
        }

        // With b ≫ a the prefix's p·ln x and p·ln(a + b) were each ~1700 and cancelled (FCdf(0.5, 5, 1e300) off by
        // 1.0e-13); merged as p·ln(x·(a + b)).
        public void BetaI_HugeOtherShapePrefixDoesNotCancel()
        {
            Rel(0.22350492887667729, Distributions.FCdf(0.5, 5, 1e300), 4e-15);
        }
    }
}
