using System;
using ToolBelt.Time;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Time
{
    public sealed class Iso8601DurationTests
    {
        public void Parse_Known()
        {
            Check.Equal(new TimeSpan(0, 1, 30, 0), Iso8601Duration.Parse("PT1H30M"));
            Check.Equal(new TimeSpan(1, 2, 3, 4), Iso8601Duration.Parse("P1DT2H3M4S"));
            Check.Equal(TimeSpan.FromDays(7), Iso8601Duration.Parse("P1W"));
            Check.Equal(TimeSpan.Zero, Iso8601Duration.Parse("PT0S"));
            Check.Equal(TimeSpan.FromSeconds(-90), Iso8601Duration.Parse("-PT1M30S"));
        }

        public void Parse_FractionalSeconds()
        {
            Check.Equal(TimeSpan.FromMilliseconds(1500), Iso8601Duration.Parse("PT1.5S"));
            Check.Equal(new TimeSpan(1234567), Iso8601Duration.Parse("PT0.1234567S")); // tick precision
        }

        public void Format_Known()
        {
            Check.Equal("PT1H30M", Iso8601Duration.Format(new TimeSpan(0, 1, 30, 0)));
            Check.Equal("P1DT2H3M4S", Iso8601Duration.Format(new TimeSpan(1, 2, 3, 4)));
            Check.Equal("PT0S", Iso8601Duration.Format(TimeSpan.Zero));
            Check.Equal("-PT1M30S", Iso8601Duration.Format(TimeSpan.FromSeconds(-90)));
            Check.Equal("PT1.5S", Iso8601Duration.Format(TimeSpan.FromMilliseconds(1500)));
        }

        public void YearsAndMonths_Rejected()
        {
            Check.False(Iso8601Duration.TryParse("P1Y", out _));
            Check.False(Iso8601Duration.TryParse("P1M", out _)); // month, not minute (minute needs T)
            Check.Throws<FormatException>(() => Iso8601Duration.Parse("P1Y2M"));
        }

        public void Invalid_Throws()
        {
            Check.Throws<FormatException>(() => Iso8601Duration.Parse("1H"));  // no leading P
            Check.Throws<FormatException>(() => Iso8601Duration.Parse("P"));   // no components
            Check.Throws<FormatException>(() => Iso8601Duration.Parse("PT"));
            Check.Throws<ArgumentNullException>(() => Iso8601Duration.Parse(null!));
        }

        // Property: Format then Parse is the identity for any (whole-tick) TimeSpan in a safe range.
        public void RoundTrip_FormatThenParse()
        {
            var rng = new Random(8601);
            for (int trial = 0; trial < 3000; trial++)
            {
                // +/- ~1000 days, arbitrary tick offset (exercises the fractional-seconds path).
                long ticks = ((long)rng.Next(-1000, 1000)) * TimeSpan.TicksPerDay + rng.Next(-10_000_000, 10_000_000);
                var original = TimeSpan.FromTicks(ticks);

                string formatted = Iso8601Duration.Format(original);
                Check.Equal(original, Iso8601Duration.Parse(formatted), $"trial {trial}: '{formatted}'");
            }
        }
    }
}
