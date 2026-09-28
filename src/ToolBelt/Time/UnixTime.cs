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

        /// <summary>Whole seconds since the epoch (the value is normalized to UTC first).</summary>
        public static long ToUnixSeconds(DateTime value) => new DateTimeOffset(value.ToUniversalTime()).ToUnixTimeSeconds();

        /// <summary>Whole milliseconds since the epoch (the value is normalized to UTC first).</summary>
        public static long ToUnixMilliseconds(DateTime value) => new DateTimeOffset(value.ToUniversalTime()).ToUnixTimeMilliseconds();

        /// <summary>Converts seconds-since-epoch to a UTC <see cref="DateTimeOffset"/>.</summary>
        public static DateTimeOffset FromUnixSeconds(long seconds) => Epoch.AddSeconds(seconds);

        /// <summary>Converts milliseconds-since-epoch to a UTC <see cref="DateTimeOffset"/>.</summary>
        public static DateTimeOffset FromUnixMilliseconds(long milliseconds) => Epoch.AddMilliseconds(milliseconds);
    }
}
