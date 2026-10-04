// ToolBelt.WinForms drop-in — Windows-only (net8.0-windows), self-contained (BCL + WinForms).
using System;
using System.Windows.Forms;

namespace ToolBelt.WinForms
{
    /// <summary>
    /// A scope that suspends a control's layout logic for the duration of a batch of changes, then resumes
    /// it (performing a layout pass) on dispose — avoids flicker and redundant re-layouts when adding or
    /// moving many child controls:
    /// <code>using (LayoutSuspender.Begin(panel)) { /* add lots of controls */ }</code>
    /// </summary>
    public sealed class LayoutSuspender : IDisposable
    {
        private readonly Control _control;
        private readonly bool _performLayoutOnResume;
        private bool _disposed;

        /// <summary>Suspends layout on <paramref name="control"/>.</summary>
        public LayoutSuspender(Control control, bool performLayoutOnResume = true)
        {
            _control = control ?? throw new ArgumentNullException(nameof(control));
            _performLayoutOnResume = performLayoutOnResume;
            _control.SuspendLayout();
        }

        /// <summary>Begins a layout-suspension scope.</summary>
        public static LayoutSuspender Begin(Control control, bool performLayoutOnResume = true)
            => new LayoutSuspender(control, performLayoutOnResume);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _control.ResumeLayout(_performLayoutOnResume);
        }
    }
}
