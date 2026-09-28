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
        public static IReadOnlyList<GroupSummary<TKey>> Summarize<TItem, TKey>(
            IEnumerable<TItem> items, Func<TItem, TKey> keySelector, Func<TItem, double> valueSelector,
            IEqualityComparer<TKey>? comparer = null)
            where TKey : notnull
        {
            if (items is null) throw new ArgumentNullException(nameof(items));
            if (keySelector is null) throw new ArgumentNullException(nameof(keySelector));
            if (valueSelector is null) throw new ArgumentNullException(nameof(valueSelector));

            var order = new List<TKey>();
            var values = new Dictionary<TKey, List<double>>(comparer ?? EqualityComparer<TKey>.Default);
            foreach (TItem item in items)
            {
                TKey key = keySelector(item);
                if (!values.TryGetValue(key, out var list))
                {
                    list = new List<double>();
                    values[key] = list;
                    order.Add(key);
                }
                list.Add(valueSelector(item));
            }

            var result = new List<GroupSummary<TKey>>(order.Count);
            foreach (TKey key in order)
            {
                List<double> group = values[key];
                double sum = 0, min = double.PositiveInfinity, max = double.NegativeInfinity;
                foreach (double v in group) { sum += v; if (v < min) min = v; if (v > max) max = v; }
                double mean = sum / group.Count;
                double variance = 0;
                if (group.Count > 1)
                {
                    foreach (double v in group) { double d = v - mean; variance += d * d; }
                    variance /= group.Count - 1;
                }
                result.Add(new GroupSummary<TKey>(key, group.Count, sum, mean, min, max, Math.Sqrt(variance)));
            }
            return result;
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

            var cells = new Dictionary<TRow, Dictionary<TCol, List<double>>>();
            foreach (TItem item in items)
            {
                TRow row = rowSelector(item);
                TCol col = colSelector(item);
                if (!cells.TryGetValue(row, out var cols))
                {
                    cols = new Dictionary<TCol, List<double>>();
                    cells[row] = cols;
                }
                if (!cols.TryGetValue(col, out var list))
                {
                    list = new List<double>();
                    cols[col] = list;
                }
                list.Add(valueSelector(item));
            }

            var result = new Dictionary<TRow, Dictionary<TCol, double>>();
            foreach (var rowEntry in cells)
            {
                var aggregated = new Dictionary<TCol, double>();
                foreach (var colEntry in rowEntry.Value)
                    aggregated[colEntry.Key] = aggregator(colEntry.Value);
                result[rowEntry.Key] = aggregated;
            }
            return result;
        }
    }
}
