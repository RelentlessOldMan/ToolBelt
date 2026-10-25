using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using ToolBelt.Tests.Framework;
using ToolBelt.WinForms;

namespace ToolBelt.WinForms.Tests
{
    /// <summary>Round 34 review regressions (WinForms satellite, second pass).</summary>
    public sealed class WinFormsReview2Tests
    {
        public void SaveCsv_InvalidDelimiter_LeavesTheFileUntouched()
        {
            RunSta(() =>
            {
                string path = Path.Combine(Path.GetTempPath(), "tb-r34-" + Guid.NewGuid().ToString("N") + ".csv");
                try
                {
                    File.WriteAllText(path, "precious data");
                    using var grid = new DataGridView();
                    var ex = Check.Throws<ArgumentException>(() => grid.SaveCsv(path, delimiter: '"'));
                    Check.Equal("delimiter", ex.ParamName);
                    Check.Equal("precious data", File.ReadAllText(path));
                }
                finally { File.Delete(path); }
            });
        }

        public void ZoomAt_NonFinitePoint_IsRejected()
        {
            RunSta(() =>
            {
                using var pb = new PlotPictureBox { Size = new Size(100, 100) };
                var before = (pb.Zoom, pb.Offset);
                Check.Throws<ArgumentOutOfRangeException>(() => pb.ZoomAt(new PointF(float.NaN, 0), 2));
                Check.Throws<ArgumentOutOfRangeException>(() => pb.ZoomAt(new PointF(0, float.PositiveInfinity), 2));
                Check.Equal(before, (pb.Zoom, pb.Offset));
                Check.Throws<ArgumentOutOfRangeException>(() => PlotPictureBox.ZoomAbout(double.PositiveInfinity, PointF.Empty, PointF.Empty, 2));
                Check.Throws<ArgumentOutOfRangeException>(() => PlotPictureBox.ZoomAbout(1, PointF.Empty, new PointF(float.NaN, 0), 2));
                Check.Throws<ArgumentOutOfRangeException>(() => PlotPictureBox.ZoomAbout(1, new PointF(0, float.NaN), PointF.Empty, 2));
            });
        }

        public void Paint_FarFromTheOrigin_DrawsTheVisiblePixels()
        {
            RunSta(() =>
            {
                using var image = new Bitmap(2, 1);
                image.SetPixel(0, 0, Color.Red);
                image.SetPixel(1, 0, Color.Blue);
                using var pb = new PlotPictureBox { Size = new Size(100, 100), Image = image, MaxZoom = 1e10 };
                pb.ResetView();
                pb.ZoomAt(PointF.Empty, 1e10);
                pb.PanBy(-1.5e10f, 0); // content x = 1.5 is at the left edge: the blue pixel, 2e10 view pixels wide
                using var target = new Bitmap(100, 100);
                Paint(pb, target);
                Check.Equal(Color.Blue.ToArgb(), target.GetPixel(50, 50).ToArgb());

                pb.PanBy(1.5e10f, 0); // back to the red pixel
                using var target2 = new Bitmap(100, 100);
                Paint(pb, target2);
                Check.Equal(Color.Red.ToArgb(), target2.GetPixel(50, 50).ToArgb());

                // Moderately deep: the image is 1e7 view pixels wide, each source pixel 1e5.
                using var strip = new Bitmap(100, 1);
                for (int i = 0; i < 100; i++) strip.SetPixel(i, 0, i == 70 ? Color.Lime : Color.Red);
                pb.Image = strip;
                pb.ResetView();
                pb.ZoomAt(PointF.Empty, 1e5);
                pb.PanBy(50 - 70.5e5f, 0); // content x = 70.5 at view x = 50
                using var target3 = new Bitmap(100, 100);
                Paint(pb, target3);
                Check.Equal(Color.Lime.ToArgb(), target3.GetPixel(50, 0).ToArgb());
            });
        }

        public void InvokeIfRequired_NoHandleFromAWorker_DoesNotRunOnTheWorker()
        {
            RunSta(() =>
            {
                using var label = new Label(); // no handle: closed-but-not-disposed dialogs look the same
                int ran = 0;
                Exception? sync = null, func = null;
                var worker = new Thread(() =>
                {
                    try { label.InvokeIfRequired(() => { ran++; }); } catch (Exception ex) { sync = ex; }
                    try { label.InvokeIfRequired(() => ++ran); } catch (Exception ex) { func = ex; }
                    label.BeginInvokeIfRequired(() => { ran++; });
                });
                worker.Start();
                worker.Join();
                Check.Equal(0, ran);
                Check.True(sync is InvalidOperationException);
                Check.True(func is InvalidOperationException);
                Check.False(label.IsHandleCreated);

                label.InvokeIfRequired(() => { ran++; }); // the UI thread itself still runs it inline
                Check.Equal(1, ran);
            });
        }

        public void MenuShortcut_NonAsciiDigit_IsRejected()
        {
            Check.False(MenuShortcut.TryParse("Ctrl+٣", out _));
            Check.False(MenuShortcut.TryParse("Ctrl+１", out _));
            Check.True(MenuShortcut.TryParse("Ctrl+3", out Keys k));
            Check.Equal(Keys.Control | Keys.D3, k);
        }

        public void MenuShortcut_ParseNull_ThrowsArgumentNull()
        {
            var ex = Check.Throws<ArgumentNullException>(() => MenuShortcut.Parse(null!));
            Check.Equal("text", ex.ParamName);
        }

        public void TestRunner_FilterMatchingNothing_DoesNotReportSuccess()
        {
            int code = TestRunner.Run(typeof(WinFormsReview2Tests).Assembly, new[] { "--filter", "ZZZ_no_such_test_ZZZ" });
            Check.Equal(3, code);
        }

        public void PaintHandlers_DrawOnTopOfTheImage()
        {
            RunSta(() =>
            {
                using var image = new Bitmap(2, 2);
                using (var g = Graphics.FromImage(image)) g.Clear(Color.Blue);
                using var pb = new PlotPictureBox { Size = new Size(100, 100), Image = image };
                pb.Paint += (_, e) => e.Graphics.FillRectangle(Brushes.Red, 40, 40, 20, 20);
                using var target = new Bitmap(100, 100);
                Paint(pb, target);
                Check.Equal(Color.Red.ToArgb(), target.GetPixel(50, 50).ToArgb());
            });
        }

        public void PanBy_OverflowingOffset_IsRejected()
        {
            RunSta(() =>
            {
                using var pb = new PlotPictureBox { Size = new Size(100, 100) };
                pb.PanBy(3e38f, 0);
                Check.Throws<ArgumentOutOfRangeException>(() => pb.PanBy(3e38f, 0));
                Check.Equal(3e38f, pb.Offset.X);
            });
        }

        public void ZoomLimits_ResetViewHonoursThemAndZoomingInNeverZoomsOut()
        {
            RunSta(() =>
            {
                using var pb = new PlotPictureBox { Size = new Size(100, 100), MaxZoom = 0.5 };
                pb.ResetView();
                Check.Equal(0.5, pb.Zoom);
                pb.MaxZoom = 4;
                pb.ZoomAt(PointF.Empty, 8); // up to 4
                pb.MaxZoom = 2;
                pb.ZoomAt(PointF.Empty, 1.2); // zoom-in above the new limit: stays, does not drop to 2
                Check.Equal(4.0, pb.Zoom);
                pb.ZoomAt(PointF.Empty, 0.9); // zooming out comes back inside
                Check.Equal(2.0, pb.Zoom);
            });
        }

        public void CommandOverloads_TakeTheirOptionalArgumentsInTheActionOrder()
        {
            RunSta(() =>
            {
                var cmd = new Command();
                using var image = new Bitmap(4, 4);
                using var strip = new MenuBuilder().Menu("File", m => m.Item("&Save", cmd, "Ctrl+S")).Build();
                var item = (ToolStripMenuItem)MenuBuilder.FindItem(strip, "File/Save")!;
                Check.Equal(Keys.Control | Keys.S, item.ShortcutKeys);
                item.PerformClick();
                Check.Null(cmd.Last);

                using var tools = new ToolStripBuilder().Button("Go", cmd, image).Build();
                Check.True(ReferenceEquals(image, tools.Items[0].Image));
                tools.Items[0].PerformClick();
                Check.Null(cmd.Last);
                Check.Equal(2, cmd.Runs);
            });
        }

        public void FindItem_NeverMatchesASeparator()
        {
            RunSta(() =>
            {
                using var strip = new MenuBuilder().Menu("File", m => m.Item("Open", () => { }).Separator()).Build();
                Check.Null(MenuBuilder.FindItem(strip, "File/"));
                Check.NotNull(MenuBuilder.FindItem(strip, "File/Open"));
            });
        }

        private sealed class Command : System.Windows.Input.ICommand
        {
            public object? Last = "unset";
            public int Runs;
            public event EventHandler? CanExecuteChanged { add { } remove { } }
            public bool CanExecute(object? parameter) => true;
            public void Execute(object? parameter) { Last = parameter; Runs++; }
        }

        private static void Paint(PlotPictureBox pb, Bitmap target)
        {
            using var g = Graphics.FromImage(target);
            g.Clear(Color.White);
            var onPaint = typeof(PlotPictureBox).GetMethod("OnPaint", BindingFlags.Instance | BindingFlags.NonPublic)!;
            try { onPaint.Invoke(pb, new object[] { new PaintEventArgs(g, new Rectangle(0, 0, target.Width, target.Height)) }); }
            catch (TargetInvocationException ex) when (ex.InnerException is not null) { throw ex.InnerException; }
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
