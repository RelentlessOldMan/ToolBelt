using System;
using System.Drawing;
using System.Threading;
using ToolBelt.Tests.Framework;
using ToolBelt.WinForms;

namespace ToolBelt.WinForms.Tests
{
    public sealed class PlotPictureBoxTests
    {
        public void Fit_ScalesUniformlyAndCentres()
        {
            var (zoom, offset) = PlotPictureBox.Fit(new Size(200, 100), new Size(400, 300));
            Check.Close(2, zoom);
            Check.Close(0, offset.X);
            Check.Close(50, offset.Y);
            Check.Close(1, PlotPictureBox.Fit(new Size(0, 1), new Size(10, 10)).Zoom);
        }

        public void ZoomAbout_KeepsPointFixedAndClamps()
        {
            var at = new PointF(120, 80);
            double zoom = 2;
            var offset = new PointF(30, -10);
            double cx = (at.X - offset.X) / zoom, cy = (at.Y - offset.Y) / zoom;   // content under the cursor
            var (z2, o2) = PlotPictureBox.ZoomAbout(zoom, offset, at, 1.5);
            Check.Close(3, z2);
            Check.Close(at.X, cx * z2 + o2.X, 1e-3);
            Check.Close(at.Y, cy * z2 + o2.Y, 1e-3);
            Check.Close(4, PlotPictureBox.ZoomAbout(2, offset, at, 100, 0.1, 4).Zoom);
            Check.Throws<ArgumentOutOfRangeException>(() => PlotPictureBox.ZoomAbout(2, offset, at, 0));
        }

        public void BitmapFromRgba_PreservesColours()
        {
            byte[] rgba = { 255, 0, 0, 255, 0, 128, 255, 200 };
            using Bitmap bmp = PlotPictureBox.BitmapFromRgba(2, 1, rgba);
            Check.Equal(Color.FromArgb(255, 255, 0, 0), bmp.GetPixel(0, 0));
            Check.Equal(Color.FromArgb(200, 0, 128, 255), bmp.GetPixel(1, 0));
            Check.Throws<ArgumentException>(() => PlotPictureBox.BitmapFromRgba(2, 2, new byte[4]));
        }

        public void Control_AutoFitsZoomsPansAndConverts()
        {
            RunSta(() =>
            {
                using var image = new Bitmap(200, 100);
                using var box = new PlotPictureBox { Size = new Size(400, 300) };
                box.Image = image;
                Check.Close(2, box.Zoom);
                Check.Close(50, box.Offset.Y);
                PointF c = box.ViewToContent(new PointF(200, 150));
                Check.Close(100, c.X, 1e-4);
                Check.Close(50, c.Y, 1e-4);

                int changes = 0;
                box.ViewChanged += (_, __) => changes++;
                PointF before = box.ViewToContent(new PointF(30, 40));
                box.ZoomAt(new PointF(30, 40), 2);
                PointF after = box.ViewToContent(new PointF(30, 40));
                Check.Close(before.X, after.X, 1e-3);
                Check.Close(before.Y, after.Y, 1e-3);

                box.Size = new Size(800, 600);                               // user zoomed: no re-fit on resize
                Check.Close(4, box.Zoom);
                box.PanBy(10, 0);
                box.ResetView();
                Check.Close(1, box.Zoom);
                box.FitToView();
                Check.Close(4, box.Zoom);                                    // min(800/200, 600/100)
                box.Size = new Size(400, 300);                               // fitted again: follows resize
                Check.Close(2, box.Zoom);
                Check.True(changes >= 5);
            });
        }

        public void Control_PaintsCrispPixels()
        {
            RunSta(() =>
            {
                // 2x1 image (red | blue) shown at 50x: nearest-neighbour keeps a hard edge in the middle.
                using var image = new Bitmap(2, 1);
                image.SetPixel(0, 0, Color.Red);
                image.SetPixel(1, 0, Color.Blue);
                using var box = new PlotPictureBox { Size = new Size(100, 50) };
                box.Image = image;
                using var shot = new Bitmap(100, 50);
                box.DrawToBitmap(shot, new Rectangle(0, 0, 100, 50));
                Check.Equal(Color.Red.ToArgb(), shot.GetPixel(10, 25).ToArgb());
                Check.Equal(Color.Red.ToArgb(), shot.GetPixel(48, 25).ToArgb());
                Check.Equal(Color.Blue.ToArgb(), shot.GetPixel(52, 25).ToArgb());
                Check.Equal(Color.Blue.ToArgb(), shot.GetPixel(90, 25).ToArgb());
            });
        }

        private static void RunSta(Action body)
        {
            Exception? error = null;
            var t = new Thread(() => { try { body(); } catch (Exception ex) { error = ex; } }) { IsBackground = true };
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join();
            if (error is not null) throw error;
        }
    }
}
