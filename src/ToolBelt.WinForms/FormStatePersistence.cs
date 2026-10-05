// ToolBelt.WinForms drop-in — Windows-only (net8.0-windows), self-contained (BCL + WinForms).
using System;
using System.Collections.Generic;
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
            if (w <= 0 || h <= 0) // a real form always has a positive size; reject 0×0 placements
                return false;
            state = new FormState(new Rectangle(x, y, w, h), (FormWindowState)ws);
            return true;
        }

        public override string ToString() => Serialize();
    }

    /// <summary>
    /// Captures and restores a <see cref="Form"/>'s window placement. <see cref="Capture"/> records the
    /// form's <em>restored</em> bounds (so a maximized form still reopens at a sensible size) and whether it
    /// was maximized; <see cref="Apply(Form, FormState)"/> puts a form back to that placement, first validating
    /// it against the monitors attached <em>now</em> — a placement saved on a since-disconnected or rearranged
    /// display is moved (and if necessary shrunk) onto a current one, so a window can never reopen off-screen.
    /// The validation itself is the pure <see cref="EnsureOnScreen"/>, usable and testable on its own.
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

        /// <summary>
        /// Applies a saved placement to <paramref name="form"/> (sets manual start position), after moving it onto
        /// a currently attached monitor if it would otherwise be unreachable. See <see cref="EnsureOnScreen"/>.
        /// </summary>
        public static void Apply(Form form, FormState state)
            => Apply(form, state, CurrentWorkingAreas());

        /// <summary>
        /// Applies a saved placement, validating it against the given monitor <paramref name="workingAreas"/>
        /// (desktop areas excluding taskbars, primary first). Exposed so placement can be tested or computed
        /// against a known display layout.
        /// </summary>
        public static void Apply(Form form, FormState state, IReadOnlyList<Rectangle> workingAreas)
        {
            if (form is null) throw new ArgumentNullException(nameof(form));
            if (workingAreas is null) throw new ArgumentNullException(nameof(workingAreas));
            form.StartPosition = FormStartPosition.Manual;
            form.WindowState = FormWindowState.Normal;
            if (!state.Bounds.IsEmpty)
                form.Bounds = EnsureOnScreen(state.Bounds, workingAreas);
            form.WindowState = state.WindowState;
        }

        /// <summary>
        /// Returns <paramref name="bounds"/> unchanged if its title-bar strip is reachable on one of the
        /// <paramref name="workingAreas"/> (at least <paramref name="minimumVisible"/> pixels of it, or its whole
        /// width if narrower) — a window deliberately straddling two monitors is left alone. Otherwise the window
        /// is relocated onto the working area it overlaps most (or, if it overlaps none, the one nearest its
        /// centre; ties go to the earliest, so pass the primary first), shrunk to fit if it is larger than that
        /// area, and clamped inside it. With no working areas the bounds are returned unchanged.
        /// </summary>
        public static Rectangle EnsureOnScreen(Rectangle bounds, IReadOnlyList<Rectangle> workingAreas, int minimumVisible = 50)
        {
            if (workingAreas is null) throw new ArgumentNullException(nameof(workingAreas));
            if (minimumVisible < 1) throw new ArgumentOutOfRangeException(nameof(minimumVisible), minimumVisible, "Must be positive.");
            if (workingAreas.Count == 0 || bounds.Width <= 0 || bounds.Height <= 0)
                return bounds;

            // The caption is what the user grabs to move a window, so that is the part that must be reachable.
            var caption = new Rectangle(bounds.X, bounds.Y, bounds.Width, Math.Min(bounds.Height, CaptionHeight));
            int needed = Math.Min(minimumVisible, bounds.Width);
            foreach (Rectangle area in workingAreas)
            {
                Rectangle seen = Rectangle.Intersect(caption, area);
                if (seen.Width >= needed && seen.Height > 0)
                    return bounds;
            }

            Rectangle target = PickTarget(bounds, workingAreas);
            int w = Math.Min(bounds.Width, target.Width);
            int h = Math.Min(bounds.Height, target.Height);
            int x = Clamp(bounds.X, target.Left, target.Right - w);
            int y = Clamp(bounds.Y, target.Top, target.Bottom - h);
            return new Rectangle(x, y, w, h);
        }

        // Approximate caption-bar height; exact value varies with DPI/theme but only gates "reachable".
        private const int CaptionHeight = 30;

        private static Rectangle PickTarget(Rectangle bounds, IReadOnlyList<Rectangle> areas)
        {
            int bestIndex = 0;
            long bestOverlap = -1;
            for (int i = 0; i < areas.Count; i++)
            {
                Rectangle o = Rectangle.Intersect(bounds, areas[i]);
                long overlap = (long)o.Width * o.Height;
                if (overlap > bestOverlap) { bestOverlap = overlap; bestIndex = i; }
            }
            if (bestOverlap > 0)
                return areas[bestIndex];

            // No overlap at all: nearest by centre-to-centre distance.
            double cx = bounds.X + bounds.Width / 2.0, cy = bounds.Y + bounds.Height / 2.0;
            double bestDist = double.MaxValue;
            for (int i = 0; i < areas.Count; i++)
            {
                Rectangle a = areas[i];
                double dx = a.X + a.Width / 2.0 - cx, dy = a.Y + a.Height / 2.0 - cy;
                double dist = dx * dx + dy * dy;
                if (dist < bestDist) { bestDist = dist; bestIndex = i; }
            }
            return areas[bestIndex];
        }

        private static int Clamp(int value, int min, int max) => value < min ? min : (value > max ? max : value);

        private static IReadOnlyList<Rectangle> CurrentWorkingAreas()
        {
            Screen[] screens = Screen.AllScreens;
            var areas = new List<Rectangle>(screens.Length);
            foreach (Screen s in screens)
                if (s.Primary) areas.Insert(0, s.WorkingArea); // primary first: it wins ties
                else areas.Add(s.WorkingArea);
            return areas;
        }
    }
}
