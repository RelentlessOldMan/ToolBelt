using System;
using System.Drawing;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using ToolBelt.Tests.Framework;
using ToolBelt.WinForms;

namespace ToolBelt.WinForms.Tests
{
    /// <summary>Round 132 review regressions (WinForms, tooling and docs, eighth pass).</summary>
    public sealed class WinFormsReview132Tests
    {
        // ---------- PlotPictureBox drawing ----------

        // Past the default MaxZoom, GDI+'s nearest-neighbour stretch put pixel boundaries away from ContentToView: one
        // meant for view x 100 was drawn at 109 at zoom 2e4 and 65 at 1e5, and at 1e6 the view showed one colour.
        public void Plot_MagnifiedPixelsSitWhereContentToViewSays()
        {
            RunSta(() =>
            {
                using var strip = new Bitmap(4, 1);
                for (int i = 0; i < 4; i++) strip.SetPixel(i, 0, i % 2 == 0 ? Color.Red : Color.Blue);
                using var pb = new PlotPictureBox { Size = new Size(200, 20), MaxZoom = 1e9, Image = strip };
                foreach (double zoom in new[] { 3.0, 64, 1000, 2e4, 5e4, 1e5, 1e6, 3e6 })
                {
                    pb.ResetView();
                    pb.ZoomAt(new PointF(2, 0), zoom);
                    pb.PanBy(98, 0);
                    Check.Equal(100f, pb.ContentToView(new PointF(2, 0)).X, $"zoom {zoom}: column 2 starts at view x 100");
                    using var bmp = new Bitmap(200, 20);
                    pb.DrawToBitmap(bmp, new Rectangle(0, 0, 200, 20));
                    for (int x = 0; x < 200; x++)
                    {
                        double col = Math.Floor(pb.ViewToContent(new PointF(x + 0.5f, 0)).X);
                        if (col < 0 || col >= 4) continue;
                        Color want = (int)col % 2 == 0 ? Color.Red : Color.Blue;
                        Check.Equal(want.ToArgb(), bmp.GetPixel(x, 0).ToArgb(), $"zoom {zoom}, view x {x}");
                    }
                }
            });
        }

        // A wheel factor whose power overflowed (1e200 over two notches) dropped the turn instead of zooming to MaxZoom.
        public void Plot_AnOverflowingWheelPowerZoomsToTheLimit()
        {
            RunSta(() =>
            {
                using var image = new Bitmap(4, 4);
                using var pb = new PlotPictureBox { Size = new Size(100, 100), MaxZoom = 64, MinZoom = 1e-3, Image = image };
                pb.ResetView();
                var wheel = typeof(PlotPictureBox).GetMethod("OnMouseWheel", BindingFlags.Instance | BindingFlags.NonPublic)!;
                pb.WheelFactor = 1e200;
                wheel.Invoke(pb, new object[] { new MouseEventArgs(MouseButtons.None, 0, 10, 10, 240) });
                Check.Equal(64.0, pb.Zoom);
                pb.WheelFactor = 1e-200;   // zooms out: its power underflows to 0
                wheel.Invoke(pb, new object[] { new MouseEventArgs(MouseButtons.None, 0, 10, 10, 240) });
                Check.Equal(1e-3, pb.Zoom);
            });
        }

        // ---------- MenuBuilder.FindItem ----------

        // A text box's Text is what the user typed, so FindItem("Open") found it ahead of the button named Open.
        public void FindItem_SkipsHostedControlsAndBlankPaths()
        {
            RunSta(() =>
            {
                using var strip = new ToolStrip();
                var box = new ToolStripTextBox { Text = "Open" };
                var combo = new ToolStripComboBox { Text = "Save" };
                var blank = new ToolStripButton("");
                var open = new ToolStripButton("&Open");
                strip.Items.AddRange(new ToolStripItem[] { box, combo, blank, open });
                Check.True(ReferenceEquals(open, MenuBuilder.FindItem(strip, "Open")), "the button, not the text box");
                Check.True(MenuBuilder.FindItem(strip, "Save") is null, "a combo box's text is not a name");
                Check.True(MenuBuilder.FindItem(strip, "") is null, "\"\" names nothing");
                Check.True(MenuBuilder.FindItem(strip, "  ") is null, "a blank path names nothing");
            });
        }

        // ---------- FormState.TryParse ----------

        // TryParse took text Serialize never writes (spaces, "+"), and an X near int.MaxValue whose Right wrapped negative.
        public void FormState_TryParse_TakesOnlyWhatSerializeWrites()
        {
            foreach (string text in new[] { " 1,2,3,4,0", "1, +2,3 ,4,0", "+1,2,3,4,0", "01,2,3,4,0", "-0,2,3,4,0", "1,2,3,4, 0",
                                            "2147483647,0,100,100,0", "0,2147483600,100,100,2" })
                Check.False(FormState.TryParse(text, out _), text);
            var rnd = new Random(132);
            for (int i = 0; i < 500; i++)
            {
                int w = rnd.Next(1, 5000), h = rnd.Next(1, 5000);
                var s = new FormState(new Rectangle(rnd.Next(int.MinValue, int.MaxValue - w), rnd.Next(int.MinValue, int.MaxValue - h), w, h),
                                      rnd.Next(2) == 0 ? FormWindowState.Normal : FormWindowState.Maximized);
                Check.True(FormState.TryParse(s.Serialize(), out FormState back), s.Serialize());
                Check.Equal(s.Bounds, back.Bounds);
                Check.Equal(s.WindowState, back.WindowState);
            }
            Check.True(FormState.TryParse("-5,-7,300,200,0", out FormState neg));
            Check.Equal(new Rectangle(-5, -7, 300, 200), neg.Bounds);
        }

        // ---------- test runner ----------

        // --list returned 0 before the no-match check, so a mistyped filter listed nothing and still exited 0.
        public void Runner_ListWithAFilterThatMatchesNothing_ExitsNoMatch()
        {
            var console = Console.Out;
            try
            {
                Console.SetOut(System.IO.TextWriter.Null);
                var assembly = typeof(WinFormsReview132Tests).Assembly;
                Check.Equal(TestRunner.NoMatch, TestRunner.Run(assembly, new[] { "--list", "--filter", "ZZZ_no_such_test_ZZZ" }));
                Check.Equal(0, TestRunner.Run(assembly, new[] { "--list", "--filter", "Runner_ListWithAFilter" }));
            }
            finally { Console.SetOut(console); }
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
