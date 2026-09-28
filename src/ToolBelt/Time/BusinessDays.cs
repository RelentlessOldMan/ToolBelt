// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;

namespace ToolBelt.Time
{
    /// <summary>
    /// Business-day arithmetic over the Gregorian calendar: Saturdays and Sundays are non-business days,
    /// plus any dates supplied as holidays (matched by their date component). Operates on the
    /// <see cref="DateTime.Date"/> part; the time of day is ignored.
    /// </summary>
    public static class BusinessDays
    {
        /// <summary>Whether <paramref name="date"/> is a weekday and not a holiday.</summary>
        public static bool IsBusinessDay(DateTime date, IEnumerable<DateTime>? holidays = null)
            => IsBusinessDay(date.Date, BuildHolidaySet(holidays));

        /// <summary>
        /// Adds <paramref name="count"/> business days (negative counts move backward). A count of 0
        /// returns the input date unchanged; non-zero counts land on a business day.
        /// </summary>
        public static DateTime AddBusinessDays(DateTime start, int count, IEnumerable<DateTime>? holidays = null)
        {
            var set = BuildHolidaySet(holidays);
            DateTime date = start.Date;
            if (count == 0)
                return date;

            int step = count > 0 ? 1 : -1;
            int remaining = Math.Abs(count);
            while (remaining > 0)
            {
                date = date.AddDays(step);
                if (IsBusinessDay(date, set))
                    remaining--;
            }
            return date;
        }

        /// <summary>Counts business days in the inclusive range [<paramref name="from"/>, <paramref name="to"/>].</summary>
        public static int CountBusinessDays(DateTime from, DateTime to, IEnumerable<DateTime>? holidays = null)
        {
            DateTime start = from.Date;
            DateTime end = to.Date;
            if (start > end)
                throw new ArgumentException("from must not be after to.", nameof(from));

            var set = BuildHolidaySet(holidays);
            int count = 0;
            for (DateTime d = start; d <= end; d = d.AddDays(1))
                if (IsBusinessDay(d, set))
                    count++;
            return count;
        }

        private static bool IsBusinessDay(DateTime date, HashSet<DateTime>? holidays)
        {
            if (date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday)
                return false;
            return holidays is null || !holidays.Contains(date);
        }

        private static HashSet<DateTime>? BuildHolidaySet(IEnumerable<DateTime>? holidays)
        {
            if (holidays is null)
                return null;
            var set = new HashSet<DateTime>();
            foreach (var h in holidays)
                set.Add(h.Date);
            return set;
        }
    }
}
