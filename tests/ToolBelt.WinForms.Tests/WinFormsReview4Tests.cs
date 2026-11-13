using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows.Forms;
using ToolBelt.Tests.Framework;
using ToolBelt.WinForms;

namespace ToolBelt.WinForms.Tests
{
    /// <summary>Round 60 review regressions (WinForms satellite, fourth pass).</summary>
    public sealed class WinFormsReview4Tests
    {
        public void MenuShortcut_MembersThatAreNoKey_AreRejected()
        {
            foreach (string s in new[] { "Ctrl+KeyCode", "Ctrl+Packet", "Ctrl+NoName", "Ctrl+ProcessKey", "Alt+Shift+KeyCode" })
                Check.False(MenuShortcut.TryParse(s, out _), s);                       // all parsed, "Ctrl+KeyCode" as Control | 0xFFFF
            Check.True(MenuShortcut.TryParse("Ctrl+S", out Keys k));
            Check.Equal(Keys.Control | Keys.S, k);
        }

        public void MenuShortcut_AHyphenNamesTheMinusKey()
        {
            Check.True(MenuShortcut.TryParse("Ctrl+-", out Keys k));                   // was false; "Ctrl+Minus" worked
            Check.Equal(Keys.Control | Keys.OemMinus, k);
            Check.Equal("Ctrl+Minus", MenuShortcut.Format(k));
            Check.Equal(Keys.Control | Keys.Shift | Keys.OemMinus, MenuShortcut.Parse("Ctrl+Shift+-"));
            Check.Equal(Keys.Control | Keys.Oemplus, MenuShortcut.Parse("Ctrl++"));
        }

        public void FindItem_IgnoresMnemonicsInThePathToo()
        {
            RunSta(() =>
            {
                using var strip = new MenuBuilder().Menu("&File", m => m.Item("&Open...", () => { }).Item("R&&D", () => { })).Build();
                ToolStripItem? open = MenuBuilder.FindItem(strip, "File/Open...");
                Check.True(open is not null);
                Check.True(ReferenceEquals(open, MenuBuilder.FindItem(strip, "&File/&Open...")));   // was null
                Check.True(ReferenceEquals(open, MenuBuilder.FindItem(strip, " &File / Open... ")));
                Check.Equal("R&&D", MenuBuilder.FindItem(strip, "File/R&&D")?.Text);
                Check.True(MenuBuilder.FindItem(strip, "File/Close") is null);
            });
        }

        public void ToolStripBuilder_EachBuildHasTheItems()
        {
            RunSta(() =>
            {
                int clicks = 0;
                var cmd = new Command();
                var builder = new ToolStripBuilder().Button("A", () => clicks++).Separator().Label("L").Button("Go", cmd)
                    .Toggle("T", true, _ => { }).DropDown("D", m => m.Item("x", () => clicks += 10));
                using var first = builder.Build();
                using var second = builder.Build();                                     // was empty
                Check.Equal(6, first.Items.Count);
                Check.Equal(6, second.Items.Count);
                Check.False(ReferenceEquals(first.Items[0], second.Items[0]));
                second.Items[0].PerformClick();
                first.Items[0].PerformClick();
                Check.Equal(2, clicks);
                ((ToolStripDropDownButton)second.Items[5]).DropDownItems[0].PerformClick();
                Check.Equal(12, clicks);
                second.Items[3].PerformClick();
                first.Items[3].PerformClick();
                Check.Equal(2, cmd.Executed);
                cmd.Can = false;
                cmd.Raise();
                Check.False(first.Items[3].Enabled);
                Check.False(second.Items[3].Enabled);
                using var third = new ToolStrip();
                builder.ApplyTo(third);
                Check.Equal(6, third.Items.Count);
            });
        }

        public void AutoSizeColumns_AnUndefinedModeNamesMode()
        {
            RunSta(() =>
            {
                using var form = new Form();
                var grid = new DataGridView();
                form.Controls.Add(grid);
                grid.Columns.Add("a", "a");
                var ex = Check.Throws<ArgumentOutOfRangeException>(() => grid.AutoSizeColumns(mode: (DataGridViewAutoSizeColumnsMode)99));
                Check.Equal("mode", ex.ParamName);                                     // was InvalidEnumArgumentException, "autoSizeColumnMode"
            });
        }

        public void PlotPictureBox_ANoOpZoomOrPan_RaisesNothingAndKeepsAutoFit()
        {
            RunSta(() =>
            {
                using var image = new Bitmap(10, 10);
                using var pb = new PlotPictureBox { Size = new Size(20, 20), MaxZoom = 2, Image = image };
                Check.Equal(2.0, pb.Zoom);
                int changes = 0;
                pb.ViewChanged += (_, __) => changes++;
                pb.ZoomAt(new PointF(5, 5), 2);                                         // clamped at MaxZoom: no change
                pb.PanBy(0, 0);
                Check.Equal(0, changes);                                                // was 2
                pb.Size = new Size(10, 10);                                             // still auto-fitting
                Check.Equal(1.0, pb.Zoom);
                Check.Equal(1, changes);
                pb.PanBy(3, 0);
                Check.Equal(2, changes);
                pb.Size = new Size(20, 20);                                             // a real pan ends auto-fit
                Check.Equal(1.0, pb.Zoom);
            });
        }

        public void PlotPictureBox_SmallPansStillMoveAViewFarOffAtDeepZoom()
        {
            RunSta(() =>
            {
                using var image = new Bitmap(100, 100);
                using var pb = new PlotPictureBox { Size = new Size(100, 100), MaxZoom = 1e6, Image = image };
                pb.ZoomAt(new PointF(99, 50), 1e6);
                Check.Equal(1e6, pb.Zoom);
                PointF before = pb.ContentToView(new PointF(99, 50));
                for (int i = 0; i < 100; i++) pb.PanBy(1, 0);                           // each one rounded away
                PointF after = pb.ContentToView(new PointF(99, 50));
                Check.True(Math.Abs(after.X - before.X - 100) <= 8, $"moved {after.X - before.X}");
            });
        }

        public void PlotPictureBox_SmoothScalingKeepsTheImageEdgesSolid()
        {
            RunSta(() =>
            {
                using var image = new Bitmap(4, 4);
                using (var g = Graphics.FromImage(image)) g.Clear(Color.Red);
                using var pb = new PlotPictureBox { Size = new Size(40, 40), BackColor = Color.White, Smooth = true, Image = image };
                using var shot = new Bitmap(40, 40);
                pb.DrawToBitmap(shot, new Rectangle(0, 0, 40, 40));
                foreach (var (x, y) in new[] { (0, 20), (39, 20), (20, 0), (20, 39), (0, 0), (20, 20) })
                {
                    Color c = shot.GetPixel(x, y);
                    Check.True(c.R > 240 && c.G < 16 && c.B < 16, $"({x},{y}) is {c}");  // the border faded towards white
                }
            });
        }

        public void BeginInvokeIfRequired_AControlDisposedBeforeThePostRuns_IsNotTouched()
        {
            RunSta(() =>
            {
                using var form = new Form { ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(-2000, -2000) };
                var tabs = new TabControl { Dock = DockStyle.Fill };
                var shown = new TabPage("one");
                var hidden = new TabPage("two");
                var label = new Label { Text = "x" };
                hidden.Controls.Add(label);
                tabs.TabPages.Add(shown);
                tabs.TabPages.Add(hidden);
                form.Controls.Add(tabs);
                form.Show();
                Application.DoEvents();
                Check.False(label.IsHandleCreated);
                bool ran = false;
                System.Threading.Tasks.Task.Run(() => label.BeginInvokeIfRequired(() => ran = true)).Wait();
                tabs.TabPages.Remove(hidden);
                hidden.Dispose();
                Check.True(label.IsDisposed);
                for (int i = 0; i < 20; i++) { Application.DoEvents(); Thread.Sleep(5); }
                Check.False(ran);                                                       // ran on the disposed label

                var live = new Label();
                shown.Controls.Add(live);
                bool ranLive = false;
                System.Threading.Tasks.Task.Run(() => live.BeginInvokeIfRequired(() => ranLive = true)).Wait();
                for (int i = 0; i < 200 && !ranLive; i++) { Application.DoEvents(); Thread.Sleep(5); }
                Check.True(ranLive);
            });
        }

        public void CommandBinding_DroppedItemsDoNotKeepTheirParameterAlive()
        {
            RunSta(() =>
            {
                var cmd = new Command();
                var parameters = new List<WeakReference>();
                for (int i = 0; i < 50; i++) parameters.Add(BuildAndDrop(cmd));
                for (int i = 0; i < 3; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); }
                int alive = parameters.FindAll(w => w.IsAlive).Count;
                Check.Equal(0, alive);                                                  // all 50 held by the command
                using var strip = new ToolStripBuilder().Button("Go", cmd, parameter: "kept").Build();   // binding prunes the dead
                Check.True(cmd.Subscribers <= 2, $"{cmd.Subscribers} subscribers");
                cmd.Can = false;
                cmd.Raise();
                Check.False(strip.Items[0].Enabled);
                strip.Items[0].PerformClick();
                cmd.Can = true;
                cmd.Raise();
                strip.Items[0].PerformClick();
                Check.Equal("kept", cmd.LastParameter);
            });
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference BuildAndDrop(Command cmd)
        {
            var parameter = new byte[1024];
            var strip = new ToolStripBuilder().Button("Delete row", cmd, parameter: parameter).Build();
            strip.Items.Clear();                                                         // removed, not disposed
            strip.Dispose();
            return new WeakReference(parameter);
        }

        public void CommandBinding_ARaiseBeforeTheStripHasAHandle_RunsOnTheBindingThread()
        {
            RunSta(() =>
            {
                SynchronizationContext.SetSynchronizationContext(null);
                WindowsFormsSynchronizationContext.AutoInstall = false;
                try
                {
                    var cmd = new Command();
                    using var strip = new ToolStripBuilder().Button("Go", cmd).Build();
                    Check.False(SynchronizationContext.Current is WindowsFormsSynchronizationContext);   // no WinForms context to capture
                    ToolStripItem item = strip.Items[0];
                    int ui = Environment.CurrentManagedThreadId, seen = -1;
                    item.EnabledChanged += (_, __) => seen = Environment.CurrentManagedThreadId;
                    cmd.Can = false;
                    System.Threading.Tasks.Task.Run(cmd.Raise).Wait();
                    for (int i = 0; i < 200 && seen < 0; i++) { Application.DoEvents(); Thread.Sleep(5); }
                    Check.Equal(ui, seen);                                              // was the worker's thread
                    Check.False(item.Enabled);
                }
                finally { WindowsFormsSynchronizationContext.AutoInstall = true; }
            });
        }

        public void Csv_AnImageColumnExportsEmptyFields()
        {
            RunSta(() =>
            {
                using var form = new Form();
                var grid = new DataGridView { AllowUserToAddRows = false };
                form.Controls.Add(grid);
                grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "Name" });
                grid.Columns.Add(new DataGridViewImageColumn { Name = "Pic", HeaderText = "Pic" });
                using var bmp = new Bitmap(2, 2);
                grid.Rows.Add("a", bmp);
                grid.Rows.Add("b", null);
                Check.Equal("Name,Pic\r\na,\r\nb,\r\n", grid.ToCsv());                 // was "a,System.Drawing.Bitmap"
                grid.SelectAll();
                Check.Equal("a\t\r\nb\t\r\n", grid.SelectionToTsv());
            });
        }

        public void EnsureOnScreen_ACaptionSliverIsNotReachable()
        {
            var screen = new[] { new Rectangle(0, 0, 1920, 1040) };
            Rectangle top = FormStatePersistence.EnsureOnScreen(new Rectangle(100, -29, 800, 600), screen);
            Check.Equal(new Rectangle(100, 0, 800, 600), top);                          // 1 px of caption was accepted
            Rectangle corner = FormStatePersistence.EnsureOnScreen(new Rectangle(1870, 1039, 800, 600), screen);
            Check.Equal(new Rectangle(1120, 440, 800, 600), corner);
            var half = new Rectangle(100, -15, 800, 600);                              // 15 px of caption: enough to grab
            Check.Equal(half, FormStatePersistence.EnsureOnScreen(half, screen));
            var small = new Rectangle(100, 1030, 800, 10);                             // a window only 10 px tall, all on screen
            Check.Equal(small, FormStatePersistence.EnsureOnScreen(small, screen));
        }

        private sealed class Command : System.Windows.Input.ICommand
        {
            private EventHandler? _changed;
            public volatile bool Can = true;
            public int Executed;
            public object? LastParameter;
            public event EventHandler? CanExecuteChanged { add => _changed += value; remove => _changed -= value; }
            public int Subscribers => _changed?.GetInvocationList().Length ?? 0;
            public bool CanExecute(object? parameter) => Can;
            public void Execute(object? parameter) { Executed++; LastParameter = parameter; }
            public void Raise() => _changed?.Invoke(this, EventArgs.Empty);
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
