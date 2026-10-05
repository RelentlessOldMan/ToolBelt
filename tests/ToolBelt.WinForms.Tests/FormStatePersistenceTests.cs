using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using ToolBelt.Tests.Framework;
using ToolBelt.WinForms;

namespace ToolBelt.WinForms.Tests
{
    public sealed class FormStatePersistenceTests
    {
        // Serialize/TryParse are pure — no Form, no UI thread needed.

        public void Serialize_Parse_RoundTrips()
        {
            var state = new FormState(new Rectangle(100, 120, 640, 480), FormWindowState.Maximized);
            string text = state.Serialize();
            Check.True(FormState.TryParse(text, out var parsed), "parses");
            Check.Equal(state.Bounds, parsed.Bounds);
            Check.Equal(state.WindowState, parsed.WindowState);
        }

        public void TryParse_RejectsMalformed()
        {
            Check.False(FormState.TryParse(null, out _));
            Check.False(FormState.TryParse("", out _));
            Check.False(FormState.TryParse("1,2,3", out _));            // too few parts
            Check.False(FormState.TryParse("a,b,c,d,e", out _));        // non-numeric
            Check.False(FormState.TryParse("0,0,100,100,7", out _));    // invalid window state
            Check.False(FormState.TryParse("0,0,-5,100,0", out _));     // negative size
            Check.False(FormState.TryParse("100,100,0,0,0", out _));    // zero size (no real form is 0x0)
        }

        public void TryParse_UsesInvariantCulture()
        {
            // Must parse regardless of the ambient culture (no comma-decimal confusion).
            var previous = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
                var state = new FormState(new Rectangle(10, 20, 300, 200), FormWindowState.Normal);
                Check.True(FormState.TryParse(state.Serialize(), out var parsed));
                Check.Equal(state.Bounds, parsed.Bounds);
            }
            finally { Thread.CurrentThread.CurrentCulture = previous; }
        }

        // Capture/Apply touch a real Form; run them on an STA thread.

        public void CaptureThenApply_RestoresBounds()
        {
            RunSta(() =>
            {
                using var source = new Form { StartPosition = FormStartPosition.Manual, Bounds = new Rectangle(50, 60, 500, 350) };
                FormState captured = FormStatePersistence.Capture(source);
                Check.Equal(new Rectangle(50, 60, 500, 350), captured.Bounds);
                Check.Equal(FormWindowState.Normal, captured.WindowState);

                using var target = new Form();
                FormStatePersistence.Apply(target, captured, TwoMonitors);
                Check.Equal(new Rectangle(50, 60, 500, 350), target.Bounds);
                Check.Equal(FormWindowState.Normal, target.WindowState);
            });
        }

        public void Apply_RelocatesPlacementFromMissingMonitor()
        {
            RunSta(() =>
            {
                // Saved on a third monitor that is no longer attached.
                var saved = new FormState(new Rectangle(4000, 100, 600, 400), FormWindowState.Normal);
                using var form = new Form();
                FormStatePersistence.Apply(form, saved, TwoMonitors);
                Check.Equal(new Rectangle(2600, 100, 600, 400), form.Bounds);
            });
        }

        public void Apply_DefaultUsesLiveScreens_AndLandsOnOne()
        {
            RunSta(() =>
            {
                var saved = new FormState(new Rectangle(-30000, -30000, 400, 300), FormWindowState.Normal);
                using var form = new Form();
                FormStatePersistence.Apply(form, saved);
                bool onSomeScreen = false;
                foreach (Screen screen in Screen.AllScreens)
                    if (screen.WorkingArea.IntersectsWith(form.Bounds)) onSomeScreen = true;
                Check.True(onSomeScreen, "a far-off-screen placement must be pulled onto a live monitor");
            });
        }

        // ---------- EnsureOnScreen (pure; fixed layout) ----------

        // Primary 1920x1040 at the origin; secondary 1280x984 to its right.
        private static readonly IReadOnlyList<Rectangle> TwoMonitors = new[]
        {
            new Rectangle(0, 0, 1920, 1040),
            new Rectangle(1920, 0, 1280, 984),
        };

        public void EnsureOnScreen_VisiblePlacement_Unchanged()
        {
            var r = new Rectangle(100, 100, 800, 600);
            Check.Equal(r, FormStatePersistence.EnsureOnScreen(r, TwoMonitors));
        }

        public void EnsureOnScreen_StraddlingMonitors_Unchanged()
        {
            var r = new Rectangle(1800, 50, 400, 300); // spans the seam — a deliberate user choice
            Check.Equal(r, FormStatePersistence.EnsureOnScreen(r, TwoMonitors));
        }

        public void EnsureOnScreen_MissingMonitor_MovesToNearestAndClamps()
        {
            // No overlap with anything; the secondary's centre is nearest. Clamped so its right edge fits.
            var r = new Rectangle(4000, 100, 600, 400);
            Check.Equal(new Rectangle(2600, 100, 600, 400), FormStatePersistence.EnsureOnScreen(r, TwoMonitors));
        }

        public void EnsureOnScreen_CaptionMostlyOffLeftEdge_PulledBack()
        {
            // Only 40px of the caption visible (< 50) — not reliably grabbable.
            var r = new Rectangle(-460, 200, 500, 300);
            Check.Equal(new Rectangle(0, 200, 500, 300), FormStatePersistence.EnsureOnScreen(r, TwoMonitors));
            // 60px visible is enough and is left alone.
            var ok = new Rectangle(-440, 200, 500, 300);
            Check.Equal(ok, FormStatePersistence.EnsureOnScreen(ok, TwoMonitors));
        }

        public void EnsureOnScreen_CaptionAboveTop_PulledDown()
        {
            // The body is visible but the title bar is above the screen: unmovable without the keyboard.
            var r = new Rectangle(300, -200, 600, 700);
            Check.Equal(new Rectangle(300, 0, 600, 700), FormStatePersistence.EnsureOnScreen(r, TwoMonitors));
        }

        public void EnsureOnScreen_PrefersAreaWithMostOverlap()
        {
            // Caption above the top of both monitors, body mostly on the secondary.
            var r = new Rectangle(1900, -100, 800, 400);
            Rectangle fixedUp = FormStatePersistence.EnsureOnScreen(r, TwoMonitors);
            Check.Equal(new Rectangle(1920, 0, 800, 400), fixedUp);
        }

        public void EnsureOnScreen_OversizedOffScreen_ShrinksToFit()
        {
            var primaryOnly = new[] { new Rectangle(0, 0, 1920, 1040) };
            var r = new Rectangle(5000, 5000, 3000, 2000);
            Check.Equal(new Rectangle(0, 0, 1920, 1040), FormStatePersistence.EnsureOnScreen(r, primaryOnly));
        }

        public void EnsureOnScreen_NarrowWindow_NeedsOnlyItsWidth()
        {
            // A 30px-wide window can't show 50px of caption; its full width visible is enough.
            var r = new Rectangle(10, 10, 30, 200);
            Check.Equal(r, FormStatePersistence.EnsureOnScreen(r, TwoMonitors));
        }

        public void EnsureOnScreen_NoWorkingAreas_Unchanged()
        {
            var r = new Rectangle(-9999, -9999, 400, 300);
            Check.Equal(r, FormStatePersistence.EnsureOnScreen(r, Array.Empty<Rectangle>()));
        }

        public void EnsureOnScreen_Validation()
        {
            Check.Throws<ArgumentNullException>(() => FormStatePersistence.EnsureOnScreen(Rectangle.Empty, null!));
            Check.Throws<ArgumentOutOfRangeException>(() => FormStatePersistence.EnsureOnScreen(new Rectangle(0, 0, 10, 10), TwoMonitors, 0));
            using var form = new Form();
            Check.Throws<ArgumentNullException>(() => FormStatePersistence.Apply(form, default, null!));
        }

        public void Capture_NullForm_Throws()
        {
            Check.Throws<ArgumentNullException>(() => FormStatePersistence.Capture(null!));
            Check.Throws<ArgumentNullException>(() => FormStatePersistence.Apply(null!, default));
        }

        private static void RunSta(Action body)
        {
            Exception? error = null;
            var t = new Thread(() =>
            {
                try { body(); }
                catch (Exception ex) { error = ex; }
            }) { IsBackground = true };
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join();
            if (error is not null) throw error;
        }
    }
}
