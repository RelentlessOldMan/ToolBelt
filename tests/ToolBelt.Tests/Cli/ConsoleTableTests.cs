using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ToolBelt.Cli;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Cli
{
    public sealed class ConsoleTableTests
    {
        public void BasicLayout()
        {
            var table = new ConsoleTable("Name", "Age");
            table.AddRow("Alice", "30");
            table.AddRow("Bob", "5");

            var lines = table.Render();
            Check.True(lines.SequenceEqual(new[]
            {
                "Name  | Age",
                "------+----",
                "Alice | 30 ",
                "Bob   | 5  ",
            }), "rendered:\n" + string.Join("\n", lines));
        }

        public void RightAlignment()
        {
            var table = new ConsoleTable("Item", "Qty");
            table.AlignColumn(1, ColumnAlignment.Right);
            table.AddRow("apples", "3");
            table.AddRow("pears", "12");

            var lines = table.Render();
            // Qty column right-aligned to width 3.
            Check.Equal("apples |   3", lines[2]);
            Check.Equal("pears  |  12", lines[3]);
        }

        public void HeaderOnly_StillRendersHeaderAndDivider()
        {
            var lines = new ConsoleTable("A", "B").Render();
            Check.Equal(2, lines.Count);
            Check.Equal("A | B", lines[0]);
            Check.Equal("--+--", lines[1]);
        }

        public void NullCell_RendersEmpty()
        {
            var table = new ConsoleTable("X");
            table.AddRow(new string[] { null! });
            Check.Equal("X", table.Render()[0]);
            Check.Equal(" ", table.Render()[2]); // width 1, empty cell padded
        }

        public void WrongCellCount_Throws()
        {
            var table = new ConsoleTable("A", "B");
            Check.Throws<ArgumentException>(() => table.AddRow("only-one"));
        }

        public void NoColumns_Throws()
        {
            Check.Throws<ArgumentException>(() => new ConsoleTable());
        }

        // Property: every rendered line has the same length regardless of content.
        public void AllLinesEqualLength_OverRandomTables()
        {
            var rng = new Random(70707);
            for (int trial = 0; trial < 500; trial++)
            {
                int cols = rng.Next(1, 6);
                var headers = new string[cols];
                for (int c = 0; c < cols; c++)
                    headers[c] = RandomCell(rng);
                var table = new ConsoleTable(headers);

                int rows = rng.Next(0, 8);
                for (int r = 0; r < rows; r++)
                {
                    var cells = new string[cols];
                    for (int c = 0; c < cols; c++)
                        cells[c] = RandomCell(rng);
                    table.AddRow(cells);
                }

                var lines = table.Render();
                int expected = lines[0].Length;
                foreach (var line in lines)
                    Check.Equal(expected, line.Length, $"trial {trial}: line '{line}'");
            }
        }

        private static string RandomCell(Random rng)
        {
            int len = rng.Next(0, 8);
            var sb = new StringBuilder(len);
            for (int i = 0; i < len; i++)
                sb.Append((char)('a' + rng.Next(0, 26)));
            return sb.ToString();
        }
    }
}
