using System;
using System.Linq;
using ToolBelt.Time;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Time
{
    public sealed class CronScheduleTests
    {
        private static DateTimeOffset T(int y, int mo, int d, int h, int mi)
            => new DateTimeOffset(y, mo, d, h, mi, 0, TimeSpan.Zero);

        public void EveryMinute()
        {
            var cron = new CronSchedule("* * * * *");
            Check.Equal(T(2026, 1, 1, 10, 1),
                cron.GetNextOccurrence(new DateTimeOffset(2026, 1, 1, 10, 0, 30, TimeSpan.Zero)));
        }

        public void DailyMidnight()
        {
            var cron = new CronSchedule("0 0 * * *");
            Check.Equal(T(2026, 3, 16, 0, 0), cron.GetNextOccurrence(T(2026, 3, 15, 10, 0)));
        }

        public void EveryFifteenMinutes()
        {
            var cron = new CronSchedule("*/15 * * * *");
            Check.Equal(T(2026, 1, 1, 10, 15), cron.GetNextOccurrence(T(2026, 1, 1, 10, 7)));
            Check.Equal(T(2026, 1, 1, 10, 30), cron.GetNextOccurrence(T(2026, 1, 1, 10, 15)));
        }

        public void ListsAndRanges()
        {
            var cron = new CronSchedule("0,30 9-17 * * *");
            var next = cron.GetNextOccurrence(T(2026, 1, 1, 8, 45));
            Check.Equal(T(2026, 1, 1, 9, 0), next);        // first slot in the 9-17 window
            Check.Equal(T(2026, 1, 1, 9, 30), cron.GetNextOccurrence(next));
        }

        public void Weekdays9am_LandsOnWeekdayAt9()
        {
            var cron = new CronSchedule("0 9 * * 1-5");
            var next = cron.GetNextOccurrence(T(2026, 1, 3, 12, 0)); // arbitrary start
            Check.Equal(9, next.Hour);
            Check.Equal(0, next.Minute);
            Check.True(next.DayOfWeek >= DayOfWeek.Monday && next.DayOfWeek <= DayOfWeek.Friday,
                $"expected a weekday, got {next.DayOfWeek}");
        }

        public void DomOrDow_Semantics()
        {
            // Both restricted -> match the 1st of the month OR any Monday.
            var cron = new CronSchedule("0 0 1 * 1");
            for (var t = T(2026, 1, 1, 0, 0); t < T(2026, 4, 1, 0, 0); )
            {
                var next = cron.GetNextOccurrence(t);
                Check.True(next.Day == 1 || next.DayOfWeek == DayOfWeek.Monday,
                    $"{next:o} is neither the 1st nor a Monday");
                t = next;
            }
        }

        public void SundayAcceptsZeroAndSeven()
        {
            Check.Equal(DayOfWeek.Sunday, new CronSchedule("0 0 * * 0").GetNextOccurrence(T(2026, 1, 1, 0, 0)).DayOfWeek);
            Check.Equal(DayOfWeek.Sunday, new CronSchedule("0 0 * * 7").GetNextOccurrence(T(2026, 1, 1, 0, 0)).DayOfWeek);
        }

        public void Unsatisfiable_Throws()
        {
            var cron = new CronSchedule("0 0 30 2 *"); // February 30th never exists
            Check.Throws<InvalidOperationException>(() => cron.GetNextOccurrence(T(2026, 1, 1, 0, 0)));
        }

        public void GetNextOccurrences_ReturnsSequence()
        {
            var cron = new CronSchedule("0 * * * *"); // top of every hour
            var occ = cron.GetNextOccurrences(T(2026, 1, 1, 10, 30), 3);
            Check.True(occ.SequenceEqual(new[]
            {
                T(2026, 1, 1, 11, 0), T(2026, 1, 1, 12, 0), T(2026, 1, 1, 13, 0),
            }));
        }

        public void InvalidExpressions_Throw()
        {
            Check.Throws<FormatException>(() => new CronSchedule("* * * *"));       // 4 fields
            Check.Throws<FormatException>(() => new CronSchedule("60 * * * *"));    // minute out of range
            Check.Throws<FormatException>(() => new CronSchedule("*/0 * * * *"));   // zero step
            Check.Throws<ArgumentNullException>(() => new CronSchedule(null!));
        }
    }
}
