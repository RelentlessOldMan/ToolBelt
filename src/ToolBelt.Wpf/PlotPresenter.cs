// ToolBelt.Wpf drop-in — Windows-only (net8.0-windows), self-contained (BCL + WPF).
using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ToolBelt.Wpf
{
    /// <summary>
    /// Displays a figure — any <see cref="ImageSource"/>: a <see cref="BitmapSource"/> for raster plots, a
    /// <see cref="DrawingImage"/> for vector content — with zoom and pan: the mouse wheel zooms about the cursor, a
    /// left-button drag pans, a double-click fits the figure to the view. The figure is fitted automatically when the
    /// source or the control's size first becomes known. With <see cref="Smooth"/> off (the default) bitmaps are
    /// scaled nearest-neighbour, so individual cells of a heat map or waterfall stay crisp when zoomed.
    /// <para>
    /// Template-free (a <see cref="FrameworkElement"/> that draws itself), so it needs no theme resources. The view is
    /// a plain <see cref="Matrix"/> (<see cref="ViewTransform"/>) mapping figure coordinates to control coordinates;
    /// <see cref="ContentToView"/>/<see cref="ViewToContent"/> convert for overlays or a cursor readout, and the zoom
    /// math is exposed as pure functions (<see cref="ZoomAbout"/>, <see cref="FitMatrix"/>).
    /// <see cref="BitmapFromRgba"/> turns raw RGBA pixels (e.g. a portable renderer's output) into a bitmap.
    /// </para>
    /// </summary>
    public sealed class PlotPresenter : FrameworkElement
    {
        public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
            nameof(Source), typeof(ImageSource), typeof(PlotPresenter),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, (d, e) => ((PlotPresenter)d)._fitPending = true));

        public static readonly DependencyProperty SmoothProperty = DependencyProperty.Register(
            nameof(Smooth), typeof(bool), typeof(PlotPresenter),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender, (d, e) => ((PlotPresenter)d).ApplyScalingMode()));

        public static readonly DependencyProperty BackgroundProperty = DependencyProperty.Register(
            nameof(Background), typeof(Brush), typeof(PlotPresenter),
            new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

        private Matrix _view = Matrix.Identity;
        private bool _fitPending = true;
        private Point? _dragFrom;

        public PlotPresenter()
        {
            ClipToBounds = true;
            Focusable = true;
            ApplyScalingMode();
        }

        public ImageSource? Source
        {
            get => (ImageSource?)GetValue(SourceProperty);
            set => SetValue(SourceProperty, value);
        }

        /// <summary>Smooth (linear) scaling for bitmaps; false keeps pixels crisp. Default false.</summary>
        public bool Smooth
        {
            get => (bool)GetValue(SmoothProperty);
            set => SetValue(SmoothProperty, value);
        }

        /// <summary>Fills the area around the figure (also makes it hit-testable for mouse input).</summary>
        public Brush? Background
        {
            get => (Brush?)GetValue(BackgroundProperty);
            set => SetValue(BackgroundProperty, value);
        }

        public double MinZoom { get; set; } = 0.02;
        public double MaxZoom { get; set; } = 64;

        /// <summary>Zoom multiplier per mouse-wheel notch.</summary>
        public double WheelFactor { get; set; } = 1.2;

        /// <summary>Figure → control coordinates.</summary>
        public Matrix ViewTransform => _view;

        /// <summary>The current scale (1 = one figure unit per device-independent pixel).</summary>
        public double Zoom => _view.M11;

        /// <summary>Raised whenever the view (zoom or pan) changes.</summary>
        public event EventHandler? ViewChanged;

        public Point ContentToView(Point content) => _view.Transform(content);

        public Point ViewToContent(Point view)
        {
            Matrix inverse = _view;
            inverse.Invert();
            return inverse.Transform(view);
        }

        /// <summary>Zooms by <paramref name="factor"/> keeping <paramref name="viewPoint"/> fixed on screen.</summary>
        public void ZoomAt(Point viewPoint, double factor) => SetView(ZoomAbout(_view, viewPoint, factor, MinZoom, MaxZoom));

        /// <summary>Moves the figure by <paramref name="delta"/> device-independent pixels.</summary>
        public void PanBy(Vector delta)
        {
            Matrix m = _view;
            m.Translate(delta.X, delta.Y);
            SetView(m);
        }

        /// <summary>Scales the figure to fit the control, centred, preserving aspect ratio.</summary>
        public void FitToView()
        {
            _fitPending = false;
            ImageSource? src = Source;
            if (src is null || ActualWidth <= 0 || ActualHeight <= 0) { _fitPending = src != null; return; }
            Matrix fit = FitMatrix(new Size(src.Width, src.Height), new Size(ActualWidth, ActualHeight));
            double s = Clamp(fit.M11, MinZoom, MaxZoom);
            if (s != fit.M11) fit = FitAtScale(new Size(src.Width, src.Height), new Size(ActualWidth, ActualHeight), s);
            SetView(fit);
        }

        /// <summary>Shows the figure at 1:1 from the top-left corner.</summary>
        public void ResetView()
        {
            _fitPending = false;
            SetView(Matrix.Identity);
        }

        // ---------- pure view math ----------

        /// <summary>
        /// The view after zooming <paramref name="view"/> by <paramref name="factor"/> about <paramref name="viewPoint"/>,
        /// with the resulting scale clamped to [<paramref name="minZoom"/>, <paramref name="maxZoom"/>]. The figure point
        /// under <paramref name="viewPoint"/> stays under it.
        /// </summary>
        public static Matrix ZoomAbout(Matrix view, Point viewPoint, double factor, double minZoom = 0, double maxZoom = double.MaxValue)
        {
            if (!(factor > 0) || double.IsInfinity(factor)) throw new ArgumentOutOfRangeException(nameof(factor), factor, "Factor must be positive.");
            double current = view.M11;
            double target = Clamp(current * factor, minZoom, maxZoom);
            double f = target / current;
            Matrix m = view;
            m.ScaleAt(f, f, viewPoint.X, viewPoint.Y);
            return m;
        }

        /// <summary>The uniform scale-and-centre transform that fits <paramref name="content"/> inside <paramref name="viewport"/>.</summary>
        public static Matrix FitMatrix(Size content, Size viewport)
        {
            if (content.Width <= 0 || content.Height <= 0 || viewport.Width <= 0 || viewport.Height <= 0) return Matrix.Identity;
            double s = Math.Min(viewport.Width / content.Width, viewport.Height / content.Height);
            return FitAtScale(content, viewport, s);
        }

        /// <summary>
        /// A frozen 32-bit bitmap from row-major RGBA bytes (4 per pixel, not premultiplied) — the layout portable
        /// raster renderers produce. Alpha is premultiplied into the colour on the way, as WPF's Pbgra32 expects.
        /// </summary>
        public static BitmapSource BitmapFromRgba(int width, int height, byte[] rgba, double dpi = 96)
        {
            if (width < 1) throw new ArgumentOutOfRangeException(nameof(width), width, "Width must be positive.");
            if (height < 1) throw new ArgumentOutOfRangeException(nameof(height), height, "Height must be positive.");
            if (rgba is null) throw new ArgumentNullException(nameof(rgba));
            if (rgba.Length != width * height * 4) throw new ArgumentException("Expected width × height × 4 bytes.", nameof(rgba));
            var bgra = new byte[rgba.Length];
            for (int i = 0; i < rgba.Length; i += 4)
            {
                int a = rgba[i + 3];
                bgra[i] = (byte)((rgba[i + 2] * a + 127) / 255);
                bgra[i + 1] = (byte)((rgba[i + 1] * a + 127) / 255);
                bgra[i + 2] = (byte)((rgba[i] * a + 127) / 255);
                bgra[i + 3] = (byte)a;
            }
            BitmapSource bmp = BitmapSource.Create(width, height, dpi, dpi, PixelFormats.Pbgra32, null, bgra, width * 4);
            bmp.Freeze();
            return bmp;
        }

        // ---------- rendering & input ----------

        protected override void OnRender(DrawingContext dc)
        {
            dc.DrawRectangle(Background, null, new Rect(RenderSize));
            ImageSource? src = Source;
            if (src is null) return;
            if (_fitPending) FitToViewSilently();
            dc.PushTransform(new MatrixTransform(_view));
            dc.DrawImage(src, new Rect(0, 0, src.Width, src.Height));
            dc.Pop();
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
            if (_fitPending) FitToView();
        }

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            base.OnMouseWheel(e);
            if (Source is null || e.Delta == 0) return;
            ZoomAt(e.GetPosition(this), Math.Pow(WheelFactor, e.Delta / 120.0));
            e.Handled = true;
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            Focus();
            if (e.ClickCount == 2) { FitToView(); e.Handled = true; return; }
            _dragFrom = e.GetPosition(this);
            CaptureMouse();
            e.Handled = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_dragFrom is Point from && IsMouseCaptured)
            {
                Point now = e.GetPosition(this);
                PanBy(now - from);
                _dragFrom = now;
            }
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);
            _dragFrom = null;
            if (IsMouseCaptured) ReleaseMouseCapture();
        }

        private void FitToViewSilently()
        {
            // Called from OnRender: compute the fit without invalidating the visual we are drawing.
            ImageSource? src = Source;
            if (src is null || ActualWidth <= 0 || ActualHeight <= 0) return;
            _view = FitMatrix(new Size(src.Width, src.Height), new Size(ActualWidth, ActualHeight));
            _fitPending = false;
        }

        private void SetView(Matrix m)
        {
            _view = m;
            InvalidateVisual();
            ViewChanged?.Invoke(this, EventArgs.Empty);
        }

        private void ApplyScalingMode()
            => RenderOptions.SetBitmapScalingMode(this, Smooth ? BitmapScalingMode.HighQuality : BitmapScalingMode.NearestNeighbor);

        private static Matrix FitAtScale(Size content, Size viewport, double s)
        {
            var m = new Matrix(s, 0, 0, s, (viewport.Width - content.Width * s) / 2, (viewport.Height - content.Height * s) / 2);
            return m;
        }

        private static double Clamp(double v, double lo, double hi) => v < lo ? lo : (v > hi ? hi : v);
    }
}
