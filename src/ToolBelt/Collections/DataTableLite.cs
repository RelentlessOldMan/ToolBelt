// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ToolBelt.Collections
{
    /// <summary>
    /// A small in-memory table: named columns and rows of loosely-typed cells (<c>object?</c>), with
    /// query-by-column-name operations (project, filter, sort, distinct, group, inner join). This is the
    /// untyped counterpart to plain LINQ-over-objects — useful when the schema is only known at runtime
    /// (CSV headers, query results) and columns are addressed by name rather than by a compiled property.
    /// Every query returns a NEW table; the source is never mutated. Not thread-safe.
    /// </summary>
    public sealed class DataTableLite
    {
        private readonly string[] _columns;
        private readonly IReadOnlyList<string> _columnsView;   // Columns handed out the array, so a cast could rename a column
        private readonly Dictionary<string, int> _index; // column name -> ordinal (ordinal, case-sensitive)
        private readonly List<object?[]> _rows;

        /// <summary>Creates an empty table with the given ordered, unique column names.</summary>
        public DataTableLite(IEnumerable<string> columns)
        {
            if (columns is null) throw new ArgumentNullException(nameof(columns));
            var list = new List<string>();
            var index = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (string c in columns)
            {
                if (c is null) throw new ArgumentException("Column names must not be null.", nameof(columns));
                if (index.ContainsKey(c)) throw new ArgumentException($"Duplicate column name '{c}'.", nameof(columns));
                index[c] = list.Count;
                list.Add(c);
            }
            if (list.Count == 0) throw new ArgumentException("A table needs at least one column.", nameof(columns));
            _columns = list.ToArray();
            _columnsView = Array.AsReadOnly(_columns);
            _index = index;
            _rows = new List<object?[]>();
        }

        private DataTableLite(string[] columns, Dictionary<string, int> index, List<object?[]> rows)
        {
            _columns = columns;
            _columnsView = Array.AsReadOnly(_columns);
            _index = index;
            _rows = rows;
        }

        /// <summary>The column names in order.</summary>
        public IReadOnlyList<string> Columns => _columnsView;

        /// <summary>Number of columns.</summary>
        public int ColumnCount => _columns.Length;

        /// <summary>Number of rows.</summary>
        public int RowCount => _rows.Count;

        /// <summary>The ordinal of a column, or -1 if it is not present.</summary>
        public int IndexOf(string column) => _index.TryGetValue(column ?? throw new ArgumentNullException(nameof(column)), out int i) ? i : -1;

        /// <summary>Whether the table has a column of the given name.</summary>
        public bool HasColumn(string column) => IndexOf(column) >= 0;

        /// <summary>Appends a row; its length must equal <see cref="ColumnCount"/>. Returns this table for chaining.</summary>
        public DataTableLite AddRow(params object?[] values)
        {
            if (values is null) throw new ArgumentNullException(nameof(values));
            if (values.Length != _columns.Length)
                throw new ArgumentException($"Row has {values.Length} values but the table has {_columns.Length} columns.", nameof(values));
            var copy = new object?[values.Length];
            Array.Copy(values, copy, values.Length);
            _rows.Add(copy);
            return this;
        }

        /// <summary>Appends a row from a sequence; its length must equal <see cref="ColumnCount"/>.</summary>
        public DataTableLite AddRow(IEnumerable<object?> values)
        {
            if (values is null) throw new ArgumentNullException(nameof(values));
            // At most one cell past the column count is read: copying the whole sequence never ended for an infinite one.
            var list = new List<object?>(ColumnCount + 1);
            foreach (var value in values)
            {
                list.Add(value);
                if (list.Count > ColumnCount) break;
            }
            return AddRow(list.ToArray());
        }

        /// <summary>The cell at (row, column ordinal).</summary>
        public object? this[int row, int column]
        {
            get
            {
                var cells = RowAt(row);
                if (column < 0 || column >= cells.Length)
                    throw new ArgumentOutOfRangeException(nameof(column), column, "Column ordinal is out of range.");
                return cells[column];
            }
        }

        /// <summary>The cell at (row, column name).</summary>
        public object? this[int row, string column]
        {
            get
            {
                int c = IndexOf(column);
                if (c < 0) throw new ArgumentException($"No column named '{column}'.", nameof(column));
                return RowAt(row)[c];
            }
        }

        /// <summary>Returns a copy of a row's cells.</summary>
        public object?[] GetRow(int row)
        {
            var src = RowAt(row);
            var copy = new object?[src.Length];
            Array.Copy(src, copy, src.Length);
            return copy;
        }

        /// <summary>Projects a subset of columns in the given order (a subset and/or reorder; names must be distinct).</summary>
        public DataTableLite Select(params string[] columns)
        {
            if (columns is null) throw new ArgumentNullException(nameof(columns));
            if (columns.Length == 0) throw new ArgumentException("Select needs at least one column.", nameof(columns));
            var ordinals = new int[columns.Length];
            for (int i = 0; i < columns.Length; i++)
            {
                if (columns[i] is null) throw new ArgumentException($"Column name at index {i} is null.", nameof(columns));
                int c = IndexOf(columns[i]);
                if (c < 0) throw new ArgumentException($"No column named '{columns[i]}'.", nameof(columns));
                ordinals[i] = c;
            }
            var result = new DataTableLite(columns);
            foreach (var row in _rows)
            {
                var projected = new object?[ordinals.Length];
                for (int i = 0; i < ordinals.Length; i++) projected[i] = row[ordinals[i]];
                result._rows.Add(projected);
            }
            return result;
        }

        /// <summary>Returns a table of the rows matching <paramref name="predicate"/> (original order preserved).</summary>
        public DataTableLite Where(Func<Row, bool> predicate)
        {
            if (predicate is null) throw new ArgumentNullException(nameof(predicate));
            var result = CloneSchemaEmpty();
            foreach (var row in _rows)
                if (predicate(new Row(row, _index)))
                    result._rows.Add((object?[])row.Clone());
            return result;
        }

        /// <summary>Returns the rows sorted ascending by one column. Stable. Null cells sort first, also with a
        /// <paramref name="comparer"/>, which sees only non-null cells. Strings compare ordinally by default.</summary>
        public DataTableLite OrderBy(string column, IComparer<object?>? comparer = null)
            => Sort(column, comparer, descending: false);

        /// <summary>Returns the rows sorted descending by one column. Stable. Null cells sort last, also with a
        /// <paramref name="comparer"/>, which sees only non-null cells. Strings compare ordinally by default.</summary>
        public DataTableLite OrderByDescending(string column, IComparer<object?>? comparer = null)
            => Sort(column, comparer, descending: true);

        private DataTableLite Sort(string column, IComparer<object?>? comparer, bool descending)
        {
            int c = IndexOf(column);
            if (c < 0) throw new ArgumentException($"No column named '{column}'.", nameof(column));
            // A caller's comparer was handed null cells, so one that didn't expect them threw, despite "nulls sort first".
            comparer = comparer is null ? CellComparer.Instance : new NullsFirst(comparer);

            var order = new int[_rows.Count];
            for (int i = 0; i < order.Length; i++) order[i] = i;
            // Swap the operands for descending: negating would leave int.MinValue unchanged.
            MergeSort(order, descending
                ? (x, y) => comparer.Compare(_rows[y][c], _rows[x][c])
                : (x, y) => comparer.Compare(_rows[x][c], _rows[y][c]));

            var result = CloneSchemaEmpty();
            foreach (int i in order) result._rows.Add((object?[])_rows[i].Clone());
            return result;
        }

        // A bottom-up merge sort of row indices: stable, and a comparer's exception propagates unchanged (Array.Sort wrapped
        // it in an InvalidOperationException, or turned an IndexOutOfRangeException into an ArgumentException without it).
        private static void MergeSort(int[] a, Comparison<int> compare)
        {
            int n = a.Length;
            int[] src = a, dst = new int[n];
            for (long width = 1; width < n; width *= 2)
            {
                for (long lo = 0; lo < n; lo += 2 * width)
                {
                    int mid = (int)Math.Min(lo + width, n), hi = (int)Math.Min(lo + 2 * width, n);
                    int i = (int)lo, j = mid, k = (int)lo;
                    while (i < mid && j < hi) dst[k++] = compare(src[j], src[i]) < 0 ? src[j++] : src[i++];
                    while (i < mid) dst[k++] = src[i++];
                    while (j < hi) dst[k++] = src[j++];
                }
                (src, dst) = (dst, src);
            }
            if (!ReferenceEquals(src, a)) Array.Copy(src, a, n);
        }

        /// <summary>
        /// Returns the table with duplicate rows removed (full-row structural equality), first kept. Cells compare
        /// with <see cref="object.Equals(object)"/> on the boxed values, so <c>1</c> (int) and <c>1L</c> (long) differ.
        /// </summary>
        public DataTableLite Distinct()
        {
            var result = CloneSchemaEmpty();
            var seen = new HashSet<object?[]>(RowEqualityComparer.Instance);
            foreach (var row in _rows)
                if (seen.Add(row))
                    result._rows.Add((object?[])row.Clone());
            return result;
        }

        /// <summary>
        /// Groups rows by the distinct values of one column, preserving first-seen key order. Rows whose key
        /// cell is null form their own group with a null key. Keys compare as boxed values (<c>1</c> and <c>1L</c> differ).
        /// </summary>
        public IReadOnlyList<DataGroup> GroupBy(string column)
        {
            int c = IndexOf(column);
            if (c < 0) throw new ArgumentException($"No column named '{column}'.", nameof(column));

            var order = new List<object?>();
            var groups = new Dictionary<object, DataTableLite>(CellEquality.Instance);
            DataTableLite? nullGroup = null;

            foreach (var row in _rows)
            {
                object? key = row[c];
                DataTableLite target;
                if (key is null)
                {
                    if (nullGroup is null) { nullGroup = CloneSchemaEmpty(); order.Add(null); }
                    target = nullGroup;
                }
                else if (!groups.TryGetValue(key, out target!))
                {
                    target = CloneSchemaEmpty();
                    groups[key] = target;
                    order.Add(key);
                }
                target._rows.Add((object?[])row.Clone());
            }

            var result = new List<DataGroup>(order.Count);
            foreach (object? key in order)
                result.Add(new DataGroup(key, key is null ? nullGroup! : groups[key]));
            return result;
        }

        /// <summary>
        /// Inner-joins this table (left) with <paramref name="other"/> (right) on equal values of
        /// <paramref name="leftColumn"/> and <paramref name="rightColumn"/>. The result has the left columns
        /// followed by the right columns; if a right column name collides with a left one, pass
        /// <paramref name="rightPrefix"/> to disambiguate. Null keys never match (SQL-like). Keys compare as boxed
        /// values, so an int key never matches a long key of the same number.
        /// </summary>
        public DataTableLite InnerJoin(DataTableLite other, string leftColumn, string rightColumn, string? rightPrefix = null)
        {
            if (other is null) throw new ArgumentNullException(nameof(other));
            int lc = IndexOf(leftColumn);
            if (lc < 0) throw new ArgumentException($"No column named '{leftColumn}'.", nameof(leftColumn));
            int rc = other.IndexOf(rightColumn);
            if (rc < 0) throw new ArgumentException($"No column named '{rightColumn}'.", nameof(rightColumn));

            var resultColumns = new List<string>(_columns);
            // A set, not List.Contains: the check was quadratic (3 s for 20k + 20k columns).
            var taken = new HashSet<string>(StringComparer.Ordinal);
            foreach (string rcol in other._columns)
            {
                string name = rightPrefix is null ? rcol : rightPrefix + rcol;
                if (_index.ContainsKey(name) || !taken.Add(name))
                    throw new ArgumentException(
                        $"Joined column name '{name}' collides with a left column; pass a rightPrefix.", nameof(rightPrefix));
                resultColumns.Add(name);
            }

            // Index the right side by join key (skip null keys — they never match).
            var rightByKey = new Dictionary<object, List<object?[]>>(CellEquality.Instance);
            foreach (var rrow in other._rows)
            {
                object? key = rrow[rc];
                if (key is null) continue;
                if (!rightByKey.TryGetValue(key, out var bucket)) { bucket = new List<object?[]>(); rightByKey[key] = bucket; }
                bucket.Add(rrow);
            }

            var result = new DataTableLite(resultColumns);
            foreach (var lrow in _rows)
            {
                object? key = lrow[lc];
                if (key is null || !rightByKey.TryGetValue(key, out var matches)) continue;
                foreach (var rrow in matches)
                {
                    var combined = new object?[_columns.Length + other._columns.Length];
                    Array.Copy(lrow, 0, combined, 0, _columns.Length);
                    Array.Copy(rrow, 0, combined, _columns.Length, other._columns.Length);
                    result._rows.Add(combined);
                }
            }
            return result;
        }

        /// <summary>Renders the table as aligned text (for debugging / console output), formatting cells with the invariant culture.</summary>
        public override string ToString()
        {
            var widths = new int[_columns.Length];
            for (int i = 0; i < _columns.Length; i++) widths[i] = _columns[i].Length;
            string[][] cells = new string[_rows.Count][];
            for (int r = 0; r < _rows.Count; r++)
            {
                cells[r] = new string[_columns.Length];
                for (int c = 0; c < _columns.Length; c++)
                {
                    object? cell = _rows[r][c];
                    // "R": .NET Framework's default wrote 15 digits, so 0.30000000000000004 showed as 0.3.
                    string s = cell is double d ? d.ToString("R", CultureInfo.InvariantCulture)
                        : cell is float f ? f.ToString("R", CultureInfo.InvariantCulture)
                        : Convert.ToString(cell, CultureInfo.InvariantCulture) ?? "";
                    cells[r][c] = s;
                    if (s.Length > widths[c]) widths[c] = s.Length;
                }
            }

            var sb = new StringBuilder();
            AppendRow(sb, _columns, widths);
            for (int r = 0; r < cells.Length; r++) AppendRow(sb, cells[r], widths);
            return sb.ToString();
        }

        private static void AppendRow(StringBuilder sb, IReadOnlyList<string> cells, int[] widths)
        {
            for (int c = 0; c < cells.Count; c++)
            {
                if (c > 0) sb.Append("  ");
                sb.Append(cells[c].PadRight(widths[c]));
            }
            sb.Append('\n');
        }

        // A row by index, naming row (the list's own exception named "index").
        private object?[] RowAt(int row)
        {
            if (row < 0 || row >= _rows.Count)
                throw new ArgumentOutOfRangeException(nameof(row), row, "Row index is out of range.");
            return _rows[row];
        }

        private DataTableLite CloneSchemaEmpty()
            => new DataTableLite((string[])_columns.Clone(), _index, new List<object?[]>());

        /// <summary>A read-only view of one row, addressable by column name or ordinal, passed to <see cref="Where"/>.</summary>
        public readonly struct Row
        {
            private readonly object?[] _cells;
            private readonly Dictionary<string, int> _index;

            internal Row(object?[] cells, Dictionary<string, int> index)
            {
                _cells = cells;
                _index = index;
            }

            /// <summary>Cell by ordinal.</summary>
            public object? this[int column]
            {
                get
                {
                    // default(Row) has no cells (its fields are null, which threw NullReferenceException).
                    if (column < 0 || column >= Count)
                        throw new ArgumentOutOfRangeException(nameof(column), column, "Column ordinal is out of range.");
                    return _cells[column];
                }
            }

            /// <summary>Cell by column name.</summary>
            public object? this[string column]
            {
                get
                {
                    if (column is null) throw new ArgumentNullException(nameof(column));
                    if (_index is null || !_index.TryGetValue(column, out int c)) throw new ArgumentException($"No column named '{column}'.", nameof(column));
                    return _cells[c];
                }
            }

            /// <summary>Number of cells.</summary>
            public int Count => _cells?.Length ?? 0;
        }

        /// <summary>One group produced by <see cref="GroupBy(string)"/>: the key value and its rows.</summary>
        public sealed class DataGroup
        {
            internal DataGroup(object? key, DataTableLite rows)
            {
                Key = key;
                Rows = rows;
            }

            /// <summary>The distinct key value for this group (may be null).</summary>
            public object? Key { get; }

            /// <summary>The rows sharing this key, as a table with the same columns.</summary>
            public DataTableLite Rows { get; }
        }

        // Orders cells with null first, strings ordinally (Comparer<object>.Default compared them in the culture current
        // when it was first used, and cached it), then via Comparer<object>.Default (IComparable). Mixed incomparable
        // types throw from the default comparer — pass an explicit comparer for those columns.
        private sealed class CellComparer : IComparer<object?>
        {
            public static readonly CellComparer Instance = new CellComparer();
            public int Compare(object? x, object? y)
            {
                if (x is null) return y is null ? 0 : -1;
                if (y is null) return 1;
                if (x is string sx && y is string sy) return string.CompareOrdinal(sx, sy);
                return Comparer<object>.Default.Compare(x, y);
            }
        }

        // A caller's comparer with null cells placed first, so it sees only non-null ones.
        private sealed class NullsFirst : IComparer<object?>
        {
            private readonly IComparer<object?> _inner;
            public NullsFirst(IComparer<object?> inner) => _inner = inner;
            public int Compare(object? x, object? y)
            {
                if (x is null) return y is null ? 0 : -1;
                if (y is null) return 1;
                return _inner.Compare(x, y);
            }
        }

        // Full-row structural equality for Distinct: element-wise object.Equals, order-sensitive.
        private sealed class RowEqualityComparer : IEqualityComparer<object?[]>
        {
            public static readonly RowEqualityComparer Instance = new RowEqualityComparer();

            public bool Equals(object?[]? a, object?[]? b)
            {
                if (ReferenceEquals(a, b)) return true;
                if (a is null || b is null || a.Length != b.Length) return false;
                for (int i = 0; i < a.Length; i++)
                    if (!Equals(a[i], b[i])) return false;
                return true;
            }

            private static new bool Equals(object? a, object? b) => a is null ? b is null : a.Equals(b);

            public int GetHashCode(object?[] row)
            {
                // Murmur3's element step and finalizer: hash*31 + h let crafted rows collide by construction (40,000
                // rows of (i, -31i) took 8 s in Distinct).
                unchecked
                {
                    uint hash = 0x9747B28C;
                    foreach (object? cell in row)
                    {
                        uint k = (uint)CellEquality.Hash(cell) * 0xCC9E2D51;
                        k = (k << 15) | (k >> 17);
                        hash ^= k * 0x1B873593;
                        hash = (hash << 13) | (hash >> 19);
                        hash = hash * 5 + 0xE6546B64;
                    }
                    hash ^= (uint)row.Length;
                    hash ^= hash >> 16; hash *= 0x85EBCA6B;
                    hash ^= hash >> 13; hash *= 0xC2B2AE35;
                    return (int)(hash ^ (hash >> 16));
                }
            }
        }

        // Cells compare with Equals, and hash by value: .NET Framework hashes a NaN by its bits and a decimal by its
        // scale, so NaNs with other payloads, or 1.0m and 1.00m, fell into separate groups and rows.
        private sealed class CellEquality : IEqualityComparer<object>
        {
            public static readonly CellEquality Instance = new CellEquality();

            public new bool Equals(object? a, object? b) => a is null ? b is null : a.Equals(b);

            public int GetHashCode(object cell) => Hash(cell);

            public static int Hash(object? cell)
            {
                switch (cell)
                {
                    case null: return 0;
                    case double d: return double.IsNaN(d) ? -1 : d.GetHashCode();
                    case float f: return float.IsNaN(f) ? -1 : f.GetHashCode();
                    case decimal m:
                        // Not through double, which can round equal decimals with different scales apart. Dividing by
                        // one drops trailing zeros, so equal nonzero decimals have one representation.
                        if (m == 0) return 0;   // 0m and -0m
                        int[] b = decimal.GetBits(m / 1.000000000000000000000000000000000m);
                        return unchecked(b[0] ^ (b[1] * 31) ^ (b[2] * 961) ^ b[3]);
                    default: return cell.GetHashCode();
                }
            }
        }
    }
}
