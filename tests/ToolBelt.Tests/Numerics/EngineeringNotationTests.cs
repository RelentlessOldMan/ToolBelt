using System;
using ToolBelt.Numerics;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Numerics
{
    public sealed class EngineeringNotationTests
    {
        public void FormatKnownValues()
        {
            Check.Equal("1.5 k", EngineeringNotation.Format(1500));
            Check.Equal("2.2 m", EngineeringNotation.Format(0.0022));
            Check.Equal("3.3 M", EngineeringNotation.Format(3.3e6));
            Check.Equal("750", EngineeringNotation.Format(750));
            Check.Equal("0", EngineeringNotation.Format(0));
            Check.Equal("-4.7 µ", EngineeringNotation.Format(-4.7e-6));
        }

        public void FormatRenormalizesAtBoundary()
        {
            // 999.95 rounded to 3 sig figs is 1000 -> should become "1 k", not "1e+03".
            Check.Equal("1 k", EngineeringNotation.Format(999.95, 3));
        }

        public void ParseKnownValues()
        {
            Check.Close(1500, EngineeringNotation.Parse("1.5 k"), 1e-9);
            Check.Close(1500, EngineeringNotation.Parse("1.5k"), 1e-9);      // no space
            Check.Close(0.0022, EngineeringNotation.Parse("2.2 m"), 1e-9);
            Check.Close(4.7e-6, EngineeringNotation.Parse("4.7u"), 1e-15);   // ASCII micro
            Check.Close(4.7e-6, EngineeringNotation.Parse("4.7 µ"), 1e-15);
            Check.Close(2000, EngineeringNotation.Parse("2 K"), 1e-9);       // lenient uppercase K
            Check.Close(750, EngineeringNotation.Parse("750"), 1e-9);
        }

        public void ParseInvalid()
        {
            Check.False(EngineeringNotation.TryParse("", out _));
            Check.False(EngineeringNotation.TryParse("abc", out _));
            Check.False(EngineeringNotation.TryParse("1.2 Q", out _)); // Q is not a prefix
            Check.Throws<FormatException>(() => EngineeringNotation.Parse("nonsense"));
        }

        public void BadSignificantDigits_Throws()
        {
            Check.Throws<ArgumentOutOfRangeException>(() => EngineeringNotation.Format(1.0, 0));
        }

        // Round-trip: Parse(Format(x)) recovers x to the chosen precision, across many magnitudes.
        public void Property_RoundTrip()
        {
            var rng = new Random(36);
            for (int t = 0; t < 5000; t++)
            {
                double mantissa = rng.NextDouble() * 2 - 1;         // [-1, 1)
                int exp = rng.Next(-20, 21);
                double value = mantissa * Math.Pow(10, exp);
                if (value == 0) continue;

                string formatted = EngineeringNotation.Format(value, 6);
                double parsed = EngineeringNotation.Parse(formatted);
                double relative = Math.Abs(parsed - value) / Math.Abs(value);
                Check.True(relative < 1e-4, $"t{t}: {value} -> '{formatted}' -> {parsed} (rel {relative})");
            }
        }
    }
}
