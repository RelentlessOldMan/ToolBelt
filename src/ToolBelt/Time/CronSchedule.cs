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
    /// 0-6 with 0 = Sunday (7 is also accepted as Sunday); a step from a value (<c>1/2</c>) runs to Saturday, so it reaches
    /// Sunday only when written as a range (<c>1-7/2</c>) or as 7 itself (<c>7/1</c>). When BOTH day-of-month and day-of-week are
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

        // cron's "wildcard job" (the minute or hour field starts with '*'): it runs by the clock as it reads, with no
        // make-up or de-duplication when the clock jumps. CronScheduler holds back only the other jobs after a step back.
        internal bool IsFrequent => _frequent;

        // The Gregorian calendar repeats every 400 years (146,097 days, a whole number of weeks), so a schedule that can
        // match at all matches within one cycle. The window was 8 years, which rejected valid schedules such as Feb 29 on
        // a weekend (`0 0 29 2 */6`, up to 16 years apart) or on a Sunday (28 or more). Days that cannot match are jumped
        // over whole (NextMatchingDay), so the longer window costs nothing for a schedule that matches often.
        private const int SearchDays = 146097 + 2;
        private const long SearchTicks = SearchDays * TimeSpan.TicksPerDay;
        private const string NotFound = "No occurrence found within 400 years; the expression is unsatisfiable.";

        // The calendar ending was reported as an unsatisfiable expression (`0 0 1 1 *` after 9999-01-02).
        private sealed class CalendarEnded : InvalidOperationException
        {
            public CalendarEnded() : base("No occurrence before the end of the calendar (9999-12-31).") { }
        }

        private static Exception NoneBefore(long first, long limit)
            => limit < first + SearchTicks ? new CalendarEnded() : new InvalidOperationException(NotFound);

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
        /// if there is none within 400 years (an impossible expression such as Feb 30) or before the calendar ends.
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
                if (!_months[wall.Month] || !DayMatches(wall))
                {
                    long day = NextMatchingDay(wall);   // nothing on this day can match
                    if (day < 0) break;
                    t = day;
                }
                else if (!_hours[wall.Hour])
                    t += (60 - wall.Minute) * TimeSpan.TicksPerMinute;   // nothing in this hour can match
                else if (!_minutes[wall.Minute])
                    t += TimeSpan.TicksPerMinute;
                else
                    return new DateTimeOffset(wall, after.Offset);
            }
            throw NoneBefore(first, limit);
        }

        /// <summary>
        /// The first scheduled time strictly after <paramref name="after"/> (seconds are ignored), reading the fields
        /// against the wall clock of <paramref name="timeZone"/> and returning it with the zone's offset at that instant.
        /// Daylight-saving transitions follow the rules in the class remarks. Throws <see cref="InvalidOperationException"/>
        /// if there is no occurrence within 400 years or before the calendar ends.
        /// </summary>
        public DateTimeOffset GetNextOccurrence(DateTimeOffset after, TimeZoneInfo timeZone)
        {
            if (timeZone is null) throw new ArgumentNullException(nameof(timeZone));

            // Walk UTC instants, testing each one's wall time in the zone, until the search window or the calendar ends.
            long first = FloorToMinute(after.UtcTicks) + TimeSpan.TicksPerMinute;
            if (first > DateTime.MaxValue.Ticks) throw new CalendarEnded();   // after is in the calendar's last minute
            TimeSpan offset = OffsetAt(timeZone, first);
            if (first + offset.Ticks < 0)   // a zone behind UTC: its wall clock starts later than year 1's UTC
            {
                first = FloorToMinute(-offset.Ticks + TimeSpan.TicksPerMinute - 1);
                offset = OffsetAt(timeZone, first);
            }
            long limit = Math.Min(first + SearchTicks, DateTime.MaxValue.Ticks + 1);
            long t = first;
            if (!_frequent && first - TimeSpan.TicksPerMinute + OffsetAt(timeZone, first - TimeSpan.TicksPerMinute).Ticks >= 0)
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

                // Jump to two days before the next day that can match: offsets differ by under 48 hours, so the walk still
                // reaches that day's offset changes (and a spring-forward gap on it) minute by minute.
                // Not while the clock may still fall back within two days: it can carry the wall clock back onto a day
                // before NextMatchingDay's (a fall-back at 00:01 Sunday repeats Saturday 23:01-23:59), and the jump
                // leapt over that second pass, so `* 23 * * 6` ran a week late and `*/5 * 6 11 *` a year late.
                if ((!_months[wall.Month] || !DayMatches(wall)) && !FallsBackWithin(timeZone, t, offset, limit))
                {
                    long day = NextMatchingDay(wall);
                    if (day < 0) break;
                    long jump = day - 2 * TimeSpan.TicksPerDay - offset.Ticks;
                    if (jump > t)
                    {
                        if (jump >= limit) break;
                        t = jump;
                        offset = OffsetAt(timeZone, t);
                        continue;
                    }
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
            // As the fixed-offset search decides: the calendar ended only if it cut the search window short. The walk also
            // stops early when no later day can match (31 September), which is unsatisfiable, not the calendar's end.
            throw NoneBefore(first, limit);
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

        // The midnight (wall ticks) of the first day after wall's whose month and day can match, or -1 if there is none in
        // the search window or before the calendar ends.
        private long NextMatchingDay(DateTime wall)
        {
            DateTime d = wall.Date;
            long last = DateTime.MaxValue.Date.Ticks;
            for (int i = 0; i < SearchDays && d.Ticks < last; i++)
            {
                d = d.AddDays(1);
                if (_months[d.Month] && DayMatches(d)) return d.Ticks;
            }
            return -1;
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

        // True if the offset drops below `offset` at some hour in the next two days (offsets differ by under 48 hours, so
        // no fall-back further off can bring the wall clock back to today or earlier). Sampled hourly, as the walk's hour
        // steps check offsets.
        private static bool FallsBackWithin(TimeZoneInfo zone, long t, TimeSpan offset, long limit)
        {
            long end = Math.Min(t + 2 * TimeSpan.TicksPerDay, Math.Min(limit, DateTime.MaxValue.Ticks));
            for (long s = t + TimeSpan.TicksPerHour; ; s += TimeSpan.TicksPerHour)
            {
                if (s > end) s = end;
                if (OffsetAt(zone, s) < offset) return true;
                if (s == end) return false;
            }
        }

        private static TimeSpan OffsetAt(TimeZoneInfo zone, long utcTicks)
            => zone.GetUtcOffset(new DateTime(utcTicks, DateTimeKind.Utc));

        private static long FloorToMinute(long ticks) => ticks - ticks % TimeSpan.TicksPerMinute;

        /// <summary>The next <paramref name="count"/> occurrences after <paramref name="after"/>: fewer (perhaps none) when
        /// the calendar ends first.</summary>
        public IReadOnlyList<DateTimeOffset> GetNextOccurrences(DateTimeOffset after, int count)
        {
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count), count, "Count must not be negative.");
            var result = new List<DateTimeOffset>(Math.Min(count, 1024));   // count is a limit, not a size
            var cursor = after;
            for (int i = 0; i < count; i++)
            {
                // The calendar ending threw away the occurrences already found.
                try { cursor = GetNextOccurrence(cursor); }
                catch (CalendarEnded) { break; }
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
                        // "5/15" means "from 5 to max step 15"; over day-of-week, to Saturday (6), so "1/2" is Monday,
                        // Wednesday, Friday. "7/1" starts past that and is Sunday alone; it was out of range.
                        if (slash >= 0)
                            hi = sundayNormalize ? Math.Max(lo, 6) : max;
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
