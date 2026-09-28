using System;
using ToolBelt.Time;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Time
{
    public sealed class IsoWeekTests
    {
        private static DateTime D(int y, int m, int d) => new DateTime(y, m, d);

        public void IsoDayOfWeek()
        {
            Check.Equal(1, IsoWeek.GetIsoDayOfWeek(D(2026, 1, 5)));  // Monday
            Check.Equal(4, IsoWeek.GetIsoDayOfWeek(D(2026, 1, 1)));  // Thursday
            Check.Equal(7, IsoWeek.GetIsoDayOfWeek(D(2026, 1, 4)));  // Sunday
        }

        public void KnownWeeks()
        {
            // 2026-01-01 is Thursday -> ISO 2026-W01.
            Check.Equal(1, IsoWeek.GetWeekOfYear(D(2026, 1, 1)));
            Check.Equal(2026, IsoWeek.GetWeekYear(D(2026, 1, 1)));
        }

        public void YearBoundary_BelongsToPreviousYear()
        {
            // 2021-01-01 (Friday) belongs to ISO 2020-W53.
            Check.Equal(53, IsoWeek.GetWeekOfYear(D(2021, 1, 1)));
            Check.Equal(2020, IsoWeek.GetWeekYear(D(2021, 1, 1)));

            // 2005-01-01 (Saturday) belongs to ISO 2004-W53.
            Check.Equal(53, IsoWeek.GetWeekOfYear(D(2005, 1, 1)));
            Check.Equal(2004, IsoWeek.GetWeekYear(D(2005, 1, 1)));
        }

        public void FiftyThreeWeekYear()
        {
            // 2015-12-31 (Thursday) is ISO 2015-W53.
            Check.Equal(53, IsoWeek.GetWeekOfYear(D(2015, 12, 31)));
            Check.Equal(2015, IsoWeek.GetWeekYear(D(2015, 12, 31)));
        }

        public void EndOfYear_BelongsToNextYear()
        {
            // 2018-12-31 (Monday) belongs to ISO 2019-W01.
            Check.Equal(1, IsoWeek.GetWeekOfYear(D(2018, 12, 31)));
            Check.Equal(2019, IsoWeek.GetWeekYear(D(2018, 12, 31)));
        }

        // Property: every day in a given ISO week shares the same (weekYear, week), and all 7 days of a
        // week map to the same week number.
        public void Property_WeekIsStableAcrossItsSevenDays()
        {
            var monday = D(2026, 1, 5); // a Monday
            int week = IsoWeek.GetWeekOfYear(monday);
            int weekYear = IsoWeek.GetWeekYear(monday);
            for (int i = 0; i < 7; i++)
            {
                var day = monday.AddDays(i);
                Check.Equal(week, IsoWeek.GetWeekOfYear(day), $"day +{i}");
                Check.Equal(weekYear, IsoWeek.GetWeekYear(day), $"day +{i}");
            }
        }
    }
}
