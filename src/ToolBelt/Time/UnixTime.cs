// ToolBelt drop-in — fully self-contained (BCL only).
using System;

namespace ToolBelt.Time
{
    /// <summary>
    /// Converts between the Unix epoch (1970-01-01 UTC) and .NET date/time types, in both seconds and
    /// milliseconds. <see cref="DateTime"/> overloads normalize to UTC first (a value with
    /// <see cref="DateTimeKind.Unspecified"/> is treated as local, matching <c>ToUniversalTime</c>).
    /// </summary>
    public static class UnixTime
    {
        /// <summary>The Unix epoch, 1970-01-01T00:00:00Z.</summary>
        public static readonly DateTimeOffset Epoch = new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero);

        /// <summary>Whole seconds since the epoch.</summary>
        public static long ToUnixSeconds(DateTimeOffset value) => value.ToUnixTimeSeconds();

        /// <summary>Whole milliseconds since the epoch.</summary>
        public static long ToUnixMilliseconds(DateTimeOffset value) => value.ToUnixTimeMilliseconds();

        /// <summary>Whole seconds since the epoch (a Local or Unspecified value is read as local time).</summary>
        public static long ToUnixSeconds(DateTime value) => FloorDiv(UtcTicks(value), TimeSpan.TicksPerSecond) - 62135596800L;

        /// <summary>Whole milliseconds since the epoch (a Local or Unspecified value is read as local time).</summary>
        public static long ToUnixMilliseconds(DateTime value) => FloorDiv(UtcTicks(value), TimeSpan.TicksPerMillisecond) - 62135596800000L;

        // ToUniversalTime clamps to the calendar, so a local time within a day of either end would map to the same
        // instant as its neighbours; there the offset is applied to the ticks directly (the UTC instant may lie just
        // outside the calendar, which seconds since the epoch can still express).
        private static long UtcTicks(DateTime value)
        {
            if (value.Kind == DateTimeKind.Utc) return value.Ticks;
            const long Margin = 2 * TimeSpan.TicksPerDay;
            if (value.Ticks > Margin && value.Ticks < DateTime.MaxValue.Ticks - Margin) return value.ToUniversalTime().Ticks;
            return value.Ticks - TimeZoneInfo.Local.GetUtcOffset(value).Ticks;
        }

        // Rounds down: the UTC ticks of a local time just after year 1 can be negative (b is positive).
        private static long FloorDiv(long a, long b) => a >= 0 ? a / b : (a + 1) / b - 1;

        /// <summary>Converts seconds-since-epoch to a UTC <see cref="DateTimeOffset"/>.</summary>
        public static DateTimeOffset FromUnixSeconds(long seconds)
        {
            try { return Epoch.AddSeconds(seconds); }
            catch (ArgumentOutOfRangeException)
            {
                throw new ArgumentOutOfRangeException(nameof(seconds), seconds, "The result would fall outside the calendar.");
            }
        }

        /// <summary>Converts milliseconds-since-epoch to a UTC <see cref="DateTimeOffset"/>.</summary>
        public static DateTimeOffset FromUnixMilliseconds(long milliseconds)
        {
            try { return Epoch.AddMilliseconds(milliseconds); }
            catch (ArgumentOutOfRangeException)
            {
                throw new ArgumentOutOfRangeException(nameof(milliseconds), milliseconds, "The result would fall outside the calendar.");
            }
        }
    }
}
