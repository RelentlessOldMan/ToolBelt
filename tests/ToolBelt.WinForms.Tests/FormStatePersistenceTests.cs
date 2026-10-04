using System;
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
                FormStatePersistence.Apply(target, captured);
                Check.Equal(new Rectangle(50, 60, 500, 350), target.Bounds);
                Check.Equal(FormWindowState.Normal, target.WindowState);
            });
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
