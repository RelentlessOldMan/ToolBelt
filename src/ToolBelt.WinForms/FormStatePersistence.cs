// ToolBelt.WinForms drop-in — Windows-only (net8.0-windows), self-contained (BCL + WinForms).
using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace ToolBelt.WinForms
{
    /// <summary>
    /// A form's saved placement: its normal (restored) bounds and whether it was maximized. Serializes to a
    /// compact, culture-invariant string you can persist anywhere (settings file, registry, etc.).
    /// </summary>
    public readonly struct FormState
    {
        public FormState(Rectangle bounds, FormWindowState windowState)
        {
            Bounds = bounds;
            WindowState = windowState;
        }

        /// <summary>The form's normal (non-maximized) bounds.</summary>
        public Rectangle Bounds { get; }

        /// <summary>The window state to restore (Normal or Maximized; Minimized is normalised to Normal).</summary>
        public FormWindowState WindowState { get; }

        /// <summary>Serializes to <c>"x,y,w,h,state"</c> using the invariant culture.</summary>
        public string Serialize() => string.Format(
            CultureInfo.InvariantCulture, "{0},{1},{2},{3},{4}",
            Bounds.X, Bounds.Y, Bounds.Width, Bounds.Height, (int)WindowState);

        /// <summary>Parses a string produced by <see cref="Serialize"/>.</summary>
        public static bool TryParse(string? text, out FormState state)
        {
            state = default;
            if (string.IsNullOrEmpty(text))
                return false;
            string[] parts = text!.Split(',');
            if (parts.Length != 5)
                return false;
            var ci = CultureInfo.InvariantCulture;
            if (!int.TryParse(parts[0], NumberStyles.Integer, ci, out int x) ||
                !int.TryParse(parts[1], NumberStyles.Integer, ci, out int y) ||
                !int.TryParse(parts[2], NumberStyles.Integer, ci, out int w) ||
                !int.TryParse(parts[3], NumberStyles.Integer, ci, out int h) ||
                !int.TryParse(parts[4], NumberStyles.Integer, ci, out int ws))
                return false;
            if (ws != (int)FormWindowState.Normal && ws != (int)FormWindowState.Maximized)
                return false;
            if (w < 0 || h < 0)
                return false;
            state = new FormState(new Rectangle(x, y, w, h), (FormWindowState)ws);
            return true;
        }

        public override string ToString() => Serialize();
    }

    /// <summary>
    /// Captures and restores a <see cref="Form"/>'s window placement. <see cref="Capture"/> records the
    /// form's <em>restored</em> bounds (so a maximized form still reopens at a sensible size) and whether it
    /// was maximized; <see cref="Apply"/> puts a form back to that placement.
    /// </summary>
    public static class FormStatePersistence
    {
        /// <summary>Captures the current placement of <paramref name="form"/>.</summary>
        public static FormState Capture(Form form)
        {
            if (form is null) throw new ArgumentNullException(nameof(form));
            Rectangle bounds = form.WindowState == FormWindowState.Normal ? form.Bounds : form.RestoreBounds;
            // Never persist "minimized" — restore to Normal in that case.
            FormWindowState ws = form.WindowState == FormWindowState.Maximized
                ? FormWindowState.Maximized
                : FormWindowState.Normal;
            return new FormState(bounds, ws);
        }

        /// <summary>Applies a saved placement to <paramref name="form"/> (sets manual start position).</summary>
        public static void Apply(Form form, FormState state)
        {
            if (form is null) throw new ArgumentNullException(nameof(form));
            form.StartPosition = FormStartPosition.Manual;
            form.WindowState = FormWindowState.Normal;
            if (!state.Bounds.IsEmpty)
                form.Bounds = state.Bounds;
            form.WindowState = state.WindowState;
        }
    }
}
