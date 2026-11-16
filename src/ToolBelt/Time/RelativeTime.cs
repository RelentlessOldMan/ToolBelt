// ToolBelt drop-in — fully self-contained (BCL only).
using System;

namespace ToolBelt.Time
{
    /// <summary>
    /// Renders a timestamp relative to "now" as a coarse human phrase — "just now", "3 minutes ago",
    /// "in 2 hours". The reference time is supplied explicitly (or via an injectable clock) so results
    /// are deterministic and testable.
    /// </summary>
    public static class RelativeTime
    {
        /// <summary>Formats <paramref name="time"/> relative to <paramref name="now"/>.</summary>
        public static string Format(DateTimeOffset time, DateTimeOffset now)
        {
            TimeSpan delta = now - time;
            bool future = delta < TimeSpan.Zero;
            TimeSpan magnitude = delta.Duration();

            double seconds = magnitude.TotalSeconds;
            if (seconds < 5)
                return "just now";

            (int amount, string unit) = Bucket(magnitude, future ? now.UtcDateTime : time.UtcDateTime, future ? time.UtcDateTime : now.UtcDateTime);
            string quantity = $"{amount} {unit}{(amount == 1 ? "" : "s")}";
            return future ? $"in {quantity}" : $"{quantity} ago";
        }

        /// <summary>Formats <paramref name="time"/> relative to the current time (or an injected clock).</summary>
        public static string Format(DateTimeOffset time, Func<DateTimeOffset>? clock = null)
            => Format(time, (clock ?? (static () => DateTimeOffset.UtcNow))());

        private static (int amount, string unit) Bucket(TimeSpan d, DateTime earlier, DateTime later)
        {
            if (d.TotalSeconds < 60) return ((int)d.TotalSeconds, "second");
            if (d.TotalMinutes < 60) return ((int)d.TotalMinutes, "minute");
            if (d.TotalHours < 24) return ((int)d.TotalHours, "hour");
            if (d.TotalDays < 30) return ((int)d.TotalDays, "day");
            if (d.TotalDays < 365) return (Math.Min(11, (int)(d.TotalDays / 30)), "month");   // 360-364 days is not "12 months"
            // Whole calendar years: days / 365 gained a year every four (2000 years read "2001", and the calendar's whole
            // span "10005").
            int years = later.Year - earlier.Year;
            if (earlier.AddYears(years) > later) years--;   // the anniversary of Feb 29 is Feb 28, as AddYears has it
            return (Math.Max(1, years), "year");   // 365 days inside a leap year is still "1 year", not "12 months"
        }
    }
}
