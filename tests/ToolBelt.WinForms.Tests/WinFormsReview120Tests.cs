using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using ToolBelt.Tests.Framework;
using ToolBelt.WinForms;

namespace ToolBelt.WinForms.Tests
{
    /// <summary>Round 120 review regressions (WinForms, tooling and docs, seventh pass).</summary>
    public sealed class WinFormsReview120Tests
    {
        // ---------- DataGridView CSV ----------

        // In a one-column grid an empty cell was a blank line, which CSV readers skip: the row was lost, and an empty
        // header made the first data row the header.
        public void Csv_OneColumn_EmptyFieldIsNotABlankLine()
        {
            RunSta(() =>
            {
                using var form = new Form();
                var grid = new DataGridView();
                form.Controls.Add(grid);
                grid.Columns.Add("A", "A");
                foreach (var v in new[] { "x", "", "y" }) grid.Rows.Add(v);
                Check.Equal("A\r\nx\r\n\"\"\r\ny\r\n", grid.ToCsv());
                grid.Columns[0].HeaderText = "";
                Check.Equal("\"\"\r\nx\r\n\"\"\r\ny\r\n", grid.ToCsv());

                grid.Columns.Add("B", "B");                                    // two columns: "," is already a record
                grid.Rows[1].Cells[1].Value = "";
                Check.Equal(",B\r\nx,\r\n,\r\ny,\r\n", grid.ToCsv());
            });
        }

        // TryParse let through infinities and NaN ("-Infinity", "-∞", an overflowing "-1e400") and a leading tab or CR, so
        // those fields reached the spreadsheet as formulas. Finite signed numbers are still left alone.
        public void Csv_EscapeFormulas_OnlySparesFiniteSignedNumbers()
        {
            RunSta(() =>
            {
                using var form = new Form();
                var grid = new DataGridView();
                form.Controls.Add(grid);
                grid.Columns.Add("A", "A");
                string[] escaped = { "-Infinity", "+Infinity", "-NaN", "-" + (char)0x221E, "+" + (char)0x221E, "-1e400", "+1e400", "\t5", "\r5", "- 5", "-5 " };
                string[] spared = { "-5", "+5", "-1.5e3", "-0", "+.5", "-1e-400" };
                foreach (var v in escaped.Concat(spared)) grid.Rows.Add(v);
                var lines = grid.ToCsv(escapeFormulas: true).Split(new[] { "\r\n" }, StringSplitOptions.None);
                for (int i = 0; i < escaped.Length; i++)
                    Check.True(lines[1 + i].TrimStart('"').StartsWith("'", StringComparison.Ordinal), "escaped: " + lines[1 + i]);
                for (int i = 0; i < spared.Length; i++)
                    Check.Equal(spared[i], lines[1 + escaped.Length + i]);
            });
        }

        // ---------- PlotPictureBox ----------

        // The static ZoomAbout took NaN or negative limits silently (the zoom never changed); the instance setters reject them.
        public void ZoomAbout_RejectsBadLimits()
        {
            var o = new PointF(0, 0);
            foreach (double min in new[] { double.NaN, -1, double.PositiveInfinity })
                Check.Equal("minZoom", Check.Throws<ArgumentOutOfRangeException>(() => PlotPictureBox.ZoomAbout(2, o, o, 1.5, min, 10)).ParamName);
            foreach (double max in new[] { double.NaN, -1, 0 })
                Check.Equal("maxZoom", Check.Throws<ArgumentOutOfRangeException>(() => PlotPictureBox.ZoomAbout(2, o, o, 1.5, 0.1, max)).ParamName);
            Check.Close(3, PlotPictureBox.ZoomAbout(2, o, o, 1.5, 0, double.PositiveInfinity).Zoom);
        }

        // A combo cell whose value is not in its Items raised DataError while being read, and with no handler the grid
        // opened its modal error dialog (one per cell): the export blocked until it was closed.
        public void Export_UnformattableCell_DoesNotOpenADialog_AndExportsTheValue()
        {
            RunSta(() =>
            {
                using var form = new Form();
                var grid = new DataGridView();
                form.Controls.Add(grid);
                var col = new DataGridViewComboBoxColumn { Name = "C", HeaderText = "C" };
                col.Items.AddRange("a", "b");
                grid.Columns.Add(col);
                grid.Rows.Add();
                grid.Rows[0].Cells[0].Value = "zzz";
                Check.Equal("C\r\nzzz\r\n", grid.ToCsv());
                grid.Rows[0].Cells[0].Selected = true;
                Check.Equal("C\r\nzzz\r\n", grid.SelectionToTsv(includeHeaders: true));
                Check.Equal("zzz", grid.Rows[0].Cells[0].Value);
            });
        }

        // A header whose value is not a string is shown by the grid ("42") but HeaderText is empty for it.
        public void Export_NonStringHeader_IsWritten()
        {
            RunSta(() =>
            {
                using var form = new Form();
                var grid = new DataGridView();
                form.Controls.Add(grid);
                grid.Columns.Add("A", "A");
                grid.Columns[0].HeaderCell.Value = 42;
                grid.Rows.Add("x");
                Check.Equal("42\r\nx\r\n", grid.ToCsv());
                grid.Rows[0].Cells[0].Selected = true;
                Check.Equal("42\r\nx\r\n", grid.SelectionToTsv(includeHeaders: true));
            });
        }

        // The temp name (target + 13 characters) passed the 255-character file-name limit for names a direct write takes.
        public void SaveCsv_LongFileName_Works()
        {
            RunSta(() =>
            {
                using var form = new Form();
                var grid = new DataGridView();
                form.Controls.Add(grid);
                grid.Columns.Add("A", "A");
                grid.Rows.Add("x");
                string dir = Path.Combine(Path.GetTempPath(), "tb-r120-" + Guid.NewGuid().ToString("N").Substring(0, 8));
                Directory.CreateDirectory(dir);
                try
                {
                    foreach (int len in new[] { 243, 250, 255 })
                    {
                        string path = Path.Combine(dir, new string('n', len - 4) + ".csv");
                        grid.SaveCsv(path);
                        Check.Equal("A\r\nx\r\n", File.ReadAllText(path));
                        Check.Equal(1, Directory.GetFiles(dir).Length, "no temp file left, length " + len);
                        File.Delete(path);
                    }
                }
                finally { Directory.Delete(dir, true); }
            });
        }

        // ---------- ControlExtensions ----------

        // The thread that created the handle exited: Invoke returned without running the delegate, so the void overload did
        // nothing silently and the generic one threw NullReferenceException.
        public void InvokeIfRequired_UiThreadExited_Throws()
        {
            Control? c = null;
            var ui = new Thread(() => { c = new Control(); c.CreateControl(); });
            ui.SetApartmentState(ApartmentState.STA);
            ui.Start();
            ui.Join();
            Exception? e1 = null, e2 = null;
            bool ran = false;
            var w = new Thread(() =>
            {
                try { c!.InvokeIfRequired(() => { ran = true; }); } catch (Exception ex) { e1 = ex; }
                try { c!.InvokeIfRequired(() => 1); } catch (Exception ex) { e2 = ex; }
            });
            w.SetApartmentState(ApartmentState.MTA);
            w.Start();
            Check.True(w.Join(30000), "the worker returned");
            if (!ran) Check.True(e1 is InvalidOperationException, "void overload: " + (e1?.GetType().Name ?? "no exception"));
            Check.True(e2 is InvalidOperationException || e2 is ObjectDisposedException, "generic overload: " + (e2?.GetType().Name ?? "no exception"));
        }

        // ---------- PlotPictureBox.Image ----------

        // A disposed image was stored before the throw (an ArgumentException with no name), so every later paint failed.
        public void PlotImage_Disposed_IsRejectedAndNotKept()
        {
            RunSta(() =>
            {
                using var good = new Bitmap(10, 10);
                using var pb = new PlotPictureBox { Size = new Size(100, 100), Image = good };
                var bad = new Bitmap(10, 10);
                bad.Dispose();
                var ex = Check.Throws<ArgumentException>(() => pb.Image = bad);
                Check.Equal("value", ex.ParamName);
                Check.True(ReferenceEquals(good, pb.Image), "the previous image is kept");
                using var bmp = new Bitmap(100, 100);
                pb.DrawToBitmap(bmp, new Rectangle(0, 0, 100, 100));
            });
        }

        // ---------- scripts ----------

        // verify.sh's build line took tail -3 of the log, which always drops the "N Warning(s)" line. The line's own
        // pipeline is replayed on a typical log tail (tail -N, then the grep pattern).
        public void VerifyBuildLine_ShowsWarningsAndErrors()
        {
            string line = File.ReadAllLines(Path.Combine(RepoRoot(), "scripts", "verify.sh"))
                .Single(l => l.StartsWith("echo \"build: ", StringComparison.Ordinal));
            string[] log = { "  ToolBelt -> x.dll", "", "Build succeeded.", "", "    3 Warning(s)", "    0 Error(s)", "", "Time Elapsed 00:00:15.40" };
            var tail = Regex.Match(line, @"tail -(\d+)");
            var lines = tail.Success ? log.Skip(log.Length - int.Parse(tail.Groups[1].Value)).ToArray() : log;
            string pattern = Regex.Match(line, @"grep -E '([^']*)'").Groups[1].Value;
            var kept = lines.Where(l => Regex.IsMatch(l, pattern)).Select(l => l.Trim()).ToArray();
            Check.Equal("3 Warning(s)|0 Error(s)|Time Elapsed 00:00:15.40", string.Join("|", kept));
        }

        // checked-build.sh and net48.sh made their lock with a plain mkdir: a missing parent folder (a custom worktree path,
        // a $TMPDIR that does not exist yet) failed it, reported as "another run is using it", exit 2.
        public void ScriptLocks_CreateTheParentFolderFirst()
        {
            foreach (string script in new[] { "checked-build.sh", "net48.sh" })
            {
                var lines = File.ReadAllLines(Path.Combine(RepoRoot(), "scripts", script)).Select(l => l.Trim()).ToList();
                int lockAt = lines.FindIndex(l => l.StartsWith("mkdir \"$LOCK\"", StringComparison.Ordinal));
                int parentAt = lines.FindIndex(l => l.StartsWith("mkdir -p \"$(dirname \"$LOCK\")\"", StringComparison.Ordinal));
                Check.True(lockAt >= 0, script + ": lock line");
                Check.True(parentAt >= 0 && parentAt < lockAt, script + ": the parent is created before the lock");
            }
        }

        private static string RepoRoot()
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "ToolBelt.sln")))
                    return dir.FullName;
            throw new InvalidOperationException("The repository root was not found.");
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
