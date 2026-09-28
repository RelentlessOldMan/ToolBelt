// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;

namespace ToolBelt.Intervals
{
    /// <summary>
    /// A half-open interval [<see cref="Start"/>, <see cref="End"/>) over any comparable key. Half-open
    /// semantics make adjacent intervals coalesce cleanly ([a,b) followed by [b,c) is [a,c)), which is why
    /// the range structures in this namespace use it. <see cref="Start"/> may equal <see cref="End"/> (an
    /// empty interval that contains nothing and overlaps nothing); <see cref="Start"/> after
    /// <see cref="End"/> is rejected.
    /// </summary>
    public readonly struct Interval<TKey> : IEquatable<Interval<TKey>> where TKey : IComparable<TKey>
    {
        public TKey Start { get; }
        public TKey End { get; }

        public Interval(TKey start, TKey end)
        {
            if (Compare(start, end) > 0)
                throw new ArgumentException($"Interval start '{start}' must not be greater than end '{end}'.");
            Start = start;
            End = end;
        }

        /// <summary>True if the interval covers no points (<see cref="Start"/> == <see cref="End"/>).</summary>
        public bool IsEmpty => Compare(Start, End) == 0;

        /// <summary>True if <paramref name="point"/> lies in [Start, End).</summary>
        public bool Contains(TKey point) => Compare(Start, point) <= 0 && Compare(point, End) < 0;

        /// <summary>True if this interval and <paramref name="other"/> share any point. Empty intervals overlap nothing.</summary>
        public bool Overlaps(Interval<TKey> other)
        {
            if (IsEmpty || other.IsEmpty) return false;
            return Compare(Start, other.End) < 0 && Compare(other.Start, End) < 0;
        }

        internal static int Compare(TKey a, TKey b) => Comparer<TKey>.Default.Compare(a, b);

        public bool Equals(Interval<TKey> other) => Compare(Start, other.Start) == 0 && Compare(End, other.End) == 0;
        public override bool Equals(object? obj) => obj is Interval<TKey> i && Equals(i);
        public static bool operator ==(Interval<TKey> a, Interval<TKey> b) => a.Equals(b);
        public static bool operator !=(Interval<TKey> a, Interval<TKey> b) => !a.Equals(b);

        public override int GetHashCode()
        {
            unchecked { return (Start?.GetHashCode() ?? 0) * 397 ^ (End?.GetHashCode() ?? 0); }
        }

        public override string ToString() => $"[{Start}, {End})";
    }
}
