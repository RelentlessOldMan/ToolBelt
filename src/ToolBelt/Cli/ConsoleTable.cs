// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;
using System.Text;

namespace ToolBelt.Cli
{
    /// <summary>Per-column text alignment for <see cref="ConsoleTable"/>.</summary>
    public enum ColumnAlignment
    {
        Left,
        Right,
    }

    /// <summary>
    /// Builds a simple aligned monospace table for console output: a header row, a dashed divider, and
    /// one line per data row, with every column padded to the width of its widest cell. All rendered
    /// lines have identical length. Cell values are used verbatim (a null cell renders as empty).
    /// </summary>
    public sealed class ConsoleTable
    {
        private readonly string[] _headers;
        private readonly ColumnAlignment[] _alignments;
        private readonly List<string[]> _rows = new List<string[]>();

        private const string ColumnSeparator = " | ";
        private const string DividerJoin = "-+-";

        public ConsoleTable(params string[] headers)
        {
            if (headers is null)
                throw new ArgumentNullException(nameof(headers));
            if (headers.Length == 0)
                throw new ArgumentException("At least one column is required.", nameof(headers));

            _headers = (string[])headers.Clone();
            _alignments = new ColumnAlignment[headers.Length]; // defaults to Left
        }

        public int ColumnCount => _headers.Length;

        public int RowCount => _rows.Count;

        /// <summary>Appends a row. The cell count must match the column count.</summary>
        public ConsoleTable AddRow(params string[] cells)
        {
            if (cells is null)
                throw new ArgumentNullException(nameof(cells));
            if (cells.Length != _headers.Length)
                throw new ArgumentException($"Expected {_headers.Length} cells, got {cells.Length}.", nameof(cells));

            var copy = new string[cells.Length];
            for (int i = 0; i < cells.Length; i++)
                copy[i] = cells[i] ?? string.Empty;
            _rows.Add(copy);
            return this;
        }

        /// <summary>Sets the alignment of a single column.</summary>
        public ConsoleTable AlignColumn(int index, ColumnAlignment alignment)
        {
            if ((uint)index >= (uint)_alignments.Length)
                throw new ArgumentOutOfRangeException(nameof(index), index, $"Index must be in [0, {_alignments.Length}).");
            _alignments[index] = alignment;
            return this;
        }

        /// <summary>Renders the table as a list of lines (header, divider, rows).</summary>
        public IReadOnlyList<string> Render()
        {
            var widths = new int[_headers.Length];
            for (int i = 0; i < _headers.Length; i++)
                widths[i] = _headers[i].Length;
            foreach (var row in _rows)
                for (int i = 0; i < row.Length; i++)
                    if (row[i].Length > widths[i])
                        widths[i] = row[i].Length;

            var lines = new List<string>(_rows.Count + 2);
            lines.Add(RenderRow(_headers, widths));
            lines.Add(RenderDivider(widths));
            foreach (var row in _rows)
                lines.Add(RenderRow(row, widths));
            return lines;
        }

        public override string ToString() => string.Join(Environment.NewLine, Render());

        private string RenderRow(string[] cells, int[] widths)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < cells.Length; i++)
            {
                if (i > 0)
                    sb.Append(ColumnSeparator);
                string cell = cells[i];
                sb.Append(_alignments[i] == ColumnAlignment.Right
                    ? cell.PadLeft(widths[i])
                    : cell.PadRight(widths[i]));
            }
            return sb.ToString();
        }

        private static string RenderDivider(int[] widths)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < widths.Length; i++)
            {
                if (i > 0)
                    sb.Append(DividerJoin);
                sb.Append(new string('-', widths[i]));
            }
            return sb.ToString();
        }
    }
}
