using System;
using ToolBelt.Time;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Time
{
    public sealed class BusinessDaysTests
    {
        private static DateTime D(int y, int m, int d) => new DateTime(y, m, d);

        public void IsBusinessDay()
        {
            // 2026-01-02 is a Friday, 2026-01-03 Saturday, 2026-01-04 Sunday, 2026-01-05 Monday.
            Check.True(BusinessDays.IsBusinessDay(D(2026, 1, 2)));
            Check.False(BusinessDays.IsBusinessDay(D(2026, 1, 3)));
            Check.False(BusinessDays.IsBusinessDay(D(2026, 1, 4)));
            Check.True(BusinessDays.IsBusinessDay(D(2026, 1, 5)));
        }

        public void AddBusinessDays_SkipsWeekend()
        {
            // Friday + 1 business day -> Monday.
            Check.Equal(D(2026, 1, 5), BusinessDays.AddBusinessDays(D(2026, 1, 2), 1));
            // Monday - 1 business day -> Friday.
            Check.Equal(D(2026, 1, 2), BusinessDays.AddBusinessDays(D(2026, 1, 5), -1));
            // Zero is a no-op.
            Check.Equal(D(2026, 1, 2), BusinessDays.AddBusinessDays(D(2026, 1, 2), 0));
        }

        public void AddBusinessDays_WithHolidays()
        {
            var holidays = new[] { D(2026, 1, 5) }; // Monday is a holiday
            // Friday + 1 -> skip Sat, Sun, Mon(holiday) -> Tuesday.
            Check.Equal(D(2026, 1, 6), BusinessDays.AddBusinessDays(D(2026, 1, 2), 1, holidays));
        }

        public void CountBusinessDays()
        {
            // 2026-01-05 (Mon) through 2026-01-11 (Sun): Mon-Fri = 5 business days.
            Check.Equal(5, BusinessDays.CountBusinessDays(D(2026, 1, 5), D(2026, 1, 11)));
            // Same range minus a mid-week holiday.
            Check.Equal(4, BusinessDays.CountBusinessDays(D(2026, 1, 5), D(2026, 1, 11), new[] { D(2026, 1, 7) }));
            // Single business day, inclusive.
            Check.Equal(1, BusinessDays.CountBusinessDays(D(2026, 1, 5), D(2026, 1, 5)));
        }

        public void HolidaysMatchByDateIgnoringTime()
        {
            var holidays = new[] { new DateTime(2026, 1, 5, 14, 30, 0) }; // has a time component
            Check.False(BusinessDays.IsBusinessDay(D(2026, 1, 5), holidays));
        }

        public void InvalidRange_Throws()
        {
            Check.Throws<ArgumentException>(() => BusinessDays.CountBusinessDays(D(2026, 1, 10), D(2026, 1, 5)));
        }

        // Property: AddBusinessDays(start, n) then AddBusinessDays(result, -n) returns to a business day
        // equivalent to the start's next business day, and always lands on a business day.
        public void Property_AddAlwaysLandsOnBusinessDay()
        {
            var rng = new Random(11);
            for (int trial = 0; trial < 1000; trial++)
            {
                var start = D(2026, 1, 1).AddDays(rng.Next(0, 400));
                int n = rng.Next(1, 30);
                var forward = BusinessDays.AddBusinessDays(start, n);
                Check.True(BusinessDays.IsBusinessDay(forward), $"trial {trial}: {forward:d} not a business day");
                // Going back n business days from a business day returns to a business day.
                var back = BusinessDays.AddBusinessDays(forward, -n);
                Check.True(BusinessDays.IsBusinessDay(back), $"trial {trial}: {back:d} not a business day");
            }
        }
    }
}
