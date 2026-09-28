using System;
using ToolBelt.Time;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Time
{
    public sealed class HumanDurationTests
    {
        public void Format_Basic()
        {
            Check.Equal("1h 2m 3s", HumanDuration.Format(new TimeSpan(0, 1, 2, 3)));
            Check.Equal("2d 5h", HumanDuration.Format(new TimeSpan(2, 5, 0, 0)));
            Check.Equal("500ms", HumanDuration.Format(TimeSpan.FromMilliseconds(500)));
            Check.Equal("0ms", HumanDuration.Format(TimeSpan.Zero));
        }

        public void Format_Negative()
        {
            Check.Equal("-1m 30s", HumanDuration.Format(TimeSpan.FromSeconds(-90)));
        }

        public void Parse_Basic()
        {
            Check.Equal(new TimeSpan(0, 1, 2, 3), HumanDuration.Parse("1h 2m 3s"));
            Check.Equal(TimeSpan.FromMilliseconds(500), HumanDuration.Parse("500ms"));
        }

        public void Parse_ToleratesMissingWhitespaceAndNegative()
        {
            Check.Equal(new TimeSpan(0, 1, 2, 3), HumanDuration.Parse("1h2m3s"));
            Check.Equal(TimeSpan.FromSeconds(-90), HumanDuration.Parse("-1m30s"));
        }

        public void Parse_Invalid_Throws()
        {
            Check.Throws<FormatException>(() => HumanDuration.Parse("abc"));
            Check.Throws<FormatException>(() => HumanDuration.Parse("10x"));
            Check.Throws<FormatException>(() => HumanDuration.Parse(""));
            Check.Throws<FormatException>(() => HumanDuration.Parse("h"));
        }

        public void Parse_TickOverflow_Fails_InsteadOfWrapping()
        {
            // A term far larger than TimeSpan's tick range must fail rather than wrap to a wrong value.
            Check.False(HumanDuration.TryParse("9999999999999d", out _));
            Check.Throws<FormatException>(() => HumanDuration.Parse("9999999999999d"));
        }

        // Property: Format then Parse is the identity for any whole-millisecond duration.
        public void RoundTrip_FormatThenParse()
        {
            var rng = new Random(20260925);
            for (int trial = 0; trial < 2000; trial++)
            {
                // Random millisecond count within +/- ~50 days (plus sub-day jitter), both signs.
                long ms = (long)rng.Next(-50, 51) * 86_400_000L
                          + rng.Next(-86_400_000, 86_400_000);
                var original = TimeSpan.FromMilliseconds(ms);

                string formatted = HumanDuration.Format(original);
                var parsed = HumanDuration.Parse(formatted);

                Check.Equal(original, parsed, $"trial {trial}: '{formatted}'");
            }
        }
    }
}
