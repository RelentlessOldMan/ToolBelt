// ToolBelt.WinForms drop-in — Windows-only (net8.0-windows), self-contained (WinForms only).
using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace ToolBelt.WinForms
{
    /// <summary>
    /// A <see cref="Panel"/> for custom drawing that doesn't flicker: double-buffered with all painting in WM_PAINT
    /// (no separate background erase), user-painted, and repainted on resize so stretched content is never left stale.
    /// Handle <see cref="Control.Paint"/> or the <see cref="Render"/> callback; call <see cref="Control.Invalidate()"/>
    /// when the data changes. Optional <see cref="AntiAlias"/> sets high-quality smoothing on the paint graphics.
    /// </summary>
    public class DoubleBufferedPanel : Panel
    {
        public DoubleBufferedPanel()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            DoubleBuffered = true;
            UpdateStyles();
        }

        /// <summary>Drawing code run on every paint, after the background is filled (an alternative to handling Paint).</summary>
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Action<Graphics, Rectangle>? Render { get; set; }

        /// <summary>Smooth lines and text (default true).</summary>
        [DefaultValue(true)]
        public bool AntiAlias { get; set; } = true;

        /// <summary>The control styles that make it flicker-free (for tests and diagnostics).</summary>
        public bool IsFlickerFree => GetStyle(ControlStyles.OptimizedDoubleBuffer) && GetStyle(ControlStyles.AllPaintingInWmPaint)
                                     && GetStyle(ControlStyles.UserPaint) && GetStyle(ControlStyles.ResizeRedraw);

        protected override void OnPaint(PaintEventArgs e)
        {
            if (e is null) throw new ArgumentNullException(nameof(e));
            if (AntiAlias)
            {
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            }
            Render?.Invoke(e.Graphics, ClientRectangle);
            base.OnPaint(e);
        }
    }
}
