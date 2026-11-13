using System;
using System.Drawing;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using ToolBelt.Tests.Framework;
using ToolBelt.WinForms;

namespace ToolBelt.WinForms.Tests
{
    /// <summary>Round 51 review regressions (WinForms satellite, third pass).</summary>
    public sealed class WinFormsReview3Tests
    {
        public void ZoomAbout_AnOffsetPastTheFloatRange_LeavesTheViewUnchanged()
        {
            var offset = new PointF(-1e30f, 0);
            var (zoom, o) = PlotPictureBox.ZoomAbout(1, offset, PointF.Empty, 1e10, 0, double.MaxValue);   // was X = -∞
            Check.Equal(1.0, zoom);
            Check.Equal(offset, o);
            var (z2, o2) = PlotPictureBox.ZoomAbout(1, new PointF(-10, 5), PointF.Empty, 2);
            Check.Equal(2.0, z2);
            Check.Equal(new PointF(-20, 10), o2);
        }

        public void Wheel_KeepsZoomingWithoutThrowing_UpToAHugeMaxZoom()
        {
            RunSta(() =>
            {
                using var image = new Bitmap(10, 10);
                using var pb = new PlotPictureBox { Size = new Size(200, 200), Image = image, MaxZoom = double.MaxValue };
                var wheel = typeof(PlotPictureBox).GetMethod("OnMouseWheel", BindingFlags.Instance | BindingFlags.NonPublic)!;
                for (int i = 0; i < 400; i++)                                          // threw at about event 46
                    wheel.Invoke(pb, new object[] { new MouseEventArgs(MouseButtons.None, 0, 190, 190, 1200) });
                Check.True(float.IsFinite(pb.Offset.X) && float.IsFinite(pb.Offset.Y));
                pb.ZoomAt(new PointF(10, 10), 0.5);
                pb.PanBy(1, 1);
            });
        }

        public void FitToView_AHugeMinZoom_KeepsAFiniteOffset()
        {
            RunSta(() =>
            {
                using var image = new Bitmap(10, 10);
                using var pb = new PlotPictureBox { Size = new Size(200, 200), MaxZoom = 1e300, MinZoom = 1e300, Image = image };
                pb.FitToView();                                                        // offset was -∞
                Check.Equal(1e300, pb.Zoom);
                Check.True(float.IsFinite(pb.Offset.X) && float.IsFinite(pb.Offset.Y));
                pb.ZoomAt(new PointF(10, 10), 0.5);                                     // threw "Must be finite"
            });
        }

        public void AutoSizeColumns_CapIsHeldToMinimumWidth()
        {
            RunSta(() =>
            {
                using var form = new Form();
                var grid = new DataGridView { Width = 600 };
                form.Controls.Add(grid);
                grid.Columns.Add("a", "a");
                grid.Columns[0].MinimumWidth = 120;
                grid.Rows.Add(new string('W', 50));
                grid.AutoSizeColumns(maxWidth: 100);
                Check.Equal(120, grid.Columns[0].Width);                               // documented: never below MinimumWidth
                grid.Columns[0].MinimumWidth = 5;
                grid.AutoSizeColumns(maxWidth: 100);
                Check.Equal(100, grid.Columns[0].Width);
            });
        }

        public void Runner_AnAssemblyWithNoTests_IsNotAPass()
        {
            // scripts/net48.sh with a misspelled folder compiled no tests and reported "ALL GREEN 0 passed".
            var empty = System.Reflection.Emit.AssemblyBuilder.DefineDynamicAssembly(
                new AssemblyName("tb-r51-empty"), System.Reflection.Emit.AssemblyBuilderAccess.Run);
            empty.DefineDynamicModule("m");
            var console = Console.Out;
            try
            {
                Console.SetOut(System.IO.TextWriter.Null);
                Check.Equal(TestRunner.NoMatch, TestRunner.Run(empty, Array.Empty<string>()));   // was 0
            }
            finally { Console.SetOut(console); }
        }

        private sealed class Command : System.Windows.Input.ICommand
        {
            public volatile bool Can = true;
            public event EventHandler? CanExecuteChanged;
            public bool CanExecute(object? parameter) => Can;
            public void Execute(object? parameter) { }
            public void Raise() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }

        // A menu item (its dropdown never shown, so no handle: the binding thread's context) and a toolbar button
        // (a shown strip: its handle).
        public void CommandBinding_ARaiseFromAWorker_UpdatesAMenuItemOnTheUiThread() => RaiseFromWorker(toolbar: false);

        public void CommandBinding_ARaiseFromAWorker_UpdatesAToolbarButtonOnTheUiThread() => RaiseFromWorker(toolbar: true);

        private static void RaiseFromWorker(bool toolbar)
        {
            RunSta(() =>
            {
                Control.CheckForIllegalCrossThreadCalls = true;
                using var form = new Form { ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(-2000, -2000) };
                var cmd = new Command();
                ToolStrip strip;
                ToolStripItem item;
                if (toolbar)
                {
                    strip = new ToolStrip();
                    new ToolStripBuilder().Button("Go", cmd).ApplyTo(strip);
                    item = strip.Items[0];
                }
                else
                {
                    strip = new MenuBuilder().Menu("File", m => m.Item("Go", cmd)).Build();
                    item = ((ToolStripMenuItem)strip.Items[0]).DropDownItems[0];
                }
                form.Controls.Add(strip);
                form.Show();
                int ui = Environment.CurrentManagedThreadId, seen = -1;
                item.EnabledChanged += (_, __) => { seen = Environment.CurrentManagedThreadId; form.Text = "changed"; };
                cmd.Can = false;
                Exception? raised = null;
                System.Threading.Tasks.Task.Run(() => { try { cmd.Raise(); } catch (Exception ex) { raised = ex; } }).Wait();
                Check.True(raised is null, "the worker's raise threw: " + raised?.Message);   // was cross-thread InvalidOperationException
                for (int i = 0; i < 200 && seen < 0; i++) { Application.DoEvents(); Thread.Sleep(5); }
                Check.Equal(ui, seen);
                Check.False(item.Enabled);
                cmd.Can = true;
                cmd.Raise();                                                           // on the UI thread: still immediate
                Check.True(item.Enabled);
            });
        }

        public void SaveCsv_AFailurePartway_KeepsThePreviousFile()
        {
            RunSta(() =>
            {
                string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "tb-r51-" + Guid.NewGuid().ToString("N"));
                System.IO.Directory.CreateDirectory(dir);
                string path = System.IO.Path.Combine(dir, "out.csv");
                try
                {
                    System.IO.File.WriteAllText(path, "previous,good,export\r\n1,2,3\r\n");
                    using var form = new Form();
                    var grid = new DataGridView();
                    form.Controls.Add(grid);
                    grid.Columns.Add("A", "A");
                    for (int r = 0; r < 3; r++) grid.Rows.Add("row" + r);
                    grid.CellFormatting += (_, e) => { if (e.RowIndex == 2) throw new InvalidOperationException("format"); };
                    Check.Throws<InvalidOperationException>(() => grid.SaveCsv(path));
                    Check.Equal("previous,good,export\r\n1,2,3\r\n", System.IO.File.ReadAllText(path));   // was "A\r\nrow0\r\nrow1\r\n"
                    Check.Equal(1, System.IO.Directory.GetFiles(dir).Length);                            // no temp left behind
                    grid.Rows.RemoveAt(2);
                    grid.SaveCsv(path);
                    Check.Equal("A\r\nrow0\r\nrow1\r\n", System.IO.File.ReadAllText(path));
                    Check.Equal(1, System.IO.Directory.GetFiles(dir).Length);
                }
                finally { System.IO.Directory.Delete(dir, true); }
            });
        }

        public void ToCsv_EscapeFormulas_DefusesSpreadsheetFormulas()
        {
            RunSta(() =>
            {
                using var form = new Form();
                var grid = new DataGridView();
                form.Controls.Add(grid);
                grid.Columns.Add("A", "A");
                foreach (var v in new[] { "=HYPERLINK(\"http://x\",\"y\")", "@SUM(1+1)", "+1+1", "-5", "-1.5e3", "\tx", "plain" }) grid.Rows.Add(v);
                Check.Equal("A\r\n\"'=HYPERLINK(\"\"http://x\"\",\"\"y\"\")\"\r\n'@SUM(1+1)\r\n'+1+1\r\n-5\r\n-1.5e3\r\n'\tx\r\nplain\r\n",
                    grid.ToCsv(escapeFormulas: true));
                Check.Equal("A\r\n\"=HYPERLINK(\"\"http://x\"\",\"\"y\"\")\"\r\n@SUM(1+1)\r\n+1+1\r\n-5\r\n-1.5e3\r\n\"\tx\"\r\nplain\r\n",
                    grid.ToCsv());                                                    // default unchanged
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
