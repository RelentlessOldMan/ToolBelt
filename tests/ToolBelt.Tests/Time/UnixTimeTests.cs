using System;
using ToolBelt.Time;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Time
{
    public sealed class UnixTimeTests
    {
        public void KnownEpochValues()
        {
            Check.Equal(0L, UnixTime.ToUnixSeconds(new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero)));
            // 2001-09-09 01:46:40 UTC is the famous 1_000_000_000.
            Check.Equal(1_000_000_000L,
                UnixTime.ToUnixSeconds(new DateTimeOffset(2001, 9, 9, 1, 46, 40, TimeSpan.Zero)));
        }

        public void FromUnixSeconds()
        {
            Check.Equal(new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero), UnixTime.FromUnixSeconds(0));
            Check.Equal(new DateTimeOffset(2001, 9, 9, 1, 46, 40, TimeSpan.Zero), UnixTime.FromUnixSeconds(1_000_000_000));
        }

        public void Milliseconds()
        {
            var dto = new DateTimeOffset(1970, 1, 1, 0, 0, 1, 500, TimeSpan.Zero);
            Check.Equal(1500L, UnixTime.ToUnixMilliseconds(dto));
            Check.Equal(dto, UnixTime.FromUnixMilliseconds(1500));
        }

        public void DateTime_NormalizesToUtc()
        {
            var utc = new DateTime(2001, 9, 9, 1, 46, 40, DateTimeKind.Utc);
            Check.Equal(1_000_000_000L, UnixTime.ToUnixSeconds(utc));
        }

        // Round-trip: seconds and milliseconds survive there-and-back.
        public void RoundTrip()
        {
            var rng = new Random(1970);
            for (int i = 0; i < 2000; i++)
            {
                long seconds = (long)(rng.NextDouble() * 4_000_000_000L) - 1_000_000_000L;
                Check.Equal(seconds, UnixTime.ToUnixSeconds(UnixTime.FromUnixSeconds(seconds)), $"seconds {seconds}");

                long millis = (long)(rng.NextDouble() * 4_000_000_000_000L) - 1_000_000_000_000L;
                Check.Equal(millis, UnixTime.ToUnixMilliseconds(UnixTime.FromUnixMilliseconds(millis)), $"millis {millis}");
            }
        }
    }
}
