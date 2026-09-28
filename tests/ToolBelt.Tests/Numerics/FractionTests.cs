using System;
using System.Numerics;
using ToolBelt.Numerics;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Numerics
{
    public sealed class FractionTests
    {
        public void ReducesToLowestTerms()
        {
            var f = new Fraction(6, 8);
            Check.Equal((BigInteger)3, f.Numerator);
            Check.Equal((BigInteger)4, f.Denominator);
        }

        public void NormalizesSignToNumerator()
        {
            var f = new Fraction(1, -2);
            Check.Equal((BigInteger)(-1), f.Numerator);
            Check.Equal((BigInteger)2, f.Denominator);
            Check.Equal(-1, f.Sign);
        }

        public void Arithmetic()
        {
            Check.Equal(new Fraction(5, 6), new Fraction(1, 2) + new Fraction(1, 3));
            Check.Equal(new Fraction(1, 6), new Fraction(1, 2) - new Fraction(1, 3));
            Check.Equal(new Fraction(1, 6), new Fraction(1, 2) * new Fraction(1, 3));
            Check.Equal(new Fraction(3, 2), new Fraction(1, 2) / new Fraction(1, 3));
            Check.Equal(new Fraction(-1, 2), -new Fraction(1, 2));
        }

        public void Comparison()
        {
            Check.True(new Fraction(1, 3) < new Fraction(1, 2));
            Check.True(new Fraction(2, 4) == new Fraction(1, 2));
            Check.True(new Fraction(3, 2) > Fraction.One);
        }

        public void ReciprocalAndDouble()
        {
            Check.Equal(new Fraction(3, 2), new Fraction(2, 3).Reciprocal());
            Check.Close(0.75, new Fraction(3, 4).ToDouble(), 1e-12);
        }

        public void ParseAndToString()
        {
            Check.Equal(new Fraction(3, 4), Fraction.Parse("6/8"));
            Check.Equal(Fraction.FromInteger(5), Fraction.Parse("5"));
            Check.Equal("3/4", new Fraction(3, 4).ToString());
            Check.Equal("5", Fraction.FromInteger(5).ToString());
        }

        public void ZeroDenominator_Throws()
        {
            Check.Throws<DivideByZeroException>(() => new Fraction(1, 0));
            Check.Throws<DivideByZeroException>(() => { var _ = new Fraction(1, 2) / Fraction.Zero; });
            Check.Throws<DivideByZeroException>(() => Fraction.Zero.Reciprocal());
        }

        public void BadParse_Throws()
        {
            Check.Throws<FormatException>(() => Fraction.Parse("abc"));
            Check.Throws<FormatException>(() => Fraction.Parse("1/x"));
            Check.Throws<ArgumentNullException>(() => Fraction.Parse(null!));
        }

        // Property: (a op b) evaluated exactly matches the same op on doubles, within FP tolerance.
        public void Property_MatchesDoubleArithmetic()
        {
            var rng = new Random(28);
            for (int t = 0; t < 2000; t++)
            {
                int an = rng.Next(-20, 21), ad = rng.Next(1, 21);
                int bn = rng.Next(-20, 21), bd = rng.Next(1, 21);
                var a = new Fraction(an, ad);
                var b = new Fraction(bn, bd);

                Check.Close((double)an / ad + (double)bn / bd, (a + b).ToDouble(), 1e-9, $"t{t} add");
                Check.Close((double)an / ad * bn / bd, (a * b).ToDouble(), 1e-9, $"t{t} mul");
                if (bn != 0)
                    Check.Close((double)an / ad / ((double)bn / bd), (a / b).ToDouble(), 1e-9, $"t{t} div");
            }
        }
    }
}
