// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Globalization;
using System.Text;

namespace ToolBelt.Time
{
    /// <summary>
    /// Formats a <see cref="TimeSpan"/> as a compact human-readable string ("1h 2m 3s", "500ms") and
    /// parses that same grammar back. Format and parse are inverses for any whole-millisecond duration,
    /// so a value survives a round-trip.
    /// </summary>
    /// <remarks>
    /// Grammar: a sign ("-") followed by one or more <c>&lt;number&gt;&lt;unit&gt;</c> terms. Units are
    /// <c>d</c> (days), <c>h</c> (hours), <c>m</c> (minutes), <c>s</c> (seconds), <c>ms</c>
    /// (milliseconds). Whitespace between terms is optional on parse and a single space on format.
    /// </remarks>
    public static class HumanDuration
    {
        /// <summary>Formats <paramref name="value"/> as e.g. "1d 2h 3m 4s 5ms". Zero renders as "0ms".</summary>
        public static string Format(TimeSpan value)
        {
            if (value == TimeSpan.Zero)
                return "0ms";

            var sb = new StringBuilder();
            if (value < TimeSpan.Zero)
            {
                sb.Append('-');
                value = value.Negate();
            }

            // Negate() of TimeSpan.MinValue overflows; guard so the sign branch above is always safe.
            long days = (long)value.TotalDays;
            AppendTerm(sb, days, "d");
            AppendTerm(sb, value.Hours, "h");
            AppendTerm(sb, value.Minutes, "m");
            AppendTerm(sb, value.Seconds, "s");
            AppendTerm(sb, value.Milliseconds, "ms");

            return sb.ToString();
        }

        /// <summary>Parses the grammar produced by <see cref="Format"/>. Throws <see cref="FormatException"/> on bad input.</summary>
        public static TimeSpan Parse(string text)
        {
            if (text is null)
                throw new ArgumentNullException(nameof(text));
            if (!TryParse(text, out var result))
                throw new FormatException($"'{text}' is not a valid duration.");
            return result;
        }

        /// <summary>Non-throwing parse. Returns false (and <see cref="TimeSpan.Zero"/>) on malformed input.</summary>
        public static bool TryParse(string text, out TimeSpan value)
        {
            value = TimeSpan.Zero;
            if (string.IsNullOrWhiteSpace(text))
                return false;

            int i = 0;
            bool negative = false;
            if (text[i] == '-')
            {
                negative = true;
                i++;
            }

            long totalTicks = 0;
            bool sawTerm = false;

            while (i < text.Length)
            {
                if (char.IsWhiteSpace(text[i])) { i++; continue; }

                // number
                int start = i;
                while (i < text.Length && char.IsDigit(text[i]))
                    i++;
                if (i == start)
                    return false; // expected a digit
                if (!long.TryParse(text.Substring(start, i - start), NumberStyles.None,
                        CultureInfo.InvariantCulture, out long number))
                    return false;

                // unit
                int unitStart = i;
                while (i < text.Length && char.IsLetter(text[i]))
                    i++;
                string unit = text.Substring(unitStart, i - unitStart);

                long ticksPerUnit = unit switch
                {
                    "d" => TimeSpan.TicksPerDay,
                    "h" => TimeSpan.TicksPerHour,
                    "m" => TimeSpan.TicksPerMinute,
                    "s" => TimeSpan.TicksPerSecond,
                    "ms" => TimeSpan.TicksPerMillisecond,
                    _ => -1,
                };
                if (ticksPerUnit < 0)
                    return false;

                try
                {
                    // checked so an absurdly large term (e.g. "9999999999999d") fails the parse rather
                    // than silently wrapping into a wrong TimeSpan reported as success.
                    totalTicks = checked(totalTicks + number * ticksPerUnit);
                }
                catch (OverflowException)
                {
                    return false;
                }
                sawTerm = true;
            }

            if (!sawTerm)
                return false;

            value = TimeSpan.FromTicks(negative ? -totalTicks : totalTicks);
            return true;
        }

        private static void AppendTerm(StringBuilder sb, long amount, string unit)
        {
            if (amount == 0)
                return;
            if (sb.Length > 0 && sb[sb.Length - 1] != '-')
                sb.Append(' ');
            sb.Append(amount.ToString(CultureInfo.InvariantCulture)).Append(unit);
        }
    }
}
