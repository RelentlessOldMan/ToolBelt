// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;
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
            _index = index;
            _rows = new List<object?[]>();
        }

        private DataTableLite(string[] columns, Dictionary<string, int> index, List<object?[]> rows)
        {
            _columns = columns;
            _index = index;
            _rows = rows;
        }

        /// <summary>The column names in order.</summary>
        public IReadOnlyList<string> Columns => _columns;

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
            var list = new List<object?>(values);
            return AddRow(list.ToArray());
        }

        /// <summary>The cell at (row, column ordinal).</summary>
        public object? this[int row, int column] => _rows[row][column];

        /// <summary>The cell at (row, column name).</summary>
        public object? this[int row, string column]
        {
            get
            {
                int c = IndexOf(column);
                if (c < 0) throw new ArgumentException($"No column named '{column}'.", nameof(column));
                return _rows[row][c];
            }
        }

        /// <summary>Returns a copy of a row's cells.</summary>
        public object?[] GetRow(int row)
        {
            var src = _rows[row];
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

        /// <summary>Returns the rows sorted ascending by one column. Stable. Null cells sort first.</summary>
        public DataTableLite OrderBy(string column, IComparer<object?>? comparer = null)
            => Sort(column, comparer, descending: false);

        /// <summary>Returns the rows sorted descending by one column. Stable. Null cells sort last.</summary>
        public DataTableLite OrderByDescending(string column, IComparer<object?>? comparer = null)
            => Sort(column, comparer, descending: true);

        private DataTableLite Sort(string column, IComparer<object?>? comparer, bool descending)
        {
            int c = IndexOf(column);
            if (c < 0) throw new ArgumentException($"No column named '{column}'.", nameof(column));
            comparer ??= CellComparer.Instance;

            // Decorate with the original index so the sort is stable (List.Sort is not).
            var order = new int[_rows.Count];
            for (int i = 0; i < order.Length; i++) order[i] = i;
            Array.Sort(order, (x, y) =>
            {
                int cmp = comparer.Compare(_rows[x][c], _rows[y][c]);
                if (descending) cmp = -cmp;
                return cmp != 0 ? cmp : x.CompareTo(y); // stable tie-break on original position
            });

            var result = CloneSchemaEmpty();
            foreach (int i in order) result._rows.Add((object?[])_rows[i].Clone());
            return result;
        }

        /// <summary>Returns the table with duplicate rows removed (full-row structural equality), first kept.</summary>
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
        /// cell is null form their own group with a null key.
        /// </summary>
        public IReadOnlyList<DataGroup> GroupBy(string column)
        {
            int c = IndexOf(column);
            if (c < 0) throw new ArgumentException($"No column named '{column}'.", nameof(column));

            var order = new List<object?>();
            var groups = new Dictionary<object, DataTableLite>();
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
        /// <paramref name="rightPrefix"/> to disambiguate. Null keys never match (SQL-like).
        /// </summary>
        public DataTableLite InnerJoin(DataTableLite other, string leftColumn, string rightColumn, string? rightPrefix = null)
        {
            if (other is null) throw new ArgumentNullException(nameof(other));
            int lc = IndexOf(leftColumn);
            if (lc < 0) throw new ArgumentException($"No column named '{leftColumn}'.", nameof(leftColumn));
            int rc = other.IndexOf(rightColumn);
            if (rc < 0) throw new ArgumentException($"No column named '{rightColumn}'.", nameof(rightColumn));

            var resultColumns = new List<string>(_columns);
            foreach (string rcol in other._columns)
            {
                string name = rightPrefix is null ? rcol : rightPrefix + rcol;
                if (_index.ContainsKey(name) || resultColumns.Contains(name))
                    throw new ArgumentException(
                        $"Joined column name '{name}' collides with a left column; pass a rightPrefix.", nameof(other));
                resultColumns.Add(name);
            }

            // Index the right side by join key (skip null keys — they never match).
            var rightByKey = new Dictionary<object, List<object?[]>>();
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

        /// <summary>Renders the table as aligned text (for debugging / console output).</summary>
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
                    string s = _rows[r][c]?.ToString() ?? "";
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
            public object? this[int column] => _cells[column];

            /// <summary>Cell by column name.</summary>
            public object? this[string column]
            {
                get
                {
                    if (column is null) throw new ArgumentNullException(nameof(column));
                    if (!_index.TryGetValue(column, out int c)) throw new ArgumentException($"No column named '{column}'.", nameof(column));
                    return _cells[c];
                }
            }

            /// <summary>Number of cells.</summary>
            public int Count => _cells.Length;
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

        // Orders cells with null first, then via Comparer<object>.Default (IComparable). Mixed incomparable
        // types throw from the default comparer — pass an explicit comparer for those columns.
        private sealed class CellComparer : IComparer<object?>
        {
            public static readonly CellComparer Instance = new CellComparer();
            public int Compare(object? x, object? y)
            {
                if (x is null) return y is null ? 0 : -1;
                if (y is null) return 1;
                return Comparer<object>.Default.Compare(x, y);
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
                unchecked
                {
                    int hash = 17;
                    foreach (object? cell in row)
                        hash = hash * 31 + (cell?.GetHashCode() ?? 0);
                    return hash;
                }
            }
        }
    }
}
