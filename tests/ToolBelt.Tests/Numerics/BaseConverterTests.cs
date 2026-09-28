using System;
using ToolBelt.Numerics;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Numerics
{
    public sealed class BaseConverterTests
    {
        public void ToBase_KnownValues()
        {
            Check.Equal("0", BaseConverter.ToBase(0, 2));
            Check.Equal("1010", BaseConverter.ToBase(10, 2));
            Check.Equal("ff", BaseConverter.ToBase(255, 16));
            Check.Equal("z", BaseConverter.ToBase(35, 36));
            Check.Equal("10", BaseConverter.ToBase(36, 36));
        }

        public void FromBase_KnownValues()
        {
            Check.Equal(10L, BaseConverter.FromBase("1010", 2));
            Check.Equal(255L, BaseConverter.FromBase("FF", 16)); // case-insensitive
            Check.Equal(255L, BaseConverter.FromBase("ff", 16));
            Check.Equal(36L, BaseConverter.FromBase("10", 36));
        }

        public void CustomAlphabet()
        {
            const string alphabet = "ABCD"; // base 4
            Check.Equal("BC", BaseConverter.ToBase(6, alphabet)); // 1*4 + 2
            Check.Equal(6L, BaseConverter.FromBase("BC", alphabet));
        }

        public void InvalidArguments_Throw()
        {
            Check.Throws<ArgumentOutOfRangeException>(() => BaseConverter.ToBase(10, 1));
            Check.Throws<ArgumentOutOfRangeException>(() => BaseConverter.ToBase(10, 37));
            Check.Throws<ArgumentOutOfRangeException>(() => BaseConverter.ToBase(-1, 10));
            Check.Throws<FormatException>(() => BaseConverter.FromBase("", 10));
            Check.Throws<FormatException>(() => BaseConverter.FromBase("12x", 10)); // bad digit
            Check.Throws<ArgumentNullException>(() => BaseConverter.FromBase(null!, 10));
        }

        public void Overflow_Throws()
        {
            // A string that exceeds long.MaxValue in the given base must throw, not silently wrap.
            Check.Throws<OverflowException>(() => BaseConverter.FromBase("99999999999999999999", 10));
        }

        // Property: value -> ToBase -> FromBase round-trips for every base 2..36.
        public void Property_RoundTrip()
        {
            var rng = new Random(29);
            for (int t = 0; t < 5000; t++)
            {
                long value = (long)(rng.NextDouble() * long.MaxValue);
                int radix = rng.Next(2, 37);
                string encoded = BaseConverter.ToBase(value, radix);
                Check.Equal(value, BaseConverter.FromBase(encoded, radix), $"t{t}: base {radix}");
            }
        }
    }
}
