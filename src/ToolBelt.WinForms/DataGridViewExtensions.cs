// ToolBelt.WinForms drop-in — Windows-only (net8.0-windows), self-contained (BCL + WinForms).
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

namespace ToolBelt.WinForms
{
    /// <summary>
    /// Everyday <see cref="DataGridView"/> chores: bind a typed sequence with readable headers, auto-size columns with a
    /// width cap, copy the selection as tab-separated text that pastes cleanly into Excel, and export to CSV. Text comes
    /// from each cell's <c>FormattedValue</c>, i.e. exactly what the grid shows (its format strings and culture).
    /// </summary>
    public static class DataGridViewExtensions
    {
        /// <summary>
        /// Binds <paramref name="items"/> (copied into a <see cref="BindingList{T}"/>, returned so you can add/remove
        /// rows that the grid then shows) with auto-generated columns; headers use <see cref="DisplayNameAttribute"/>
        /// where present and properties marked <c>[Browsable(false)]</c> are hidden. The grid needs a binding context —
        /// any grid on a form has one.
        /// </summary>
        public static BindingList<T> BindList<T>(this DataGridView grid, IEnumerable<T> items)
        {
            if (grid is null) throw new ArgumentNullException(nameof(grid));
            if (items is null) throw new ArgumentNullException(nameof(items));
            var list = new BindingList<T>(items.ToList());
            grid.AutoGenerateColumns = true;
            grid.DataSource = list;
            foreach (PropertyDescriptor p in TypeDescriptor.GetProperties(typeof(T)))
            {
                DataGridViewColumn? column = grid.Columns.Cast<DataGridViewColumn>().FirstOrDefault(c => c.DataPropertyName == p.Name);
                if (column is null) continue;
                if (!string.IsNullOrEmpty(p.DisplayName) && p.DisplayName != p.Name) column.HeaderText = p.DisplayName;
            }
            return list;
        }

        /// <summary>Sizes every column to its content, then caps each at <paramref name="maxWidth"/> pixels if given.</summary>
        public static void AutoSizeColumns(this DataGridView grid, int? maxWidth = null,
            DataGridViewAutoSizeColumnsMode mode = DataGridViewAutoSizeColumnsMode.AllCells)
        {
            if (grid is null) throw new ArgumentNullException(nameof(grid));
            if (maxWidth.HasValue && maxWidth.Value < 1) throw new ArgumentOutOfRangeException(nameof(maxWidth), maxWidth, "Must be positive.");
            grid.AutoResizeColumns(mode);
            if (maxWidth is int cap)
                foreach (DataGridViewColumn c in grid.Columns)
                    if (c.Width > cap) c.Width = cap;
        }

        /// <summary>
        /// The selected cells as tab-separated rows covering the selection's bounding rectangle (unselected cells inside
        /// it are blank), visible columns in display order, optionally preceded by their headers. Cells holding a tab,
        /// newline or quote are quoted the way Excel expects. Empty when nothing is selected.
        /// </summary>
        public static string SelectionToTsv(this DataGridView grid, bool includeHeaders = false)
        {
            if (grid is null) throw new ArgumentNullException(nameof(grid));
            var selected = grid.SelectedCells.Cast<DataGridViewCell>()
                .Where(c => c.OwningColumn.Visible && !c.OwningRow.IsNewRow)
                .ToList();
            if (selected.Count == 0) return string.Empty;

            var columns = selected.Select(c => c.OwningColumn).Distinct().OrderBy(c => c.DisplayIndex).ToList();
            int firstCol = columns.First().DisplayIndex, lastCol = columns.Last().DisplayIndex;
            var span = grid.Columns.Cast<DataGridViewColumn>()
                .Where(c => c.Visible && c.DisplayIndex >= firstCol && c.DisplayIndex <= lastCol)
                .OrderBy(c => c.DisplayIndex).ToList();
            int firstRow = selected.Min(c => c.RowIndex), lastRow = selected.Max(c => c.RowIndex);
            var chosen = new HashSet<DataGridViewCell>(selected);

            var sb = new StringBuilder();
            if (includeHeaders)
                sb.Append(string.Join("\t", span.Select(c => TsvField(c.HeaderText)))).Append("\r\n");
            for (int r = firstRow; r <= lastRow; r++)
            {
                DataGridViewRow row = grid.Rows[r];
                if (row.IsNewRow || !row.Visible) continue;
                sb.Append(string.Join("\t", span.Select(c =>
                {
                    DataGridViewCell cell = row.Cells[c.Index];
                    return chosen.Contains(cell) ? TsvField(Text(cell)) : string.Empty;
                }))).Append("\r\n");
            }
            return sb.ToString();
        }

        /// <summary>Copies <see cref="SelectionToTsv"/> to the clipboard (call on the UI thread). Returns false if nothing is selected.</summary>
        public static bool CopySelectionToClipboard(this DataGridView grid, bool includeHeaders = false)
        {
            string tsv = grid.SelectionToTsv(includeHeaders);
            if (tsv.Length == 0) return false;
            Clipboard.SetText(tsv);
            return true;
        }

        /// <summary>
        /// Writes the grid as RFC 4180 CSV: an optional header row, then every row (the new-row placeholder excluded),
        /// visible columns only in display order unless <paramref name="visibleColumnsOnly"/> is false. Fields holding the
        /// delimiter, a quote or a line break are quoted, with quotes doubled. Lines end in CRLF. Values are the cells'
        /// formatted text, which means each row is unshared (WinForms' memory optimisation for big unbound grids) as it is
        /// read; for 100k+ rows, export from the bound data source instead.
        /// </summary>
        public static void ToCsv(this DataGridView grid, TextWriter writer, bool includeHeaders = true, bool visibleColumnsOnly = true, char delimiter = ',')
        {
            if (grid is null) throw new ArgumentNullException(nameof(grid));
            if (writer is null) throw new ArgumentNullException(nameof(writer));
            if (delimiter == '"' || delimiter == '\r' || delimiter == '\n') throw new ArgumentException("Invalid delimiter.", nameof(delimiter));
            var columns = grid.Columns.Cast<DataGridViewColumn>()
                .Where(c => !visibleColumnsOnly || c.Visible)
                .OrderBy(c => c.DisplayIndex).ToList();
            string sep = delimiter.ToString();
            if (includeHeaders)
                writer.Write(string.Join(sep, columns.Select(c => CsvField(c.HeaderText, delimiter))) + "\r\n");
            foreach (DataGridViewRow row in grid.Rows)
            {
                if (row.IsNewRow) continue;
                writer.Write(string.Join(sep, columns.Select(c => CsvField(Text(row.Cells[c.Index]), delimiter))) + "\r\n");
            }
        }

        /// <summary>The grid as a CSV string. See <see cref="ToCsv(DataGridView, TextWriter, bool, bool, char)"/>.</summary>
        public static string ToCsv(this DataGridView grid, bool includeHeaders = true, bool visibleColumnsOnly = true, char delimiter = ',')
        {
            using var sw = new StringWriter();
            grid.ToCsv(sw, includeHeaders, visibleColumnsOnly, delimiter);
            return sw.ToString();
        }

        /// <summary>Saves the grid as CSV; UTF-8 with a byte-order mark by default, so Excel detects the encoding.</summary>
        public static void SaveCsv(this DataGridView grid, string path, Encoding? encoding = null, bool includeHeaders = true, char delimiter = ',')
        {
            if (path is null) throw new ArgumentNullException(nameof(path));
            using var writer = new StreamWriter(path, false, encoding ?? new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            grid.ToCsv(writer, includeHeaders, true, delimiter);
        }

        private static string Text(DataGridViewCell cell) => cell.FormattedValue?.ToString() ?? string.Empty;

        private static string CsvField(string value, char delimiter)
        {
            bool quote = value.IndexOf(delimiter) >= 0 || value.IndexOf('"') >= 0 || value.IndexOf('\r') >= 0 || value.IndexOf('\n') >= 0
                || (value.Length > 0 && (char.IsWhiteSpace(value[0]) || char.IsWhiteSpace(value[value.Length - 1])));
            return quote ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
        }

        private static string TsvField(string value)
            => value.IndexOf('\t') >= 0 || value.IndexOf('\n') >= 0 || value.IndexOf('\r') >= 0 || value.IndexOf('"') >= 0
                ? "\"" + value.Replace("\"", "\"\"") + "\""
                : value;
    }
}
