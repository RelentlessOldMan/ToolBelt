using System;
using ToolBelt.Time;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Time
{
    public sealed class DateRangeTests
    {
        private static DateTimeOffset D(int day) => new DateTimeOffset(2026, 1, day, 0, 0, 0, TimeSpan.Zero);

        public void Duration()
        {
            Check.Equal(TimeSpan.FromDays(4), new DateRange(D(1), D(5)).Duration);
        }

        public void Contains_Inclusive()
        {
            var r = new DateRange(D(1), D(5));
            Check.True(r.Contains(D(1)));
            Check.True(r.Contains(D(3)));
            Check.True(r.Contains(D(5)));
            Check.False(r.Contains(D(6)));
        }

        public void Overlaps()
        {
            Check.True(new DateRange(D(1), D(5)).Overlaps(new DateRange(D(4), D(8))));
            Check.True(new DateRange(D(1), D(5)).Overlaps(new DateRange(D(5), D(9)))); // touch at endpoint
            Check.False(new DateRange(D(1), D(5)).Overlaps(new DateRange(D(6), D(9))));
        }

        public void Overlaps_IsSymmetric()
        {
            var a = new DateRange(D(1), D(5));
            var b = new DateRange(D(4), D(9));
            Check.Equal(a.Overlaps(b), b.Overlaps(a));
        }

        public void Intersect()
        {
            var i = new DateRange(D(1), D(5)).Intersect(new DateRange(D(3), D(9)));
            Check.True(i.HasValue);
            Check.Equal(D(3), i!.Value.Start);
            Check.Equal(D(5), i.Value.End);

            Check.False(new DateRange(D(1), D(2)).Intersect(new DateRange(D(5), D(6))).HasValue);
        }

        public void InvalidRange_Throws()
        {
            Check.Throws<ArgumentException>(() => new DateRange(D(5), D(1)));
        }

        // Property: intersect exists iff the ranges overlap, and the intersection lies within both.
        public void Property_IntersectMatchesOverlap()
        {
            var rng = new Random(909);
            for (int trial = 0; trial < 2000; trial++)
            {
                var a = MakeRange(rng);
                var b = MakeRange(rng);
                var i = a.Intersect(b);

                Check.Equal(a.Overlaps(b), i.HasValue, $"trial {trial}: overlap vs intersect presence");
                if (i.HasValue)
                {
                    var r = i.Value;
                    Check.True(r.Start >= a.Start && r.Start >= b.Start, $"trial {trial}: start bound");
                    Check.True(r.End <= a.End && r.End <= b.End, $"trial {trial}: end bound");
                }
            }
        }

        private static DateRange MakeRange(Random rng)
        {
            int start = rng.Next(1, 20);
            int end = start + rng.Next(0, 10);
            return new DateRange(D(start), D(end));
        }
    }
}
