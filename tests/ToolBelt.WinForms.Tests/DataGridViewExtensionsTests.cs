using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using ToolBelt.Tests.Framework;
using ToolBelt.WinForms;

namespace ToolBelt.WinForms.Tests
{
    public sealed class DataGridViewExtensionsTests
    {
        private sealed class Reading
        {
            public Reading(string sensor, double value, string note) { Sensor = sensor; Value = value; Note = note; }
            public string Sensor { get; }
            [DisplayName("Value (V)")] public double Value { get; }
            public string Note { get; }
            [Browsable(false)] public string Secret => "hidden";
        }

        private static DataGridView Grid()
        {
            var grid = new DataGridView { BindingContext = new BindingContext(), AllowUserToAddRows = false };
            grid.BindList(new[]
            {
                new Reading("A", 1.5, "ok"),
                new Reading("B", 2, "has, comma"),
                new Reading("C", 3.25, "say \"hi\"\tthen tab"),
            });
            return grid;
        }

        public void BindList_GeneratesColumnsWithDisplayNames()
        {
            RunSta(() =>
            {
                using DataGridView grid = Grid();
                Check.Equal(3, grid.Rows.Count);
                Check.Equal("Sensor|Value (V)|Note", string.Join("|", grid.Columns.Cast<DataGridViewColumn>().Select(c => c.HeaderText)));
                Check.False(grid.Columns.Cast<DataGridViewColumn>().Any(c => c.DataPropertyName == "Secret"));
            });
        }

        public void BindList_ReturnsALiveList()
        {
            RunSta(() =>
            {
                var grid = new DataGridView { BindingContext = new BindingContext(), AllowUserToAddRows = false };
                BindingList<Reading> list = grid.BindList(new[] { new Reading("A", 1, "") });
                list.Add(new Reading("B", 2, ""));
                Check.Equal(2, grid.Rows.Count);
                grid.Dispose();
            });
        }

        public void ToCsv_QuotesPerRfc4180()
        {
            RunSta(() =>
            {
                using DataGridView grid = Grid();
                string csv = grid.ToCsv();
                string[] lines = csv.Split(new[] { "\r\n" }, StringSplitOptions.None);
                Check.Equal("Sensor,Value (V),Note", lines[0]);
                Check.Equal("A,1.5,ok", lines[1]);
                Check.Equal("B,2,\"has, comma\"", lines[2]);
                Check.Equal("C,3.25,\"say \"\"hi\"\"\tthen tab\"", lines[3]);
                Check.Equal("", lines[4]);                                       // trailing CRLF
                Check.Equal("A;1.5;ok", grid.ToCsv(includeHeaders: false, delimiter: ';').Split('\r')[0]);
            });
        }

        public void ToCsv_HonoursColumnVisibilityAndOrder()
        {
            RunSta(() =>
            {
                using DataGridView grid = Grid();
                grid.Columns[1].Visible = false;
                grid.Columns[2].DisplayIndex = 0;
                Check.Equal("Note,Sensor", grid.ToCsv().Split('\r')[0]);
                Check.Equal("Note,Sensor,Value (V)", grid.ToCsv(visibleColumnsOnly: false).Split('\r')[0]);
            });
        }

        public void SelectionToTsv_BoundingRectangleWithBlanks()
        {
            RunSta(() =>
            {
                using DataGridView grid = Grid();
                grid.ClearSelection();
                Check.Equal("", grid.SelectionToTsv());
                grid.Rows[0].Cells[0].Selected = true;                           // A
                grid.Rows[1].Cells[1].Selected = true;                           // 2
                string tsv = grid.SelectionToTsv(includeHeaders: true);
                Check.Equal("Sensor\tValue (V)\r\nA\t\r\n\t2\r\n", tsv);
            });
        }

        public void SelectionToTsv_QuotesAwkwardCells()
        {
            RunSta(() =>
            {
                using DataGridView grid = Grid();
                grid.ClearSelection();
                grid.Rows[2].Cells[2].Selected = true;
                Check.Equal("\"say \"\"hi\"\"\tthen tab\"\r\n", grid.SelectionToTsv());
            });
        }

        public void SaveCsv_WritesUtf8WithBom()
        {
            RunSta(() =>
            {
                using DataGridView grid = Grid();
                string path = Path.Combine(Path.GetTempPath(), "toolbelt-grid-" + Guid.NewGuid().ToString("N") + ".csv");
                try
                {
                    grid.SaveCsv(path);
                    byte[] bytes = File.ReadAllBytes(path);
                    Check.True(bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, "UTF-8 BOM for Excel");
                    Check.True(File.ReadAllText(path).StartsWith("Sensor,", StringComparison.Ordinal));
                }
                finally { File.Delete(path); }
            });
        }

        public void AutoSizeColumns_CapsWidth()
        {
            RunSta(() =>
            {
                using var form = new Form();
                var grid = new DataGridView { AllowUserToAddRows = false, Dock = DockStyle.Fill };
                form.Controls.Add(grid);
                form.CreateControl();
                grid.BindList(new[] { new Reading("A very very very long sensor name indeed", 1, "x") });
                grid.AutoSizeColumns(maxWidth: 60);
                Check.True(grid.Columns.Cast<DataGridViewColumn>().All(c => c.Width <= 60));
                Check.Throws<ArgumentOutOfRangeException>(() => grid.AutoSizeColumns(maxWidth: 0));
            });
        }

        private static void RunSta(Action body)
        {
            Exception? error = null;
            var t = new Thread(() => { try { body(); } catch (Exception ex) { error = ex; } }) { IsBackground = true };
            // FormattedValue follows the thread culture (by design: it is what the grid shows); pin it so "1.5" is stable.
            t.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join();
            if (error is not null) throw error;
        }
    }
}
