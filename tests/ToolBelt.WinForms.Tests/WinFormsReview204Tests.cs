using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using ToolBelt.Tests.Framework;
using ToolBelt.WinForms;

namespace ToolBelt.WinForms.Tests
{
    /// <summary>Round 204 review regressions (WinForms, tooling and docs, twelfth pass).</summary>
    public sealed class WinFormsReview204Tests
    {
        // ---------- PlotPictureBox ----------

        // The smooth path left magnified views to GDI+'s bicubic stretch, which put the black|white boundary meant for view
        // x 100.25 at 92.6 (zoom 1000), 85.5 (2000) and 141 (5000). The interpolation is symmetric about the boundary, so
        // the first view column brighter than half is the first whose centre maps past content x 1, give or take one.
        public void PlotPictureBox_SmoothMagnifiedView_PutsTheBoundaryWhereContentToViewDoes()
        {
            RunSta(() =>
            {
                using var image = new Bitmap(2, 1);
                image.SetPixel(0, 0, Color.Black);
                image.SetPixel(1, 0, Color.White);
                using var pb = new PlotPictureBox { Size = new Size(200, 20), MaxZoom = 1e12, Smooth = true, Image = image };
                var rng = new Random(204);
                for (int trial = 0; trial < 60; trial++)
                {
                    double zoom = Math.Pow(10, 0.5 + rng.NextDouble() * 4);   // 3 to 3e4
                    int at = 20 + rng.Next(160);
                    pb.ResetView();
                    pb.ZoomAt(new PointF(1, 0), zoom);
                    pb.PanBy(at - pb.ContentToView(new PointF(1, 0)).X, 0);
                    using var bmp = new Bitmap(200, 20);
                    pb.DrawToBitmap(bmp, new Rectangle(0, 0, 200, 20));
                    // A view row through the image (it is one pixel high), and only the columns over it: the background
                    // beside it is the control's colour.
                    int row = Math.Min(19, (int)Math.Floor(pb.ContentToView(new PointF(0, 0.5f)).Y));
                    int first = -1, expected = -1;
                    for (int x = 0; x < 200; x++)
                    {
                        double u = pb.ViewToContent(new PointF(x + 0.5f, row + 0.5f)).X;
                        if (!(u > 0 && u < 2)) continue;
                        if (first < 0 && bmp.GetPixel(x, row).R >= 128) first = x;
                        if (expected < 0 && u > 1) expected = x;
                    }
                    Check.True(Math.Abs(first - expected) <= 1, $"zoom {pb.Zoom}: boundary at {first}, expected {expected}");
                }
            });
        }

        // ---------- MenuShortcut ----------

        // "Ctrl+Ctrl+S" parsed as Ctrl+S: a repeated modifier is no shortcut Format writes, so it is a typo to refuse.
        public void MenuShortcut_RepeatedModifier_IsRefused()
        {
            string[] names = { "Ctrl", "Control", "Alt", "Shift" };
            Keys[] flags = { Keys.Control, Keys.Control, Keys.Alt, Keys.Shift };
            var rng = new Random(2041);
            for (int trial = 0; trial < 2000; trial++)
            {
                int n = 1 + rng.Next(5);
                var parts = new string[n];
                var seen = Keys.None;
                bool repeated = false;
                for (int i = 0; i < n; i++)
                {
                    int m = rng.Next(4);
                    parts[i] = rng.Next(2) == 0 ? names[m] : names[m].ToUpperInvariant();
                    if ((seen & flags[m]) != 0) repeated = true;
                    seen |= flags[m];
                }
                string text = string.Join("+", parts) + "+S";
                bool ok = MenuShortcut.TryParse(text, out Keys keys);
                if (repeated)
                {
                    Check.False(ok, text);
                    Check.Equal(Keys.None, keys, text);
                    Check.Throws<FormatException>(() => MenuShortcut.Parse(text));
                }
                else if (ToolStripManager.IsValidShortcut(seen | Keys.S))   // Shift+S alone is no menu shortcut
                {
                    Check.True(ok, text);
                    Check.Equal(seen | Keys.S, keys, text);
                }
            }
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
