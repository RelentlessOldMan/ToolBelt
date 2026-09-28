// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ToolBelt.Time
{
    /// <summary>
    /// Parses and formats ISO 8601 duration strings (e.g. <c>PT1H30M</c>, <c>P1DT2H3M4S</c>, <c>P1W</c>)
    /// as <see cref="TimeSpan"/>. Weeks and days are accepted; years and months are NOT (they are not a
    /// fixed number of ticks). <see cref="Format"/> emits days/hours/minutes/seconds (never weeks or
    /// years), so it round-trips through <see cref="Parse"/>. Sub-second precision is preserved to the
    /// tick.
    /// </summary>
    public static class Iso8601Duration
    {
        // Order matters: weeks/days before an optional T section of hours/minutes/(fractional)seconds.
        private static readonly Regex Pattern = new Regex(
            @"^(?<neg>-)?P(?=[\dT])(?:(?<w>\d+)W)?(?:(?<d>\d+)D)?(?:T(?:(?<h>\d+)H)?(?:(?<m>\d+)M)?(?:(?<s>\d+(?:\.\d+)?)S)?)?$",
            RegexOptions.CultureInvariant);

        /// <summary>Parses an ISO 8601 duration. Throws <see cref="FormatException"/> on invalid input.</summary>
        public static TimeSpan Parse(string text)
        {
            if (text is null) throw new ArgumentNullException(nameof(text));
            if (!TryParse(text, out var value))
                throw new FormatException($"'{text}' is not a supported ISO 8601 duration.");
            return value;
        }

        /// <summary>Non-throwing parse. Years/months and malformed input return false.</summary>
        public static bool TryParse(string text, out TimeSpan value)
        {
            value = TimeSpan.Zero;
            if (string.IsNullOrEmpty(text))
                return false;

            var m = Pattern.Match(text);
            if (!m.Success)
                return false;

            try
            {
                long ticks = 0;
                bool any = false;
                if (m.Groups["w"].Success) { ticks = checked(ticks + long.Parse(m.Groups["w"].Value, CultureInfo.InvariantCulture) * 7 * TimeSpan.TicksPerDay); any = true; }
                if (m.Groups["d"].Success) { ticks = checked(ticks + long.Parse(m.Groups["d"].Value, CultureInfo.InvariantCulture) * TimeSpan.TicksPerDay); any = true; }
                if (m.Groups["h"].Success) { ticks = checked(ticks + long.Parse(m.Groups["h"].Value, CultureInfo.InvariantCulture) * TimeSpan.TicksPerHour); any = true; }
                if (m.Groups["m"].Success) { ticks = checked(ticks + long.Parse(m.Groups["m"].Value, CultureInfo.InvariantCulture) * TimeSpan.TicksPerMinute); any = true; }
                if (m.Groups["s"].Success) { ticks = checked(ticks + ParseSecondsToTicks(m.Groups["s"].Value)); any = true; }

                if (!any)
                    return false; // "P" / "PT" with no components

                value = TimeSpan.FromTicks(m.Groups["neg"].Success ? -ticks : ticks);
                return true;
            }
            catch (OverflowException)
            {
                return false;
            }
        }

        /// <summary>Formats a <see cref="TimeSpan"/> as an ISO 8601 duration. Zero renders as "PT0S".</summary>
        public static string Format(TimeSpan value)
        {
            if (value == TimeSpan.MinValue)
                throw new ArgumentOutOfRangeException(nameof(value), "TimeSpan.MinValue cannot be formatted (its magnitude overflows).");

            bool negative = value.Ticks < 0;
            long ticks = negative ? -value.Ticks : value.Ticks;

            long days = ticks / TimeSpan.TicksPerDay; ticks %= TimeSpan.TicksPerDay;
            long hours = ticks / TimeSpan.TicksPerHour; ticks %= TimeSpan.TicksPerHour;
            long minutes = ticks / TimeSpan.TicksPerMinute; ticks %= TimeSpan.TicksPerMinute;
            long seconds = ticks / TimeSpan.TicksPerSecond;
            long fraction = ticks % TimeSpan.TicksPerSecond;

            var sb = new StringBuilder();
            sb.Append(negative ? "-P" : "P");
            if (days > 0)
                sb.Append(days).Append('D');

            bool hasTime = hours > 0 || minutes > 0 || seconds > 0 || fraction > 0;
            if (hasTime)
            {
                sb.Append('T');
                if (hours > 0) sb.Append(hours).Append('H');
                if (minutes > 0) sb.Append(minutes).Append('M');
                if (seconds > 0 || fraction > 0)
                {
                    if (fraction == 0)
                        sb.Append(seconds).Append('S');
                    else
                        sb.Append(seconds).Append('.').Append(fraction.ToString("D7", CultureInfo.InvariantCulture).TrimEnd('0')).Append('S');
                }
            }

            if (sb.Length == (negative ? 2 : 1)) // only the "P"/"-P" was written
                sb.Append("T0S");

            return sb.ToString();
        }

        private static long ParseSecondsToTicks(string s)
        {
            int dot = s.IndexOf('.');
            if (dot < 0)
                return checked(long.Parse(s, CultureInfo.InvariantCulture) * TimeSpan.TicksPerSecond);

            long whole = long.Parse(s.Substring(0, dot), CultureInfo.InvariantCulture);
            string frac = (s.Substring(dot + 1) + "0000000").Substring(0, 7); // pad/truncate to 100ns ticks
            long fracTicks = long.Parse(frac, CultureInfo.InvariantCulture);
            return checked(whole * TimeSpan.TicksPerSecond + fracTicks);
        }
    }
}
