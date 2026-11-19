using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using ToolBelt.Tests.Framework;
using ToolBelt.WinForms;

namespace ToolBelt.WinForms.Tests
{
    /// <summary>Round 155 review regressions (WinForms, tooling and docs, ninth pass).</summary>
    public sealed class WinFormsReview155Tests
    {
        // ---------- DataGridViewExtensions ----------

        // A selected cell in a hidden row counted (SelectAll selects them): alone it gave a header-only result, and beside
        // a visible one it widened the span the visible rows were written over ("\t\tc" for "c").
        public void SelectionToTsv_CellsInHiddenRowsAreLeftOut()
        {
            RunSta(() =>
            {
                using var form = new Form();
                var grid = new DataGridView { AllowUserToAddRows = false };
                form.Controls.Add(grid);
                grid.Columns.Add("A", "A");
                grid.Columns.Add("B", "B");
                grid.Columns.Add("C", "C");
                grid.Rows.Add("a", "b", "c");
                grid.Rows.Add("d", "e", "f");
                grid.CurrentCell = null;
                grid.Rows[1].Visible = false;
                grid.ClearSelection();
                grid.Rows[1].Cells[0].Selected = true;
                Check.Equal("", grid.SelectionToTsv(includeHeaders: true));
                Check.Equal("", grid.SelectionToTsv());
                grid.Rows[0].Cells[2].Selected = true;
                Check.Equal("c\r\n", grid.SelectionToTsv());
                Check.Equal("C\r\nc\r\n", grid.SelectionToTsv(includeHeaders: true));
                // Guard: once the row is shown again its cell counts.
                grid.Rows[1].Visible = true;
                Check.Equal("\t\tc\r\nd\t\t\r\n", grid.SelectionToTsv());
            });
        }

        // A surrogate delimiter was taken, and every encoder wrote it as U+FFFD: a file no reader could split again.
        public void Csv_ASurrogateDelimiterIsRefused()
        {
            RunSta(() =>
            {
                using var form = new Form();
                var grid = new DataGridView { AllowUserToAddRows = false };
                form.Controls.Add(grid);
                grid.Columns.Add("A", "A");
                grid.Columns.Add("B", "B");
                grid.Rows.Add("x", "y");
                string dir = Path.Combine(Path.GetTempPath(), "tb-r155-" + Guid.NewGuid().ToString("N").Substring(0, 8));
                Directory.CreateDirectory(dir);
                try
                {
                    string path = Path.Combine(dir, "g.csv");
                    foreach (char d in new[] { '\uD800', '\uDBFF', '\uDC00', '\uDFFF' })
                    {
                        Check.Equal("delimiter", Check.Throws<ArgumentException>(() => grid.ToCsv(delimiter: d)).ParamName);
                        Check.Equal("delimiter", Check.Throws<ArgumentException>(() => grid.ToCsv(new StringWriter(), delimiter: d)).ParamName);
                        Check.Equal("delimiter", Check.Throws<ArgumentException>(() => grid.SaveCsv(path, delimiter: d)).ParamName);
                        Check.False(File.Exists(path));
                    }
                    // Guards: ordinary delimiters still work.
                    Check.Equal("A;B\r\nx;y\r\n", grid.ToCsv(delimiter: ';'));
                    Check.Equal("A\tB\r\nx\ty\r\n", grid.ToCsv(delimiter: '\t'));
                    Check.Equal("A\u00A7B\r\nx\u00A7y\r\n", grid.ToCsv(delimiter: '\u00A7'));
                }
                finally { Directory.Delete(dir, true); }
            });
        }

        // The write-then-move broke on targets that are not plain files: NUL threw FileNotFoundException for its temp file,
        // an alternate data stream threw "The parameter is incorrect", and a symbolic link was replaced by a plain file while
        // the file it named kept the old data.
        public void SaveCsv_DevicesStreamsAndLinksAreWritten()
        {
            RunSta(() =>
            {
                using var form = new Form();
                var grid = new DataGridView { AllowUserToAddRows = false };
                form.Controls.Add(grid);
                grid.Columns.Add("A", "A");
                grid.Rows.Add("x");
                string dir = Path.Combine(Path.GetTempPath(), "tb-r155-" + Guid.NewGuid().ToString("N").Substring(0, 8));
                Directory.CreateDirectory(dir);
                try
                {
                    grid.SaveCsv("NUL");
                    grid.SaveCsv(@"\\.\NUL");

                    string host = Path.Combine(dir, "host.txt");
                    File.WriteAllText(host, "main");
                    grid.SaveCsv(host + ":grid.csv");
                    Check.Equal("A\r\nx\r\n", File.ReadAllText(host + ":grid.csv"));
                    Check.Equal("main", File.ReadAllText(host));

                    string real = Path.Combine(dir, "real.csv"), link = Path.Combine(dir, "link.csv");
                    File.WriteAllText(real, "old");
                    bool linked;
                    try { File.CreateSymbolicLink(link, "real.csv"); linked = true; }
                    catch (IOException) { linked = false; }            // no symlink privilege (Developer Mode off)
                    catch (UnauthorizedAccessException) { linked = false; }
                    if (linked)
                    {
                        grid.SaveCsv(link);
                        Check.Equal("real.csv", new FileInfo(link).LinkTarget);
                        Check.Equal("A\r\nx\r\n", File.ReadAllText(real));
                    }
                    Check.Equal(linked ? 3 : 2, Directory.GetFiles(dir).Length, "no temp file left");
                }
                finally { Directory.Delete(dir, true); }
            });
        }

        // ---------- FormStatePersistence ----------

        // An empty state still set a manual start position and Normal, so the out value of a failed TryParse (or a
        // first-run default) pinned a centred form to (0,0) and dropped a designer-set Maximized.
        public void Apply_AnEmptyStateLeavesTheFormAlone()
        {
            RunSta(() =>
            {
                var areas = new[] { new Rectangle(0, 0, 1920, 1040) };
                Check.False(FormState.TryParse("garbage", out FormState failed));
                foreach (FormState empty in new[] { default, failed, new FormState(new Rectangle(10, 10, 0, 300), FormWindowState.Maximized) })
                {
                    using var form = new Form { StartPosition = FormStartPosition.CenterScreen, WindowState = FormWindowState.Maximized, Location = new Point(40, 50) };
                    FormStatePersistence.Apply(form, empty, areas);
                    Check.Equal(FormStartPosition.CenterScreen, form.StartPosition);
                    Check.Equal(FormWindowState.Maximized, form.WindowState);
                    Check.Equal(new Point(40, 50), form.Location);
                }
                // Guard: a real state still applies.
                using var placed = new Form { StartPosition = FormStartPosition.CenterScreen, WindowState = FormWindowState.Maximized };
                FormStatePersistence.Apply(placed, new FormState(new Rectangle(100, 120, 640, 480), FormWindowState.Normal), areas);
                Check.Equal(FormStartPosition.Manual, placed.StartPosition);
                Check.Equal(FormWindowState.Normal, placed.WindowState);
                Check.Equal(new Rectangle(100, 120, 640, 480), placed.Bounds);
            });
        }

        private static void RunSta(Action body)
        {
            Exception? error = null;
            var t = new Thread(() => { try { body(); } catch (Exception ex) { error = ex; } }) { IsBackground = true };
            t.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join();
            if (error is not null) throw error;
        }
    }
}
