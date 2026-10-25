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
    /// restricted, a day matches if EITHER matches (standard cron behavior); as in Vixie cron, a field that starts
    /// with <c>*</c> (including <c>*/2</c>) counts as unrestricted. Resolution is one minute.
    /// <para>
    /// <see cref="GetNextOccurrence(DateTimeOffset)"/> reads the fields against the wall clock of the given offset, which
    /// never changes, so it knows nothing of daylight saving. To follow a real time zone use
    /// <see cref="GetNextOccurrence(DateTimeOffset, TimeZoneInfo)"/>, which handles the transitions the way Vixie cron
    /// does: a time that a spring-forward transition skips runs at the transition instead, and a time that a fall-back
    /// transition repeats runs once (in its first pass) — unless the minute or hour field starts with <c>*</c>, in which
    /// case the job simply runs at every matching instant, in both passes, and nothing skipped is made up.
    /// </para>
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
        private readonly bool _frequent;       // minute or hour field starts with '*': no DST make-up or de-duplication

        // A valid schedule recurs within 8 years (Feb 29 across a non-leap century year such as 2100).
        private const long SearchTicks = 8 * 366 * TimeSpan.TicksPerDay;
        private const string NotFound = "No occurrence found within 8 years (or before the end of the calendar); the expression may be unsatisfiable.";

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
            _domRestricted = !fields[2].StartsWith("*", StringComparison.Ordinal);
            _dowRestricted = !fields[4].StartsWith("*", StringComparison.Ordinal);
            _frequent = fields[0].StartsWith("*", StringComparison.Ordinal) || fields[1].StartsWith("*", StringComparison.Ordinal);
        }

        /// <summary>
        /// The first scheduled time strictly after <paramref name="after"/> (seconds are ignored), reading the fields
        /// against the wall clock of <paramref name="after"/>'s fixed offset. Throws <see cref="InvalidOperationException"/>
        /// if there is none within 8 years (an impossible expression such as Feb 30) or before the calendar ends.
        /// </summary>
        public DateTimeOffset GetNextOccurrence(DateTimeOffset after)
        {
            // Wall-clock ticks, bounded so that converting back with the offset stays inside DateTimeOffset's range.
            long offset = after.Offset.Ticks;
            long first = FloorToMinute(after.DateTime.Ticks) + TimeSpan.TicksPerMinute;
            long limit = Math.Min(first + SearchTicks, Math.Min(DateTime.MaxValue.Ticks, DateTime.MaxValue.Ticks + offset) + 1);
            for (long t = first; t < limit;)
            {
                var wall = new DateTime(t);
                if (!_months[wall.Month] || !DayMatches(wall) || !_hours[wall.Hour])
                    t += (60 - wall.Minute) * TimeSpan.TicksPerMinute;   // nothing in this hour can match
                else if (!_minutes[wall.Minute])
                    t += TimeSpan.TicksPerMinute;
                else
                    return new DateTimeOffset(wall, after.Offset);
            }
            throw new InvalidOperationException(NotFound);
        }

        /// <summary>
        /// The first scheduled time strictly after <paramref name="after"/> (seconds are ignored), reading the fields
        /// against the wall clock of <paramref name="timeZone"/> and returning it with the zone's offset at that instant.
        /// Daylight-saving transitions follow the rules in the class remarks. Throws <see cref="InvalidOperationException"/>
        /// if there is no occurrence within 8 years or before the calendar ends.
        /// </summary>
        public DateTimeOffset GetNextOccurrence(DateTimeOffset after, TimeZoneInfo timeZone)
        {
            if (timeZone is null) throw new ArgumentNullException(nameof(timeZone));

            // Walk UTC instants, testing each one's wall time in the zone, until the search window or the calendar ends.
            long first = Math.Max(FloorToMinute(after.UtcTicks) + TimeSpan.TicksPerMinute, TimeSpan.TicksPerDay);
            long limit = Math.Min(first + SearchTicks, DateTime.MaxValue.Ticks + 1);
            long t = first;
            TimeSpan offset = OffsetAt(timeZone, t);
            if (!_frequent)
            {
                // The walk may start exactly at a spring-forward transition (after was in the minute before it): a
                // fixed time in the gap still runs now.
                TimeSpan before = OffsetAt(timeZone, first - TimeSpan.TicksPerMinute);
                if (offset > before && GapMatches(first - TimeSpan.TicksPerMinute + before.Ticks, first + offset.Ticks, out long end))
                    return new DateTimeOffset(new DateTime(end), offset);
            }
            while (t < limit && t + offset.Ticks <= DateTime.MaxValue.Ticks)
            {
                var wall = new DateTime(t + offset.Ticks);
                if (Matches(wall))
                {
                    var hit = new DateTimeOffset(wall, offset);
                    if (_frequent || !IsRepeat(timeZone, hit)) return hit;
                }

                // Skip the rest of a wall-clock hour that cannot match, but cross an offset change one minute at a time.
                long step = TimeSpan.TicksPerMinute;
                if (!_months[wall.Month] || !DayMatches(wall) || !_hours[wall.Hour])
                {
                    long hourStep = (60 - wall.Minute) * TimeSpan.TicksPerMinute;
                    if (t + hourStep < limit && OffsetAt(timeZone, t + hourStep) == offset) step = hourStep;
                }

                long next = t + step;
                if (next >= limit) break;
                TimeSpan nextOffset = OffsetAt(timeZone, next);
                if (nextOffset > offset && !_frequent)
                {
                    // Spring forward: the wall clock jumped over (wall, next wall). A fixed time in the gap runs now.
                    if (GapMatches(wall.Ticks, next + nextOffset.Ticks, out long gapEnd))
                        return new DateTimeOffset(new DateTime(gapEnd), nextOffset);
                }
                t = next;
                offset = nextOffset;
            }
            throw new InvalidOperationException(NotFound);
        }

        // True if a wall time strictly between lastWall and gapEnd (the wall times a spring-forward skipped) matches;
        // gapEnd comes back clamped to the calendar.
        private bool GapMatches(long lastWall, long gapEnd, out long end)
        {
            end = Math.Min(gapEnd, DateTime.MaxValue.Ticks);
            for (long g = lastWall + TimeSpan.TicksPerMinute; g < end; g += TimeSpan.TicksPerMinute)
                if (Matches(new DateTime(g))) return true;
            return false;
        }

        private bool Matches(DateTime wall)
            => _months[wall.Month] && DayMatches(wall) && _hours[wall.Hour] && _minutes[wall.Minute];

        // True for the second pass through a wall time that a fall-back transition repeats.
        private static bool IsRepeat(TimeZoneInfo zone, DateTimeOffset instant)
        {
            if (!zone.IsAmbiguousTime(instant)) return false;
            TimeSpan smallest = TimeSpan.MaxValue;
            foreach (TimeSpan o in zone.GetAmbiguousTimeOffsets(instant)) if (o < smallest) smallest = o;
            return instant.Offset == smallest;   // the clock went back, so the later pass has the smaller offset
        }

        private static TimeSpan OffsetAt(TimeZoneInfo zone, long utcTicks)
            => zone.GetUtcOffset(new DateTime(utcTicks, DateTimeKind.Utc));

        private static long FloorToMinute(long ticks) => ticks - ticks % TimeSpan.TicksPerMinute;

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

        private bool DayMatches(DateTime candidate)
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
                    hi = sundayNormalize ? 6 : max;   // a step over day-of-week must not reach 7 (Sunday again)
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
                            hi = sundayNormalize ? 6 : max;
                    }
                }

                if (lo < min || hi > max || lo > hi)
                    throw new FormatException($"Value out of range [{min},{max}] in cron field '{field}'.");

                for (long v = lo; v <= hi; v += step)   // long: a huge step must not wrap around
                    set[sundayNormalize && v == 7 ? 0 : (int)v] = true;
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
