using System;
using ToolBelt.Time;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Time
{
    public sealed class TimeOfDayRangeTests
    {
        private static TimeSpan T(int h, int m = 0) => new TimeSpan(h, m, 0);

        public void NormalWindow_Contains()
        {
            var business = new TimeOfDayRange(T(9), T(17));
            Check.False(business.WrapsMidnight);
            Check.True(business.Contains(T(9)));       // inclusive start
            Check.True(business.Contains(T(12, 30)));
            Check.False(business.Contains(T(17)));     // exclusive end
            Check.False(business.Contains(T(8, 59)));
            Check.Equal(TimeSpan.FromHours(8), business.Duration);
        }

        public void OvernightWindow_Contains()
        {
            var night = new TimeOfDayRange(T(22), T(6));
            Check.True(night.WrapsMidnight);
            Check.True(night.Contains(T(23)));
            Check.True(night.Contains(T(0)));
            Check.True(night.Contains(T(5, 59)));
            Check.False(night.Contains(T(6)));   // exclusive end
            Check.False(night.Contains(T(12)));
            Check.Equal(TimeSpan.FromHours(8), night.Duration);
        }

        public void EmptyWindow_ContainsNothing()
        {
            var empty = new TimeOfDayRange(T(9), T(9));
            Check.True(empty.IsEmpty);
            Check.False(empty.Contains(T(9)));
            Check.Equal(TimeSpan.Zero, empty.Duration);
        }

        public void ContainsDateTime()
        {
            var business = new TimeOfDayRange(T(9), T(17));
            Check.True(business.Contains(new DateTime(2026, 1, 1, 10, 0, 0)));
            Check.False(business.Contains(new DateTime(2026, 1, 1, 18, 0, 0)));
            Check.True(business.Contains(new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero)));
        }

        public void InvalidArguments_Throw()
        {
            Check.Throws<ArgumentOutOfRangeException>(() => new TimeOfDayRange(T(-1), T(5)));
            Check.Throws<ArgumentOutOfRangeException>(() => new TimeOfDayRange(T(0), TimeSpan.FromHours(24)));
            var r = new TimeOfDayRange(T(9), T(17));
            Check.Throws<ArgumentOutOfRangeException>(() => r.Contains(TimeSpan.FromHours(25)));
        }

        // Property: Contains + its complement partition the day; exactly Duration worth of minutes are inside.
        public void Property_MinutesInsideEqualDuration()
        {
            var ranges = new[]
            {
                new TimeOfDayRange(T(9), T(17)),
                new TimeOfDayRange(T(22), T(6)),
                new TimeOfDayRange(T(0), T(1)),
                new TimeOfDayRange(T(23, 30), T(0, 15)),
            };
            foreach (var range in ranges)
            {
                int inside = 0;
                for (int minute = 0; minute < 1440; minute++)
                    if (range.Contains(TimeSpan.FromMinutes(minute))) inside++;
                Check.Equal((int)range.Duration.TotalMinutes, inside, $"range {range}");
            }
        }
    }
}
