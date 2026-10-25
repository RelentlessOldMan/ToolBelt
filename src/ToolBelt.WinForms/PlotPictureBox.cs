// ToolBelt.WinForms drop-in — Windows-only (net8.0-windows), self-contained (BCL + WinForms).
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ToolBelt.WinForms
{
    /// <summary>
    /// A zoom-and-pan image view for figures — the WinForms twin of the WPF <c>PlotPresenter</c>. The mouse wheel zooms
    /// about the cursor, a left-button drag pans, a double-click fits the image to the control. The image is fitted
    /// when it is assigned and re-fitted on resize until the user zooms or pans. With <see cref="Smooth"/> off (the
    /// default) scaling is nearest-neighbour, so heat-map and waterfall cells stay crisp. The view is a uniform scale
    /// <see cref="Zoom"/> plus an <see cref="Offset"/> (view = image × zoom + offset); <see cref="ContentToView"/> and
    /// <see cref="ViewToContent"/> convert for overlays or a cursor readout. Double-buffered, so no flicker.
    /// </summary>
    public sealed class PlotPictureBox : Control
    {
        private Image? _image;
        private bool _smooth;
        private bool _userAdjusted;
        private Point? _dragFrom;

        public PlotPictureBox()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint
                | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            BackColor = Color.White;
        }

        /// <summary>
        /// The figure (not owned: the caller disposes it). Assign the next image (or null) before disposing the current
        /// one — the control keeps painting whatever is assigned, and a disposed image cannot be drawn.
        /// </summary>
        public Image? Image
        {
            get => _image;
            set
            {
                _image = value;
                _userAdjusted = false;
                FitToView();
            }
        }

        /// <summary>High-quality interpolation instead of crisp nearest-neighbour pixels.</summary>
        public bool Smooth
        {
            get => _smooth;
            set { _smooth = value; Invalidate(); }
        }

        public double Zoom { get; private set; } = 1;
        public PointF Offset { get; private set; }
        /// <summary>
        /// The smallest zoom the wheel, <see cref="ZoomAt"/>, <see cref="FitToView"/> and <see cref="ResetView"/> reach;
        /// positive and finite. Above <see cref="MaxZoom"/> it wins. Changing a limit applies from the next zoom.
        /// </summary>
        public double MinZoom { get => _minZoom; set => _minZoom = PositiveFinite(value); }

        /// <summary>The largest zoom the wheel, <see cref="ZoomAt"/>, <see cref="FitToView"/> and <see cref="ResetView"/> reach; positive and finite.</summary>
        public double MaxZoom { get => _maxZoom; set => _maxZoom = PositiveFinite(value); }

        /// <summary>Zoom per wheel notch; positive and finite (above 1 zooms in on wheel-up).</summary>
        public double WheelFactor { get => _wheelFactor; set => _wheelFactor = PositiveFinite(value); }

        private double _minZoom = 0.02, _maxZoom = 64, _wheelFactor = 1.2;

        private static double PositiveFinite(double value)
            => value > 0 && !double.IsInfinity(value) ? value : throw new ArgumentOutOfRangeException(nameof(value), value, "Must be positive and finite.");

        /// <summary>Raised whenever zoom or pan changes.</summary>
        public event EventHandler? ViewChanged;

        public PointF ContentToView(PointF p) => new PointF((float)(p.X * Zoom + Offset.X), (float)(p.Y * Zoom + Offset.Y));

        public PointF ViewToContent(PointF p) => new PointF((float)((p.X - Offset.X) / Zoom), (float)((p.Y - Offset.Y) / Zoom));

        /// <summary>Zooms by <paramref name="factor"/> keeping <paramref name="viewPoint"/> fixed.</summary>
        public void ZoomAt(PointF viewPoint, double factor)
        {
            if (!float.IsFinite(viewPoint.X) || !float.IsFinite(viewPoint.Y))
                throw new ArgumentOutOfRangeException(nameof(viewPoint), viewPoint, "Must be finite.");
            var (zoom, offset) = ZoomAbout(Zoom, Offset, viewPoint, factor, MinZoom, MaxZoom);
            _userAdjusted = true;
            SetView(zoom, offset);
        }

        public void PanBy(float dx, float dy)
        {
            if (!float.IsFinite(dx)) throw new ArgumentOutOfRangeException(nameof(dx), dx, "Must be finite.");
            if (!float.IsFinite(dy)) throw new ArgumentOutOfRangeException(nameof(dy), dy, "Must be finite.");
            var offset = new PointF(Offset.X + dx, Offset.Y + dy);
            if (!float.IsFinite(offset.X)) throw new ArgumentOutOfRangeException(nameof(dx), dx, "The offset would overflow.");
            if (!float.IsFinite(offset.Y)) throw new ArgumentOutOfRangeException(nameof(dy), dy, "The offset would overflow.");
            _userAdjusted = true;
            SetView(Zoom, offset);
        }

        /// <summary>Fits the image to the control, centred, preserving aspect ratio (and resumes auto-fit on resize).</summary>
        public void FitToView()
        {
            _userAdjusted = false;
            if (_image is null || ClientSize.Width <= 0 || ClientSize.Height <= 0) { Invalidate(); return; }
            var (zoom, offset) = Fit(_image.Size, ClientSize);
            double clamped = Math.Max(MinZoom, Math.Min(MaxZoom, zoom));
            if (clamped != zoom)
                offset = new PointF((float)((ClientSize.Width - _image.Width * clamped) / 2), (float)((ClientSize.Height - _image.Height * clamped) / 2));
            SetView(clamped, offset);
        }

        /// <summary>1:1 at the top-left corner (or the nearest zoom the limits allow).</summary>
        public void ResetView()
        {
            _userAdjusted = true;
            SetView(Math.Max(MinZoom, Math.Min(MaxZoom, 1)), PointF.Empty);
        }

        // ---------- pure view math ----------

        /// <summary>
        /// The (zoom, offset) after zooming by <paramref name="factor"/> about <paramref name="at"/>, clamped to [min, max].
        /// A zoom that would leave (0, ∞) — e.g. underflow with <paramref name="minZoom"/> 0 — leaves the view unchanged, and
        /// so does a clamp that would reverse the direction (zooming in on a view already above <paramref name="maxZoom"/>).
        /// </summary>
        public static (double Zoom, PointF Offset) ZoomAbout(double zoom, PointF offset, PointF at, double factor, double minZoom = 0, double maxZoom = double.MaxValue)
        {
            if (!(zoom > 0) || double.IsInfinity(zoom)) throw new ArgumentOutOfRangeException(nameof(zoom), zoom, "Zoom must be positive and finite.");
            if (!float.IsFinite(offset.X) || !float.IsFinite(offset.Y)) throw new ArgumentOutOfRangeException(nameof(offset), offset, "Must be finite.");
            if (!float.IsFinite(at.X) || !float.IsFinite(at.Y)) throw new ArgumentOutOfRangeException(nameof(at), at, "Must be finite.");
            if (!(factor > 0) || double.IsInfinity(factor)) throw new ArgumentOutOfRangeException(nameof(factor), factor, "Factor must be positive.");
            double target = Math.Max(minZoom, Math.Min(maxZoom, zoom * factor));
            if (!(target > 0) || double.IsInfinity(target)) return (zoom, offset);
            if (factor > 1 ? target < zoom : target > zoom) return (zoom, offset);
            double f = target / zoom;
            // The content point under `at` must stay under it: at = c·z + o = c·z' + o'.
            var o = new PointF((float)(at.X - (at.X - offset.X) * f), (float)(at.Y - (at.Y - offset.Y) * f));
            return (target, o);
        }

        /// <summary>The (zoom, offset) that fits <paramref name="content"/> inside <paramref name="viewport"/>, centred.</summary>
        public static (double Zoom, PointF Offset) Fit(Size content, Size viewport)
        {
            if (content.Width <= 0 || content.Height <= 0 || viewport.Width <= 0 || viewport.Height <= 0) return (1, PointF.Empty);
            double z = Math.Min((double)viewport.Width / content.Width, (double)viewport.Height / content.Height);
            return (z, new PointF((float)((viewport.Width - content.Width * z) / 2), (float)((viewport.Height - content.Height * z) / 2)));
        }

        /// <summary>A 32-bit ARGB <see cref="Bitmap"/> from row-major RGBA bytes (4 per pixel) — portable renderer output.</summary>
        public static Bitmap BitmapFromRgba(int width, int height, byte[] rgba)
        {
            if (width < 1) throw new ArgumentOutOfRangeException(nameof(width), width, "Width must be positive.");
            if (height < 1) throw new ArgumentOutOfRangeException(nameof(height), height, "Height must be positive.");
            if (rgba is null) throw new ArgumentNullException(nameof(rgba));
            if (rgba.LongLength != (long)width * height * 4) throw new ArgumentException("Expected width × height × 4 bytes.", nameof(rgba));
            var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            try
            {
                Fill(bmp, width, height, rgba);
            }
            catch
            {
                bmp.Dispose();
                throw;
            }
            return bmp;
        }

        private static void Fill(Bitmap bmp, int width, int height, byte[] rgba)
        {
            BitmapData data = bmp.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                var row = new byte[width * 4];
                for (int y = 0; y < height; y++)
                {
                    int src = y * width * 4;
                    for (int x = 0; x < width; x++)
                    {
                        int i = x * 4;
                        row[i] = rgba[src + i + 2];     // B
                        row[i + 1] = rgba[src + i + 1]; // G
                        row[i + 2] = rgba[src + i];     // R
                        row[i + 3] = rgba[src + i + 3]; // A
                    }
                    Marshal.Copy(row, 0, data.Scan0 + y * data.Stride, row.Length);
                }
            }
            finally { bmp.UnlockBits(data); }
        }

        // ---------- painting & input ----------

        protected override void OnPaint(PaintEventArgs e)
        {
            // The image first, then the Paint event, so handlers draw overlays (via ContentToView) on top of it.
            if (_image is not null) DrawImage(e, _image);
            base.OnPaint(e);
        }

        private void DrawImage(PaintEventArgs e, Image image)
        {
            Graphics g = e.Graphics;
            g.InterpolationMode = _smooth ? InterpolationMode.HighQualityBicubic : InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = _smooth ? PixelOffsetMode.HighQuality : PixelOffsetMode.Half; // Half: no half-pixel shift
            double x = Offset.X, y = Offset.Y, w = image.Width * Zoom, h = image.Height * Zoom;
            if (Math.Max(Math.Max(Math.Abs(x), Math.Abs(y)), Math.Max(Math.Abs(x + w), Math.Abs(y + h))) <= SafeCoordinate)
            {
                g.DrawImage(image, new RectangleF((float)x, (float)y, (float)w, (float)h));
                return;
            }
            // Far off the origin (deep zoom, long pan) GDI+ overflows on the full destination rectangle: draw only the
            // whole source pixels in view.
            Rectangle clip = e.ClipRectangle;
            double z = Zoom;
            int c0 = SourceIndex(Math.Floor((clip.Left - x) / z), image.Width), c1 = SourceIndex(Math.Ceiling((clip.Right - x) / z), image.Width);
            int r0 = SourceIndex(Math.Floor((clip.Top - y) / z), image.Height), r1 = SourceIndex(Math.Ceiling((clip.Bottom - y) / z), image.Height);
            if (c1 <= c0 || r1 <= r0) return;
            double dx = x + c0 * z, dy = y + r0 * z, dr = x + c1 * z, db = y + r1 * z;
            if (Math.Max(Math.Max(Math.Abs(dx), Math.Abs(dy)), Math.Max(Math.Abs(dr), Math.Abs(db))) <= SafeCoordinate)
            {
                g.DrawImage(image, new RectangleF((float)dx, (float)dy, (float)(dr - dx), (float)(db - dy)),
                    new RectangleF(c0, r0, c1 - c0, r1 - r0), GraphicsUnit.Pixel);
                return;
            }
            // Each source pixel spans millions of view pixels, so only a few are in view: fill their blocks.
            using var pixels = new Bitmap(c1 - c0, r1 - r0, PixelFormat.Format32bppArgb);
            using (var pg = Graphics.FromImage(pixels))
            {
                pg.CompositingMode = CompositingMode.SourceCopy;
                pg.DrawImage(image, new Rectangle(0, 0, c1 - c0, r1 - r0), c0, r0, c1 - c0, r1 - r0, GraphicsUnit.Pixel);
            }
            for (int j = 0; j < r1 - r0; j++)
                for (int i = 0; i < c1 - c0; i++)
                {
                    double l = Math.Max(x + (c0 + i) * z, clip.Left), t = Math.Max(y + (r0 + j) * z, clip.Top);
                    double r = Math.Min(x + (c0 + i + 1) * z, clip.Right), b = Math.Min(y + (r0 + j + 1) * z, clip.Bottom);
                    if (!(r > l && b > t)) continue;
                    using var brush = new SolidBrush(pixels.GetPixel(i, j));
                    g.FillRectangle(brush, (float)l, (float)t, (float)(r - l), (float)(b - t));
                }
        }

        private static int SourceIndex(double index, int size) => (int)Math.Max(0, Math.Min(size, index));

        private const double SafeCoordinate = 1 << 22;

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (!_userAdjusted) FitToView();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (_image is null || e.Delta == 0) return;
            double notches = Math.Max(-50, Math.Min(50, e.Delta / 120.0)); // a synthetic huge delta must not overflow the power
            double factor = Math.Pow(WheelFactor, notches);
            if (factor > 0 && !double.IsInfinity(factor)) ZoomAt(e.Location, factor);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            // Focus on click, not on hover: stealing focus while the pointer merely crosses the plot would interrupt typing
            // elsewhere. (Windows 10+ delivers the wheel to the window under the cursor anyway.)
            if (CanFocus) Focus();
            if (e.Button == MouseButtons.Left) { _dragFrom = e.Location; Capture = true; }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_dragFrom is Point from && e.Button == MouseButtons.Left)
            {
                PanBy(e.X - from.X, e.Y - from.Y);
                _dragFrom = e.Location;
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _dragFrom = null;
            Capture = false;
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (e.Button == MouseButtons.Left) FitToView();
        }

        private void SetView(double zoom, PointF offset)
        {
            Zoom = zoom;
            Offset = offset;
            Invalidate();
            ViewChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
