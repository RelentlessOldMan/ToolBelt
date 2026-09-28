using System;
using ToolBelt.Text;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Text
{
    public sealed class RomanNumeralsTests
    {
        public void ToRoman_KnownValues()
        {
            Check.Equal("I", RomanNumerals.ToRoman(1));
            Check.Equal("IV", RomanNumerals.ToRoman(4));
            Check.Equal("IX", RomanNumerals.ToRoman(9));
            Check.Equal("XL", RomanNumerals.ToRoman(40));
            Check.Equal("XCIX", RomanNumerals.ToRoman(99));
            Check.Equal("MCMLXXXIV", RomanNumerals.ToRoman(1984));
            Check.Equal("MMMCMXCIX", RomanNumerals.ToRoman(3999));
        }

        public void FromRoman_KnownValues()
        {
            Check.Equal(4, RomanNumerals.FromRoman("IV"));
            Check.Equal(1984, RomanNumerals.FromRoman("MCMLXXXIV"));
            Check.Equal(3999, RomanNumerals.FromRoman("MMMCMXCIX"));
            Check.Equal(14, RomanNumerals.FromRoman("xiv")); // case-insensitive
        }

        public void OutOfRange_Throws()
        {
            Check.Throws<ArgumentOutOfRangeException>(() => RomanNumerals.ToRoman(0));
            Check.Throws<ArgumentOutOfRangeException>(() => RomanNumerals.ToRoman(4000));
        }

        public void Malformed_Throws()
        {
            Check.Throws<FormatException>(() => RomanNumerals.FromRoman("IIII")); // non-canonical
            Check.Throws<FormatException>(() => RomanNumerals.FromRoman("IC"));   // invalid subtractive
            Check.Throws<FormatException>(() => RomanNumerals.FromRoman("ABC"));  // bad symbols
            Check.Throws<FormatException>(() => RomanNumerals.FromRoman(""));
            Check.Throws<ArgumentNullException>(() => RomanNumerals.FromRoman(null!));
        }

        public void TryFromRoman()
        {
            Check.True(RomanNumerals.TryFromRoman("XLII", out int v) && v == 42);
            Check.False(RomanNumerals.TryFromRoman("IIII", out _));
            Check.False(RomanNumerals.TryFromRoman(null!, out _));
        }

        // Property: every value in range round-trips through ToRoman/FromRoman.
        public void Property_RoundTrip()
        {
            for (int n = 1; n <= 3999; n++)
                Check.Equal(n, RomanNumerals.FromRoman(RomanNumerals.ToRoman(n)), $"round-trip {n}");
        }
    }
}
