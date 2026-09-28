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

            (int amount, string unit) = Bucket(magnitude);
            string quantity = $"{amount} {unit}{(amount == 1 ? "" : "s")}";
            return future ? $"in {quantity}" : $"{quantity} ago";
        }

        /// <summary>Formats <paramref name="time"/> relative to the current time (or an injected clock).</summary>
        public static string Format(DateTimeOffset time, Func<DateTimeOffset>? clock = null)
            => Format(time, (clock ?? (static () => DateTimeOffset.UtcNow))());

        private static (int amount, string unit) Bucket(TimeSpan d)
        {
            if (d.TotalSeconds < 60) return ((int)d.TotalSeconds, "second");
            if (d.TotalMinutes < 60) return ((int)d.TotalMinutes, "minute");
            if (d.TotalHours < 24) return ((int)d.TotalHours, "hour");
            if (d.TotalDays < 30) return ((int)d.TotalDays, "day");
            if (d.TotalDays < 365) return ((int)(d.TotalDays / 30), "month");
            return ((int)(d.TotalDays / 365), "year");
        }
    }
}
