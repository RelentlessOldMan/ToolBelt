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
    /// source or the control's size first becomes known, and when a source of a different size replaces it; a
    /// same-size source (a live plot's next frame) keeps the current zoom and pan. With <see cref="Smooth"/> off (the default) bitmaps are
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
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender,
                (d, e) => ((PlotPresenter)d).OnSourceChanged((ImageSource?)e.OldValue, (ImageSource?)e.NewValue)));

        public static readonly DependencyProperty SmoothProperty = DependencyProperty.Register(
            nameof(Smooth), typeof(bool), typeof(PlotPresenter),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender, (d, e) => ((PlotPresenter)d).ApplyScalingMode()));

        public static readonly DependencyProperty BackgroundProperty = DependencyProperty.Register(
            nameof(Background), typeof(Brush), typeof(PlotPresenter),
            new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

        private Matrix _view = Matrix.Identity;
        private bool _fitPending = true;
        private Point? _dragFrom;
        private double _wheelFactor = 1.2;

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

        /// <summary>The smallest scale fitting and zooming allow: a positive finite number.</summary>
        public double MinZoom
        {
            get => _minZoom;
            set => _minZoom = value > 0 && !double.IsInfinity(value)
                ? value
                : throw new ArgumentOutOfRangeException(nameof(value), value, "MinZoom must be a positive finite number.");
        }

        /// <summary>The largest scale fitting and zooming allow: a positive finite number.</summary>
        public double MaxZoom
        {
            get => _maxZoom;
            set => _maxZoom = value > 0 && !double.IsInfinity(value)
                ? value
                : throw new ArgumentOutOfRangeException(nameof(value), value, "MaxZoom must be a positive finite number.");
        }

        private double _minZoom = 0.02, _maxZoom = 64;

        /// <summary>Zoom multiplier per mouse-wheel notch: a positive finite number (below 1 reverses the wheel).</summary>
        public double WheelFactor
        {
            get => _wheelFactor;
            set => _wheelFactor = value > 0 && !double.IsInfinity(value)
                ? value
                : throw new ArgumentOutOfRangeException(nameof(value), value, "WheelFactor must be a positive finite number.");
        }

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

        /// <summary>
        /// Moves the figure by <paramref name="delta"/> device-independent pixels (which must be finite). A pan whose
        /// offset would overflow to infinity is ignored.
        /// </summary>
        public void PanBy(Vector delta)
        {
            if (!double.IsFinite(delta.X) || !double.IsFinite(delta.Y))
                throw new ArgumentOutOfRangeException(nameof(delta), delta, "The pan must be finite.");   // NaN stuck in the view
            Matrix m = _view;
            m.Translate(delta.X, delta.Y);
            if (IsFinite(m)) SetView(m);
        }

        /// <summary>Scales the figure to fit the control, centred, preserving aspect ratio.</summary>
        public void FitToView()
        {
            _fitPending = false;
            ImageSource? src = Source;
            if (src is null || ActualWidth <= 0 || ActualHeight <= 0) { _fitPending = src != null; return; }
            SetView(ClampedFit(src));
        }

        // The fit matrix with its scale clamped to [MinZoom, MaxZoom] — shared by FitToView and the render-time auto-fit.
        private Matrix ClampedFit(ImageSource src)
        {
            Matrix fit = FitMatrix(new Size(src.Width, src.Height), new Size(ActualWidth, ActualHeight));
            double s = Clamp(fit.M11, MinZoom, MaxZoom);
            Matrix m = s != fit.M11 ? FitAtScale(new Size(src.Width, src.Height), new Size(ActualWidth, ActualHeight), s) : fit;
            return m.HasInverse ? m : _view;   // a 1e10-wide figure fitted to a scale ViewToContent could not invert
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
        /// under <paramref name="viewPoint"/> stays under it. A zoom that would leave the view non-invertible (WPF treats
        /// a scale below about 5e-8 as singular) or not finite returns <paramref name="view"/> unchanged.
        /// <paramref name="viewPoint"/> must be finite.
        /// </summary>
        public static Matrix ZoomAbout(Matrix view, Point viewPoint, double factor, double minZoom = 0, double maxZoom = double.MaxValue)
        {
            if (!(factor > 0) || double.IsInfinity(factor)) throw new ArgumentOutOfRangeException(nameof(factor), factor, "Factor must be positive.");
            if (!double.IsFinite(viewPoint.X) || !double.IsFinite(viewPoint.Y))
                throw new ArgumentOutOfRangeException(nameof(viewPoint), viewPoint, "The point must be finite.");   // a NaN offset still "had an inverse"
            double current = view.M11;
            double target = Clamp(current * factor, minZoom, maxZoom);
            double f = target / current;
            if (double.IsNaN(f) || double.IsInfinity(f)) return view;
            Matrix m = view;
            m.ScaleAt(f, f, viewPoint.X, viewPoint.Y);
            return m.HasInverse && IsFinite(m) ? m : view;   // ViewToContent and panning invert the view
        }

        private static bool IsFinite(Matrix m)
            => double.IsFinite(m.M11) && double.IsFinite(m.M12) && double.IsFinite(m.M21) && double.IsFinite(m.M22)
            && double.IsFinite(m.OffsetX) && double.IsFinite(m.OffsetY);

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
            if (!(dpi > 0) || double.IsInfinity(dpi)) throw new ArgumentOutOfRangeException(nameof(dpi), dpi, "DPI must be a positive finite number.");
            // In long: int arithmetic wrapped, so 32768² × 4 matched an empty array.
            if (rgba.Length != (long)width * height * 4) throw new ArgumentException("Expected width × height × 4 bytes.", nameof(rgba));
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

        // A new figure of the same size (a live plot's next frame) keeps the user's zoom and pan; a first or resized one is fitted.
        private void OnSourceChanged(ImageSource? old, ImageSource? now)
        {
            if (old is null || now is null || old.Width != now.Width || old.Height != now.Height) _fitPending = true;
        }

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            base.OnMouseWheel(e);
            if (Source is null || e.Delta == 0) return;
            double factor = Math.Pow(WheelFactor, e.Delta / 120.0);
            if (!(factor > 0) || double.IsInfinity(factor)) return;   // a huge delta can overflow or underflow the power
            ZoomAt(e.GetPosition(this), factor);
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
            // Called from OnRender: compute the fit without invalidating the visual we are drawing, and tell listeners
            // (overlays, cursor readouts) after the render pass rather than from inside it.
            ImageSource? src = Source;
            if (src is null || ActualWidth <= 0 || ActualHeight <= 0) return;
            _view = ClampedFit(src);
            _fitPending = false;
            Dispatcher.BeginInvoke(new Action(() => ViewChanged?.Invoke(this, EventArgs.Empty)), System.Windows.Threading.DispatcherPriority.Render);
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
