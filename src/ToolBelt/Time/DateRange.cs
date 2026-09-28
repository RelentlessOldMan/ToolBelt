// ToolBelt drop-in — fully self-contained (BCL only).
using System;

namespace ToolBelt.Time
{
    /// <summary>
    /// A closed time interval <c>[Start, End]</c> (both endpoints inclusive) with containment, overlap,
    /// and intersection tests. Construction requires <c>Start &lt;= End</c>. Comparisons use the absolute
    /// instants, so ranges in different offsets compare correctly.
    /// </summary>
    public readonly struct DateRange : IEquatable<DateRange>
    {
        public DateRange(DateTimeOffset start, DateTimeOffset end)
        {
            if (end < start)
                throw new ArgumentException("End must be on or after start.", nameof(end));
            Start = start;
            End = end;
        }

        /// <summary>The inclusive start instant.</summary>
        public DateTimeOffset Start { get; }

        /// <summary>The inclusive end instant.</summary>
        public DateTimeOffset End { get; }

        /// <summary>The length of the interval (<see cref="End"/> - <see cref="Start"/>).</summary>
        public TimeSpan Duration => End - Start;

        /// <summary>Whether <paramref name="instant"/> lies within the range (inclusive of both ends).</summary>
        public bool Contains(DateTimeOffset instant) => instant >= Start && instant <= End;

        /// <summary>Whether this range shares any instant with <paramref name="other"/>.</summary>
        public bool Overlaps(DateRange other) => Start <= other.End && other.Start <= End;

        /// <summary>The overlapping range, or null if the two ranges are disjoint.</summary>
        public DateRange? Intersect(DateRange other)
        {
            if (!Overlaps(other))
                return null;
            var start = Start > other.Start ? Start : other.Start;
            var end = End < other.End ? End : other.End;
            return new DateRange(start, end);
        }

        public bool Equals(DateRange other) => Start == other.Start && End == other.End;

        public override bool Equals(object? obj) => obj is DateRange other && Equals(other);

        public override int GetHashCode() => Start.GetHashCode() ^ End.GetHashCode();

        public override string ToString() => $"[{Start:o} .. {End:o}]";
    }
}
