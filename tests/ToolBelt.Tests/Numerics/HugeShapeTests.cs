using System;
using System.Reflection;
using ToolBelt.Numerics;
using ToolBelt.Quality;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Numerics
{
    /// <summary>
    /// Regressions from round 46: the incomplete beta at shapes near MaxValue (overflow to NaN), at subnormal shapes, and
    /// the drop-in copies of the gamma/beta machinery in Anova and GaugeRR, which had missed round 45's fixes. Each test
    /// failed before its fix. References from mpmath at 50 digits (the gamma limit where b ≫ a).
    /// </summary>
    public sealed class HugeShapeTests
    {
        private static void Rel(double expected, double actual, double tol) =>
            Check.Close(expected, actual, tol * Math.Abs(expected));

        public void FCdf_HugeDenominatorDfIsNotNaN()
        {
            // F(1e10, 1e300) is χ²(1e10)/1e10, concentrated within ~1.4e-5 of 1; F(1e300, 1e300) within ~1e-150.
            Check.Equal(0.0, Distributions.FCdf(0.5, 1e10, 1e300));
            Check.Equal(1.0, Distributions.FSurvival(0.5, 1e10, 1e300));
            Check.Equal(0.0, Distributions.FCdf(0.5, 1e300, 1e300));
            Check.Equal(0.0, Distributions.FCdf(1e-300, 1e300, 1e300));
            Check.Equal(1.0, Distributions.FCdf(1.5, 1e10, 1e300));
            Check.Equal(0.5, Distributions.FCdf(1, 1e300, 1e300));
            Check.Equal(1.0, Anova.FUpperTailProbability(0.5, 1e10, 1e300));
        }

        public void FQuantile_HugeDfFindsTheMedianNearOne()
        {
            // Was 2.5e-34 and 0, searched against NaN CDFs. Median of χ²(k)/k by Wilson–Hilferty: (1 − 2/(9k))³.
            Rel(0.99999999993333333, Distributions.FQuantile(0.5, 1e10, 1e300), 1e-14);
            Rel(1.0, Distributions.FQuantile(0.5, 1e300, 1e300), 1e-15);
        }

        public void RegularizedBetaI_ShapeNearMaxValue()
        {
            // I_x(a, b) → P(a, b·x) as b → ∞: (b·x = 8.9e-16) P(2, ·) = 3.94e-31, P(½, ·) = 3.36e-8. Were NaN (m(b − m)
            // overflowed in the continued fraction).
            Rel(3.9443045261050558e-31, SpecialFunctions.RegularizedBetaI(5e-324, 2, double.MaxValue), 1e-13);
            Rel(3.3628319713339572e-8, SpecialFunctions.RegularizedBetaI(5e-324, 0.5, double.MaxValue), 1e-13);
            Check.Equal(0.0, SpecialFunctions.RegularizedBetaI(5e-324, 1e300, double.MaxValue));
            // a + b overflows: a step at the mode a/(a + b). Were NaN.
            Check.Equal(0.5, SpecialFunctions.RegularizedBetaI(0.5, double.MaxValue, double.MaxValue));
            Check.Equal(0.0, SpecialFunctions.RegularizedBetaI(5e-324, double.MaxValue, double.MaxValue));
            Check.Equal(0.0, SpecialFunctions.RegularizedBetaI(0.4, 8e307, 8e307));
            Check.Equal(1.0, SpecialFunctions.RegularizedBetaI(0.6, 8e307, 8e307));
            Check.Equal(0.0, SpecialFunctions.RegularizedBetaI(0.99, double.MaxValue, 1e306));   // mode 0.9945
            Check.Equal(1.0, SpecialFunctions.RegularizedBetaI(0.995, double.MaxValue, 1e306));
        }

        public void RegularizedBetaI_SubnormalShapesSplitTheMass()
        {
            // With a = b tiny the mass sits at 0 and 1 in equal parts: I ≈ b/(a + b) = ½. Was 0 (the prefix
            // x^a(1 − x)^b/B(a, b) ≈ 4e-324 underflowed before its division by a).
            Rel(0.5, SpecialFunctions.RegularizedBetaI(5e-324, 5e-324, 5e-324), 1e-12);
            Rel(0.5, SpecialFunctions.RegularizedBetaI(0.5, 5e-324, 5e-324), 1e-12);
        }

        // ---------- the drop-in copies agree ----------

        private static MethodInfo Private(Type type, string name) =>
            type.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException(type.Name + "." + name + " not found");

        private static (double, double) Call(MethodInfo m, params double[] args)
        {
            var all = new object[args.Length + 2];
            for (int i = 0; i < args.Length; i++) all[i] = args[i];
            m.Invoke(null, all);
            return ((double)all[args.Length], (double)all[args.Length + 1]);
        }

        private static readonly Type[] Copies = { typeof(Distributions), typeof(SpecialFunctions), typeof(Anova), typeof(GaugeRR) };

        // Anova and GaugeRR kept pre-round-45 copies: I_x(1e20, 1000) at x = 1 − 1e-20 gave a negative upper tail, and
        // Q(½, x) near the middle was 1 − P (a few digits lost).
        public void Copies_BetaIAgreeWithDistributions()
        {
            double[][] cases =
            {
                new[] { 0.5, 0.5, 1e20, 1000 }, new[] { 1 - 1e-12, 1e-12, 1e20, 1000 }, new[] { 0.3, 0.7, 1e-100, 1e-20 },
                new[] { 0.9, 0.1, 1e-20, 1e-100 }, new[] { 5e-291, 1, 5e9, 5e299 }, new[] { 0.5, 0.5, double.MaxValue, double.MaxValue },
                new[] { 1e-5, 1 - 1e-5, 2.5, 3.5 }, new[] { 0.4, 0.6, 50, 60 }, new[] { 0.01, 0.99, 5000, 3 },
                new[] { 0.7, 0.3, 1, 1e-20 }, new[] { 0.99, 0.01, 50, 1e-300 }, new[] { 0.01, 0.99, 1e-300, 50 },
                new[] { 0.9999999999, 1e-10, 1e5, 1e-300 }, new[] { 0.3, 0.7, 1e-20, 1 }, new[] { 1e-300, 1, 5, 1e300 },
            };
            var reference = Private(typeof(Distributions), "BetaI");
            foreach (var type in Copies)
            {
                var m = Private(type, "BetaI");
                foreach (var c in cases)
                {
                    var want = Call(reference, c); var got = Call(m, c);
                    Check.True(want.Equals(got), $"{type.Name}.BetaI({string.Join(", ", c)}) = {got}, Distributions gives {want}");
                }
            }
        }

        public void Copies_GammaPQAgreeWithDistributions()
        {
            double[][] cases =
            {
                new[] { 0.5, 0.3 }, new[] { 0.5, 1.2 }, new[] { 5e-9, 0.5 }, new[] { 5e-324, 0.5 }, new[] { 3.0, 2.0 }, new[] { 0.5, 40.0 }, new[] { 1e-300, 1e-10 }, new[] { 1e-20, 1e-10 },
            };
            var reference = Private(typeof(Distributions), "GammaPQ");
            foreach (var type in Copies)
            {
                var m = Private(type, "GammaPQ");
                foreach (var c in cases)
                {
                    var want = Call(reference, c); var got = Call(m, c);
                    Check.True(want.Equals(got), $"{type.Name}.GammaPQ({string.Join(", ", c)}) = {got}, Distributions gives {want}");
                }
            }
        }
    }
}
