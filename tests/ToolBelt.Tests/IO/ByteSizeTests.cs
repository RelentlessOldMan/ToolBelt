using System;
using ToolBelt.IO;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.IO
{
    public sealed class ByteSizeTests
    {
        public void Format_Binary()
        {
            Check.Equal("0 B", ByteSize.Format(0));
            Check.Equal("512 B", ByteSize.Format(512));
            Check.Equal("1.50 KiB", ByteSize.Format(1536));
            Check.Equal("1.00 MiB", ByteSize.Format(1024 * 1024));
        }

        public void Format_Decimal()
        {
            Check.Equal("1.50 KB", ByteSize.Format(1500, binary: false));
            Check.Equal("1.00 MB", ByteSize.Format(1_000_000, binary: false));
        }

        public void Format_Negative()
        {
            Check.Equal("-1.50 KiB", ByteSize.Format(-1536));
        }

        public void Format_DecimalsOption()
        {
            Check.Equal("1.5 KiB", ByteSize.Format(1536, binary: true, decimals: 1));
            Check.Equal("2 KiB", ByteSize.Format(2048, binary: true, decimals: 0));
        }

        public void Parse_Known()
        {
            Check.Equal(1024L, ByteSize.Parse("1 KiB"));
            Check.Equal(1000L, ByteSize.Parse("1 KB"));
            Check.Equal(1000L, ByteSize.Parse("1K"));       // bare K = decimal
            Check.Equal(1536L, ByteSize.Parse("1.5 KiB"));
            Check.Equal(1500L, ByteSize.Parse("1.5kb"));    // case-insensitive
            Check.Equal(10485760L, ByteSize.Parse("10MiB"));
            Check.Equal(42L, ByteSize.Parse("42"));         // bare number = bytes
        }

        public void Parse_Invalid_Throws()
        {
            Check.Throws<FormatException>(() => ByteSize.Parse("abc"));
            Check.Throws<FormatException>(() => ByteSize.Parse("5 XB"));
            Check.Throws<FormatException>(() => ByteSize.Parse(""));
        }

        public void Null_Throws()
        {
            Check.Throws<ArgumentNullException>(() => ByteSize.Parse(null!));
        }

        public void Overflow_ReturnsFalse_InsteadOfGarbage()
        {
            // number * multiplier far exceeds long range; must fail rather than yield a wrapped value.
            Check.False(ByteSize.TryParse("1000000 EB", out _));
            Check.Throws<FormatException>(() => ByteSize.Parse("1e30"));
        }

        // Whole-unit values round-trip exactly (no rounding loss).
        public void RoundTrip_WholeUnits()
        {
            var rng = new Random(2048);
            long[] units = { 1L, 1024L, 1024L * 1024, 1024L * 1024 * 1024 };
            for (int trial = 0; trial < 500; trial++)
            {
                long unit = units[rng.Next(units.Length)];
                long count = rng.Next(1, 512);
                long bytes = count * unit;

                string formatted = ByteSize.Format(bytes, binary: true, decimals: 2);
                long parsed = ByteSize.Parse(formatted);

                // Allow tiny rounding at 2 decimals: within 0.5% or a byte.
                long tolerance = Math.Max(1, bytes / 200);
                Check.True(Math.Abs(parsed - bytes) <= tolerance,
                    $"trial {trial}: {bytes} -> '{formatted}' -> {parsed}");
            }
        }
    }
}
