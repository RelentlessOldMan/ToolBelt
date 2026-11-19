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
            WindowState = windowState == FormWindowState.Maximized ? FormWindowState.Maximized : FormWindowState.Normal;
        }

        /// <summary>The form's normal (non-maximized) bounds.</summary>
        public Rectangle Bounds { get; }

        /// <summary>The window state to restore: Normal or Maximized (anything else given to the constructor becomes Normal).</summary>
        public FormWindowState WindowState { get; }

        /// <summary>Serializes to <c>"x,y,w,h,state"</c> using the invariant culture.</summary>
        public string Serialize() => string.Format(
            CultureInfo.InvariantCulture, "{0},{1},{2},{3},{4}",
            Bounds.X, Bounds.Y, Bounds.Width, Bounds.Height, (int)WindowState);

        /// <summary>
        /// Parses a string produced by <see cref="Serialize"/>: exactly its form (no spaces, "+" signs or leading zeros), with
        /// a positive size whose right and bottom edges stay in <see cref="int"/> range.
        /// </summary>
        public static bool TryParse(string? text, out FormState state)
        {
            state = default;
            if (string.IsNullOrEmpty(text))
                return false;
            string[] parts = text!.Split(',');
            if (parts.Length != 5)
                return false;
            // As Serialize writes them: " 1" and "+2" were taken, and Serialize never writes either.
            if (!Canonical(parts[0], out int x) || !Canonical(parts[1], out int y) || !Canonical(parts[2], out int w) ||
                !Canonical(parts[3], out int h) || !Canonical(parts[4], out int ws))
                return false;
            if (ws != (int)FormWindowState.Normal && ws != (int)FormWindowState.Maximized)
                return false;
            if (w <= 0 || h <= 0) // a real form always has a positive size; reject 0×0 placements
                return false;
            // 2147483647,0,100,100 was taken, and its Right wrapped to -2147483549.
            if ((long)x + w > int.MaxValue || (long)y + h > int.MaxValue)
                return false;
            state = new FormState(new Rectangle(x, y, w, h), (FormWindowState)ws);
            return true;
        }

        private static bool Canonical(string s, out int value)
            => int.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value)
               && s == value.ToString(CultureInfo.InvariantCulture);

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
        /// <summary>
        /// Captures the current placement of <paramref name="form"/>. A minimized form is captured as Normal at its
        /// restored bounds — even one that was maximized before it was minimized.
        /// </summary>
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
        /// a currently attached monitor if it would otherwise be unreachable. See <see cref="EnsureOnScreen"/>. A state
        /// without a positive size (<c>default</c>, or what a failed <see cref="FormState.TryParse"/> gives) leaves the form
        /// as it is.
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
            // An empty state still set a manual start position and Normal: the out value of a failed TryParse pinned a
            // centred form to (0,0) and dropped a designer-set Maximized.
            if (state.Bounds.Width <= 0 || state.Bounds.Height <= 0)
                return;
            form.StartPosition = FormStartPosition.Manual;
            form.WindowState = FormWindowState.Normal;
            form.Bounds = EnsureOnScreen(state.Bounds, workingAreas);
            form.WindowState = state.WindowState;
        }

        /// <summary>
        /// Returns <paramref name="bounds"/> unchanged if it fits within the bounding box of all the
        /// <paramref name="workingAreas"/> and its title-bar strip is reachable on one of them (at least
        /// <paramref name="minimumVisible"/> pixels of it, or its whole width if narrower, and half the caption's height) — a window deliberately
        /// straddling two monitors is left alone. (A larger window would be shrunk by Windows about its own origin,
        /// which can move the reachable part of the caption off-screen.) Otherwise the window
        /// is relocated onto the working area it overlaps most (or, if it overlaps none, the one nearest its
        /// centre; ties go to the earliest, so pass the primary first), shrunk to fit if it is larger than that
        /// area, and clamped inside it. With no working areas the bounds are returned unchanged. Empty areas, and areas
        /// whose right or bottom edge would pass <see cref="int.MaxValue"/>, are ignored.
        /// </summary>
        public static Rectangle EnsureOnScreen(Rectangle bounds, IReadOnlyList<Rectangle> workingAreas, int minimumVisible = 50)
        {
            if (workingAreas is null) throw new ArgumentNullException(nameof(workingAreas));
            if (minimumVisible < 1) throw new ArgumentOutOfRangeException(nameof(minimumVisible), minimumVisible, "Must be positive.");
            // An empty area once won the "nearest" pick and the window came back 0×0 (or −100 wide); an area with an
            // overflowing Right clamped the window past it.
            var usable = new List<Rectangle>(workingAreas.Count);
            foreach (Rectangle area in workingAreas)
                if (area.Width > 0 && area.Height > 0 && (long)area.X + area.Width <= int.MaxValue && (long)area.Y + area.Height <= int.MaxValue)
                    usable.Add(area);
            workingAreas = usable;
            if (workingAreas.Count == 0 || bounds.Width <= 0 || bounds.Height <= 0)
                return bounds;

            // The caption is what the user grabs to move a window, so that is the part that must be reachable.
            var caption = new Rectangle(bounds.X, bounds.Y, bounds.Width, Math.Min(bounds.Height, CaptionHeight));
            int needed = Math.Min(minimumVisible, bounds.Width);
            int neededHeight = Math.Min(MinimumCaptionVisible, caption.Height); // a 1 px sliver of caption cannot be grabbed
            // The union's extent in long: Rectangle.Union's int width overflowed for areas far apart (negative, so a window
            // straddling two adjacent monitors was moved).
            long left = long.MaxValue, top = long.MaxValue, right = long.MinValue, bottom = long.MinValue;
            foreach (Rectangle area in workingAreas)
            {
                left = Math.Min(left, area.X); top = Math.Min(top, area.Y);
                right = Math.Max(right, (long)area.X + area.Width); bottom = Math.Max(bottom, (long)area.Y + area.Height);
            }
            if (bounds.Width <= right - left && bounds.Height <= bottom - top)
                foreach (Rectangle area in workingAreas)
                {
                    Rectangle seen = Rectangle.Intersect(caption, area);
                    if (seen.Width >= needed && seen.Height >= neededHeight)
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
        private const int MinimumCaptionVisible = CaptionHeight / 2;

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
