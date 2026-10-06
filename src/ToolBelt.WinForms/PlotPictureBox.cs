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

        /// <summary>The figure (not owned: the caller disposes it).</summary>
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
        public double MinZoom { get; set; } = 0.02;
        public double MaxZoom { get; set; } = 64;
        public double WheelFactor { get; set; } = 1.2;

        /// <summary>Raised whenever zoom or pan changes.</summary>
        public event EventHandler? ViewChanged;

        public PointF ContentToView(PointF p) => new PointF((float)(p.X * Zoom + Offset.X), (float)(p.Y * Zoom + Offset.Y));

        public PointF ViewToContent(PointF p) => new PointF((float)((p.X - Offset.X) / Zoom), (float)((p.Y - Offset.Y) / Zoom));

        /// <summary>Zooms by <paramref name="factor"/> keeping <paramref name="viewPoint"/> fixed.</summary>
        public void ZoomAt(PointF viewPoint, double factor)
        {
            var (zoom, offset) = ZoomAbout(Zoom, Offset, viewPoint, factor, MinZoom, MaxZoom);
            _userAdjusted = true;
            SetView(zoom, offset);
        }

        public void PanBy(float dx, float dy)
        {
            _userAdjusted = true;
            SetView(Zoom, new PointF(Offset.X + dx, Offset.Y + dy));
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

        /// <summary>1:1 at the top-left corner.</summary>
        public void ResetView()
        {
            _userAdjusted = true;
            SetView(1, PointF.Empty);
        }

        // ---------- pure view math ----------

        /// <summary>The (zoom, offset) after zooming by <paramref name="factor"/> about <paramref name="at"/>, clamped to [min, max].</summary>
        public static (double Zoom, PointF Offset) ZoomAbout(double zoom, PointF offset, PointF at, double factor, double minZoom = 0, double maxZoom = double.MaxValue)
        {
            if (!(zoom > 0)) throw new ArgumentOutOfRangeException(nameof(zoom), zoom, "Zoom must be positive.");
            if (!(factor > 0) || double.IsInfinity(factor)) throw new ArgumentOutOfRangeException(nameof(factor), factor, "Factor must be positive.");
            double target = Math.Max(minZoom, Math.Min(maxZoom, zoom * factor));
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
            if (rgba.Length != width * height * 4) throw new ArgumentException("Expected width × height × 4 bytes.", nameof(rgba));
            var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
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
            return bmp;
        }

        // ---------- painting & input ----------

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (_image is null) return;
            Graphics g = e.Graphics;
            g.InterpolationMode = _smooth ? InterpolationMode.HighQualityBicubic : InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = _smooth ? PixelOffsetMode.HighQuality : PixelOffsetMode.Half; // Half: no half-pixel shift
            g.DrawImage(_image, new RectangleF(Offset.X, Offset.Y, (float)(_image.Width * Zoom), (float)(_image.Height * Zoom)));
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (!_userAdjusted) FitToView();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (_image is null || e.Delta == 0) return;
            ZoomAt(e.Location, Math.Pow(WheelFactor, e.Delta / 120.0));
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
