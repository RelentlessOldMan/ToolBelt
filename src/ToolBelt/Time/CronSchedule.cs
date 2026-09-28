// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;
using System.Globalization;

namespace ToolBelt.Time
{
    /// <summary>
    /// A standard 5-field cron schedule (<c>minute hour day-of-month month day-of-week</c>) with
    /// next-occurrence calculation. Each field supports <c>*</c>, single values, comma lists
    /// (<c>1,15,30</c>), ranges (<c>9-17</c>), and steps (<c>*/15</c>, <c>0-30/5</c>). Day-of-week is
    /// 0-6 with 0 = Sunday (7 is also accepted as Sunday). When BOTH day-of-month and day-of-week are
    /// restricted, a day matches if EITHER matches (standard cron behavior). Resolution is one minute.
    /// </summary>
    public sealed class CronSchedule
    {
        private readonly bool[] _minutes;      // 0-59
        private readonly bool[] _hours;        // 0-23
        private readonly bool[] _daysOfMonth;  // 1-31
        private readonly bool[] _months;       // 1-12
        private readonly bool[] _daysOfWeek;   // 0-6 (Sunday=0)
        private readonly bool _domRestricted;
        private readonly bool _dowRestricted;

        public CronSchedule(string expression)
        {
            if (expression is null) throw new ArgumentNullException(nameof(expression));

            var fields = expression.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length != 5)
                throw new FormatException("A cron expression must have exactly 5 fields: minute hour day-of-month month day-of-week.");

            _minutes = ParseField(fields[0], 0, 59, nameof(fields));
            _hours = ParseField(fields[1], 0, 23, nameof(fields));
            _daysOfMonth = ParseField(fields[2], 1, 31, nameof(fields));
            _months = ParseField(fields[3], 1, 12, nameof(fields));
            _daysOfWeek = ParseField(fields[4], 0, 7, nameof(fields), sundayNormalize: true);
            _domRestricted = fields[2] != "*";
            _dowRestricted = fields[4] != "*";
        }

        /// <summary>The first scheduled time strictly after <paramref name="after"/> (seconds are ignored).</summary>
        public DateTimeOffset GetNextOccurrence(DateTimeOffset after)
        {
            // Advance to the start of the next whole minute.
            var candidate = new DateTimeOffset(
                after.Year, after.Month, after.Day, after.Hour, after.Minute, 0, after.Offset)
                .AddMinutes(1);

            // A valid schedule always recurs within 4 years; bound the search so an impossible
            // expression (e.g. "0 0 30 2 *" — Feb 30) fails instead of looping forever.
            DateTimeOffset limit = candidate.AddYears(4);
            while (candidate < limit)
            {
                if (_months[candidate.Month]
                    && DayMatches(candidate)
                    && _hours[candidate.Hour]
                    && _minutes[candidate.Minute])
                {
                    return candidate;
                }
                candidate = candidate.AddMinutes(1);
            }

            throw new InvalidOperationException("No occurrence found within 4 years; the expression may be unsatisfiable.");
        }

        /// <summary>The next <paramref name="count"/> occurrences after <paramref name="after"/>.</summary>
        public IReadOnlyList<DateTimeOffset> GetNextOccurrences(DateTimeOffset after, int count)
        {
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count), count, "Count must not be negative.");
            var result = new List<DateTimeOffset>(count);
            var cursor = after;
            for (int i = 0; i < count; i++)
            {
                cursor = GetNextOccurrence(cursor);
                result.Add(cursor);
            }
            return result;
        }

        private bool DayMatches(DateTimeOffset candidate)
        {
            bool domOk = _daysOfMonth[candidate.Day];
            bool dowOk = _daysOfWeek[(int)candidate.DayOfWeek]; // .NET DayOfWeek: Sunday=0..Saturday=6

            // Both restricted -> OR; otherwise the '*' field's set is all-true so AND reduces to the other.
            return _domRestricted && _dowRestricted ? domOk || dowOk : domOk && dowOk;
        }

        private static bool[] ParseField(string field, int min, int max, string paramName, bool sundayNormalize = false)
        {
            // For day-of-week we accept 0-7 as input (7 == Sunday) but normalize into a 0-6 set.
            int setSize = sundayNormalize ? 7 : max + 1;
            var set = new bool[setSize];

            foreach (string part in field.Split(','))
            {
                if (part.Length == 0)
                    throw new FormatException($"Empty term in cron field '{field}'.");

                string range = part;
                int step = 1;
                int slash = part.IndexOf('/');
                if (slash >= 0)
                {
                    range = part.Substring(0, slash);
                    if (!int.TryParse(part.Substring(slash + 1), NumberStyles.None, CultureInfo.InvariantCulture, out step) || step <= 0)
                        throw new FormatException($"Invalid step in cron field '{field}'.");
                }

                int lo, hi;
                if (range == "*")
                {
                    lo = min;
                    hi = max;
                }
                else
                {
                    int dash = range.IndexOf('-');
                    if (dash > 0)
                    {
                        lo = ParseInt(range.Substring(0, dash), field);
                        hi = ParseInt(range.Substring(dash + 1), field);
                    }
                    else
                    {
                        lo = hi = ParseInt(range, field);
                        if (slash >= 0) // "5/15" means "from 5 to max step 15"
                            hi = max;
                    }
                }

                if (lo < min || hi > max || lo > hi)
                    throw new FormatException($"Value out of range [{min},{max}] in cron field '{field}'.");

                for (int v = lo; v <= hi; v += step)
                    set[sundayNormalize && v == 7 ? 0 : v] = true;
            }

            return set;
        }

        private static int ParseInt(string s, string field)
        {
            if (!int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out int value))
                throw new FormatException($"'{s}' is not a valid number in cron field '{field}'.");
            return value;
        }
    }
}
