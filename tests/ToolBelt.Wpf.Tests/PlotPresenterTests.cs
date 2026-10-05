using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ToolBelt.Tests.Framework;
using ToolBelt.Wpf;

namespace ToolBelt.Wpf.Tests
{
    public sealed class PlotPresenterTests
    {
        private static BitmapSource Bitmap(int w, int h) => PlotPresenter.BitmapFromRgba(w, h, new byte[w * h * 4]);

        private static PlotPresenter Laid(ImageSource src, double w, double h)
        {
            var p = new PlotPresenter { Source = src };
            p.Measure(new Size(w, h));
            p.Arrange(new Rect(0, 0, w, h));
            return p;
        }

        public void BitmapFromRgba_ConvertsChannelOrderAndPremultiplies()
        {
            FileDropBehaviorTests.RunSta(() =>
            {
                byte[] rgba = { 255, 0, 0, 255,   0, 0, 255, 255,   200, 100, 50, 128 };
                BitmapSource bmp = PlotPresenter.BitmapFromRgba(3, 1, rgba);
                Check.True(bmp.IsFrozen);
                Check.Equal(3, bmp.PixelWidth);
                var px = new byte[12];
                bmp.CopyPixels(px, 12, 0);
                Check.Equal("0,0,255,255", string.Join(",", px[0], px[1], px[2], px[3]));      // red as BGRA
                Check.Equal("255,0,0,255", string.Join(",", px[4], px[5], px[6], px[7]));      // blue as BGRA
                Check.Equal("25,50,100,128", string.Join(",", px[8], px[9], px[10], px[11]));  // premultiplied by 128/255
                Check.Throws<ArgumentException>(() => PlotPresenter.BitmapFromRgba(2, 2, new byte[4]));
            });
        }

        public void FitMatrix_ScalesUniformlyAndCentres()
        {
            Matrix m = PlotPresenter.FitMatrix(new Size(200, 100), new Size(100, 100));
            Check.Close(0.5, m.M11);
            Check.Close(0.5, m.M22);
            Check.Close(0, m.OffsetX);
            Check.Close(25, m.OffsetY);
            Check.Equal(Matrix.Identity, PlotPresenter.FitMatrix(new Size(0, 10), new Size(10, 10)));
        }

        public void ZoomAbout_KeepsThePointFixedAndClamps()
        {
            var view = new Matrix(2, 0, 0, 2, 30, -10);
            var at = new Point(120, 80);
            Point before;
            Matrix inv = view; inv.Invert(); before = inv.Transform(at);

            Matrix zoomed = PlotPresenter.ZoomAbout(view, at, 1.5);
            Check.Close(3, zoomed.M11);
            Point mapped = zoomed.Transform(before);
            Check.Close(at.X, mapped.X, 1e-9);
            Check.Close(at.Y, mapped.Y, 1e-9);

            Check.Close(4, PlotPresenter.ZoomAbout(view, at, 100, 0.1, 4).M11);              // clamped high
            Check.Close(0.5, PlotPresenter.ZoomAbout(view, at, 0.0001, 0.5, 4).M11);         // clamped low
            Check.Throws<ArgumentOutOfRangeException>(() => PlotPresenter.ZoomAbout(view, at, 0));
        }

        public void Control_FitsOnLayoutAndConvertsCoordinates()
        {
            FileDropBehaviorTests.RunSta(() =>
            {
                PlotPresenter p = Laid(Bitmap(200, 100), 400, 300);
                Check.Close(2, p.Zoom);                                    // min(400/200, 300/100)
                Check.Close(0, p.ViewTransform.OffsetX);
                Check.Close(50, p.ViewTransform.OffsetY);                  // (300 − 200) / 2
                Point c = p.ViewToContent(new Point(200, 150));
                Check.Close(100, c.X);
                Check.Close(50, c.Y);                                      // the view centre shows the figure centre
                Check.Close(200, p.ContentToView(c).X);
            });
        }

        public void Control_ZoomPanResetAndViewChanged()
        {
            FileDropBehaviorTests.RunSta(() =>
            {
                PlotPresenter p = Laid(Bitmap(100, 100), 100, 100);
                int changes = 0;
                p.ViewChanged += (_, __) => changes++;
                Point under = p.ViewToContent(new Point(30, 40));
                p.ZoomAt(new Point(30, 40), 2);
                Check.Close(2, p.Zoom);
                Point still = p.ViewToContent(new Point(30, 40));
                Check.Close(under.X, still.X, 1e-9);
                Check.Close(under.Y, still.Y, 1e-9);

                p.PanBy(new Vector(10, -5));
                Check.Close(under.X - 10 / 2.0, p.ViewToContent(new Point(30, 40)).X, 1e-9);

                p.ResetView();
                Check.Equal(Matrix.Identity, p.ViewTransform);
                p.FitToView();
                Check.Close(1, p.Zoom);
                Check.Equal(4, changes);

                p.MaxZoom = 3;
                p.ZoomAt(new Point(0, 0), 10);
                Check.Close(3, p.Zoom);
            });
        }

        public void Control_RendersWithoutError()
        {
            FileDropBehaviorTests.RunSta(() =>
            {
                PlotPresenter p = Laid(Bitmap(50, 40), 120, 90);
                var target = new RenderTargetBitmap(120, 90, 96, 96, PixelFormats.Pbgra32);
                target.Render(p);                                          // exercises OnRender
                Check.Equal(120, target.PixelWidth);
                p.Smooth = true;
                Check.Equal(BitmapScalingMode.HighQuality, RenderOptions.GetBitmapScalingMode(p));
            });
        }
    }
}
