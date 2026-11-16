// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;

namespace ToolBelt.Collections
{
    /// <summary>Per-group summary statistics.</summary>
    public sealed class GroupSummary<TKey>
    {
        internal GroupSummary(TKey key, int count, double sum, double mean, double min, double max, double stdDev)
        {
            Key = key; Count = count; Sum = sum; Mean = mean; Min = min; Max = max; StandardDeviation = stdDev;
        }

        public TKey Key { get; }
        public int Count { get; }
        public double Sum { get; }
        public double Mean { get; }
        public double Min { get; }
        public double Max { get; }
        /// <summary>Sample standard deviation (n-1); 0 for a single-element group.</summary>
        public double StandardDeviation { get; }
    }

    /// <summary>
    /// Group-by aggregation — the most common analysis operation people perform by hand with dictionaries.
    /// <see cref="Summarize"/> produces per-group count/sum/mean/min/max/stddev; <see cref="Pivot"/> turns
    /// long-form rows into a wide row×column grid with a chosen aggregator. Groups are returned in first-seen
    /// order for reproducible output.
    /// </summary>
    public static class Aggregation
    {
        // .NET Framework hashes a double or float NaN by its bits and a decimal by its scale, so equal keys (NaNs with
        // other payloads, 1.0m and 1.00m) fell into separate buckets: one key counted twice. Keys of those types hash
        // by value; everything else uses the default comparer.
        private static class ValueKeys
        {
            public static IEqualityComparer<TK> For<TK>()
            {
#if NETSTANDARD2_0
                object? c = typeof(TK) == typeof(double) ? DoubleKeys.Instance
                    : typeof(TK) == typeof(float) ? SingleKeys.Instance
                    : typeof(TK) == typeof(decimal) ? DecimalKeys.Instance
                    : typeof(TK) == typeof(double?) ? new NullableKeys<double>(DoubleKeys.Instance)
                    : typeof(TK) == typeof(float?) ? new NullableKeys<float>(SingleKeys.Instance)
                    : typeof(TK) == typeof(decimal?) ? new NullableKeys<decimal>(DecimalKeys.Instance)
                    : (object?)null;
                if (c is not null) return (IEqualityComparer<TK>)c;
#endif
                return EqualityComparer<TK>.Default;
            }
#if NETSTANDARD2_0

            private sealed class DoubleKeys : IEqualityComparer<double>
            {
                public static readonly DoubleKeys Instance = new DoubleKeys();
                public bool Equals(double x, double y) => x.Equals(y);
                public int GetHashCode(double v) => double.IsNaN(v) ? -1 : v.GetHashCode();
            }

            private sealed class SingleKeys : IEqualityComparer<float>
            {
                public static readonly SingleKeys Instance = new SingleKeys();
                public bool Equals(float x, float y) => x.Equals(y);
                public int GetHashCode(float v) => float.IsNaN(v) ? -1 : v.GetHashCode();
            }

            private sealed class DecimalKeys : IEqualityComparer<decimal>
            {
                public static readonly DecimalKeys Instance = new DecimalKeys();
                public bool Equals(decimal x, decimal y) => x == y;
                // Not through double: the conversion can round 1.0m and 1.00m-style pairs apart (the Framework's own bug).
                // Dividing by one drops trailing zeros, so equal nonzero decimals have one representation.
                public int GetHashCode(decimal v)
                {
                    if (v == 0) return 0;   // 0m and -0m
                    int[] b = decimal.GetBits(v / 1.000000000000000000000000000000000m);
                    return unchecked(b[0] ^ (b[1] * 31) ^ (b[2] * 961) ^ b[3]);
                }
            }

            private sealed class NullableKeys<TV> : IEqualityComparer<TV?> where TV : struct
            {
                private readonly IEqualityComparer<TV> _inner;
                public NullableKeys(IEqualityComparer<TV> inner) => _inner = inner;
                public bool Equals(TV? x, TV? y) => x.HasValue ? y.HasValue && _inner.Equals(x.Value, y.Value) : !y.HasValue;
                public int GetHashCode(TV? v) => v.HasValue ? _inner.GetHashCode(v.Value) : 0;
            }
#endif
        }

        public static IReadOnlyList<GroupSummary<TKey>> Summarize<TItem, TKey>(
            IEnumerable<TItem> items, Func<TItem, TKey> keySelector, Func<TItem, double> valueSelector,
            IEqualityComparer<TKey>? comparer = null)
            where TKey : notnull
        {
            if (items is null) throw new ArgumentNullException(nameof(items));
            if (keySelector is null) throw new ArgumentNullException(nameof(keySelector));
            if (valueSelector is null) throw new ArgumentNullException(nameof(valueSelector));

            // One running accumulator per group: every value of every group was kept (20 million items held 384 MB).
            var order = new List<TKey>();
            var groups = new Dictionary<TKey, Running>(comparer ?? ValueKeys.For<TKey>());
            foreach (TItem item in items)
            {
                TKey key = keySelector(item);
                if (key is null) throw new ArgumentException("keySelector returned null for an item.", nameof(keySelector));   // the dictionary threw ArgumentNullException("key")
                if (!groups.TryGetValue(key, out var acc))
                {
                    acc = new Running();
                    groups[key] = acc;
                    order.Add(key);
                }
                acc.Add(valueSelector(item));
            }

            var result = new List<GroupSummary<TKey>>(order.Count);
            foreach (TKey key in order)
                result.Add(groups[key].Summary(key));
            return result;
        }

        // Count, sum, min and max as plain running values, and Welford's mean and squared deviations on the values scaled
        // by 2^-k, where 2^k is near the largest |v| so far (rescaled, exactly, by a power of two when a larger one
        // comes). Scaled values stay near 1, so finite values whose sum or squared deviations overflow (1e308 + 1e308)
        // or underflow (1e-200 and 2e-200 had a deviation of 0) keep their spread.
        private sealed class Running
        {
            private long _count;
            private double _sum, _min = double.PositiveInfinity, _max = double.NegativeInfinity;
            private bool _nonFinite;
            private int _k = int.MinValue;   // no nonzero finite value yet
            private double _scaledSum, _scaledMean, _scaledM2;
            private long _scaledCount;

            public void Add(double v)
            {
                if (_count == int.MaxValue) throw new OverflowException("A group has more than int.MaxValue items.");
                _count++;
                _sum += v;
                if (double.IsNaN(v)) { _min = _max = double.NaN; _nonFinite = true; return; }
                if (v < _min) _min = v; // a NaN min/max stays NaN: comparisons with NaN are false
                if (v > _max) _max = v;
                if (double.IsInfinity(v)) { _nonFinite = true; return; }
                if (v != 0)
                {
                    // Log2 of a value near double.MaxValue rounds to 1024, and 2^1024 is ∞; 2^1023 still scales exactly.
                    int e = Math.Min(1023, (int)Math.Floor(Math.Log(Math.Abs(v), 2)));
                    if (_k == int.MinValue) _k = e;
                    else if (e > _k)
                    {
                        _scaledSum = Scale(_scaledSum, _k - e);
                        _scaledMean = Scale(_scaledMean, _k - e);
                        _scaledM2 = Scale(Scale(_scaledM2, _k - e), _k - e);
                        _k = e;
                    }
                }
                double x = _k == int.MinValue ? 0 : Scale(v, -_k);
                _scaledCount++;
                _scaledSum += x;
                double d = x - _scaledMean;
                _scaledMean += d / _scaledCount;
                _scaledM2 += d * (x - _scaledMean);
            }

            public GroupSummary<TKey> Summary<TKey>(TKey key)
            {
                int n = (int)_count;
                double sum = _sum, mean = _sum / n, stdDev;
                if (_nonFinite)
                {
                    stdDev = n > 1 ? double.NaN : 0;   // ∞ − ∞ (or a NaN) in the deviations
                }
                else
                {
                    int k = _k == int.MinValue ? 0 : _k;
                    stdDev = n > 1 ? Scale(Math.Sqrt(_scaledM2 / (n - 1)), k) : 0;
                    double peak = Math.Max(Math.Abs(_min), Math.Abs(_max));
                    if (double.IsInfinity(sum) || (peak > 0 && peak < 3.8725919148493183e-121))   // 2^-400
                    {
                        // The sum too: it was left at the overflowed ∞ when the true sum ([1e308, 1e308, −1e308]) is finite.
                        sum = Scale(_scaledSum, k);
                        mean = Scale(_scaledSum / n, k);
                    }
                }
                return new GroupSummary<TKey>(key, n, sum, mean, _min, _max, stdDev);
            }

            // x·2^e, in steps that each stay in range (2^±k alone can pass it).
            private static double Scale(double x, int e)
            {
                while (e > 1000) { x *= Math.Pow(2, 1000); e -= 1000; }
                while (e < -1000) { x *= Math.Pow(2, -1000); e += 1000; }
                return x * Math.Pow(2, e);
            }
        }

        public static Dictionary<TRow, Dictionary<TCol, double>> Pivot<TItem, TRow, TCol>(
            IEnumerable<TItem> items,
            Func<TItem, TRow> rowSelector, Func<TItem, TCol> colSelector, Func<TItem, double> valueSelector,
            Func<IReadOnlyList<double>, double> aggregator)
            where TRow : notnull
            where TCol : notnull
        {
            if (items is null) throw new ArgumentNullException(nameof(items));
            if (rowSelector is null) throw new ArgumentNullException(nameof(rowSelector));
            if (colSelector is null) throw new ArgumentNullException(nameof(colSelector));
            if (valueSelector is null) throw new ArgumentNullException(nameof(valueSelector));
            if (aggregator is null) throw new ArgumentNullException(nameof(aggregator));

            var cells = new Dictionary<TRow, Dictionary<TCol, List<double>>>(ValueKeys.For<TRow>());
            foreach (TItem item in items)
            {
                TRow row = rowSelector(item);
                if (row is null) throw new ArgumentException("rowSelector returned null for an item.", nameof(rowSelector));   // the dictionary threw ArgumentNullException("key")
                TCol col = colSelector(item);
                if (col is null) throw new ArgumentException("colSelector returned null for an item.", nameof(colSelector));   // the dictionary threw ArgumentNullException("key")
                if (!cells.TryGetValue(row, out var cols))
                {
                    cols = new Dictionary<TCol, List<double>>(ValueKeys.For<TCol>());
                    cells[row] = cols;
                }
                if (!cols.TryGetValue(col, out var list))
                {
                    list = new List<double>();
                    cols[col] = list;
                }
                list.Add(valueSelector(item));
            }

            var result = new Dictionary<TRow, Dictionary<TCol, double>>(ValueKeys.For<TRow>());
            foreach (var rowEntry in cells)
            {
                var aggregated = new Dictionary<TCol, double>(ValueKeys.For<TCol>());
                foreach (var colEntry in rowEntry.Value)
                    aggregated[colEntry.Key] = aggregator(colEntry.Value);
                result[rowEntry.Key] = aggregated;
            }
            return result;
        }
    }
}
