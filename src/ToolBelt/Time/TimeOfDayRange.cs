// ToolBelt drop-in — fully self-contained (BCL only).
using System;

namespace ToolBelt.Time
{
    /// <summary>
    /// A half-open time-of-day window [Start, End) within a 24-hour clock, independent of any date. When
    /// <see cref="Start"/> is later than <see cref="End"/> the window wraps past midnight (e.g. 22:00–06:00).
    /// A window whose start equals its end is empty and contains no instant.
    /// </summary>
    public readonly struct TimeOfDayRange
    {
        private static readonly TimeSpan Day = TimeSpan.FromHours(24);

        public TimeSpan Start { get; }
        public TimeSpan End { get; }

        public TimeOfDayRange(TimeSpan start, TimeSpan end)
        {
            if (start < TimeSpan.Zero || start >= Day)
                throw new ArgumentOutOfRangeException(nameof(start), start, "Start must be within [00:00, 24:00).");
            if (end < TimeSpan.Zero || end >= Day)
                throw new ArgumentOutOfRangeException(nameof(end), end, "End must be within [00:00, 24:00).");
            Start = start;
            End = end;
        }

        /// <summary>True if the window crosses midnight (start &gt; end).</summary>
        public bool WrapsMidnight => Start > End;

        /// <summary>True if the window is empty (start == end).</summary>
        public bool IsEmpty => Start == End;

        /// <summary>The length of the window, accounting for a midnight wrap.</summary>
        public TimeSpan Duration => WrapsMidnight ? Day - Start + End : End - Start;

        /// <summary>True if the given time-of-day falls within the half-open window.</summary>
        public bool Contains(TimeSpan timeOfDay)
        {
            if (timeOfDay < TimeSpan.Zero || timeOfDay >= Day)
                throw new ArgumentOutOfRangeException(nameof(timeOfDay), timeOfDay, "Time of day must be within [00:00, 24:00).");
            if (IsEmpty) return false;
            return WrapsMidnight
                ? timeOfDay >= Start || timeOfDay < End
                : timeOfDay >= Start && timeOfDay < End;
        }

        /// <summary>True if the clock time of the given moment falls within the window.</summary>
        public bool Contains(DateTime moment) => Contains(moment.TimeOfDay);

        /// <summary>True if the clock time of the given moment falls within the window.</summary>
        public bool Contains(DateTimeOffset moment) => Contains(moment.TimeOfDay);

        public override string ToString() => $"[{Start:hh\\:mm}, {End:hh\\:mm})";
    }
}
