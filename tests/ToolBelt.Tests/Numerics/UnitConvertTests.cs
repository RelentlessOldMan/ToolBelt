using System;
using ToolBelt.Numerics;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Numerics
{
    public sealed class UnitConvertTests
    {
        public void Length()
        {
            Check.Close(1000, UnitConvert.Convert(1, "km", "m"), 1e-9);
            Check.Close(1, UnitConvert.Convert(1000, "m", "km"), 1e-9);
            Check.Close(1.609344, UnitConvert.Convert(1, "mi", "km"), 1e-9);
            Check.Close(12, UnitConvert.Convert(1, "ft", "in"), 1e-9);
        }

        public void Mass()
        {
            Check.Close(1, UnitConvert.Convert(1000, "g", "kg"), 1e-9);
            Check.Close(1000, UnitConvert.Convert(1, "t", "kg"), 1e-9);
        }

        public void Time()
        {
            Check.Close(120, UnitConvert.Convert(2, "h", "min"), 1e-9);
            Check.Close(1000, UnitConvert.Convert(1, "s", "ms"), 1e-9);
        }

        public void Angle()
        {
            Check.Close(Math.PI, UnitConvert.Convert(180, "deg", "rad"), 1e-12);
            Check.Close(360, UnitConvert.Convert(1, "turn", "deg"), 1e-9);
        }

        public void DataSize()
        {
            Check.Close(1024, UnitConvert.Convert(1, "KiB", "B"), 1e-9);
            Check.Close(1000, UnitConvert.Convert(1, "KB", "B"), 1e-9);
            Check.Close(1, UnitConvert.Convert(1024, "B", "KiB"), 1e-9);
        }

        public void Temperature()
        {
            Check.Close(212, UnitConvert.Convert(100, "C", "F"), 1e-9);
            Check.Close(32, UnitConvert.Convert(0, "C", "F"), 1e-9);
            Check.Close(0, UnitConvert.Convert(32, "F", "C"), 1e-9);
            Check.Close(273.15, UnitConvert.Convert(0, "C", "K"), 1e-9);
            Check.Close(100, UnitConvert.Convert(212, "F", "C"), 1e-9);
        }

        public void CrossCategoryThrows()
        {
            Check.Throws<ArgumentException>(() => UnitConvert.Convert(1, "kg", "m"));
            Check.Throws<ArgumentException>(() => UnitConvert.Convert(1, "C", "kg"));
            Check.Throws<ArgumentException>(() => UnitConvert.Convert(1, "furlong", "m"));
        }

        public void TryParse()
        {
            Check.True(UnitConvert.TryParse("5 km", out double v, out string u) && v == 5 && u == "km");
            Check.True(UnitConvert.TryParse("3.2kg", out double v2, out string u2) && Math.Abs(v2 - 3.2) < 1e-9 && u2 == "kg");
            Check.True(UnitConvert.TryParse("-40 C", out double v3, out string u3) && v3 == -40 && u3 == "C");
            Check.False(UnitConvert.TryParse("abc", out _, out _));
        }

        // Round-trip / chain: converting there and back, and via an intermediate unit, is consistent.
        public void Property_RoundTripAndChain()
        {
            var rng = new Random(42);
            string[] lengths = { "m", "km", "cm", "mi", "ft", "in" };
            for (int t = 0; t < 2000; t++)
            {
                double value = rng.NextDouble() * 1000;
                string a = lengths[rng.Next(lengths.Length)];
                string b = lengths[rng.Next(lengths.Length)];
                double there = UnitConvert.Convert(value, a, b);
                double back = UnitConvert.Convert(there, b, a);
                Check.Close(value, back, 1e-6, $"t{t}: {a}<->{b}");
                // chain via metres must match the direct conversion
                double viaBase = UnitConvert.Convert(UnitConvert.Convert(value, a, "m"), "m", b);
                Check.Close(there, viaBase, 1e-6, $"t{t}: chain {a}->m->{b}");
            }
        }
    }
}
