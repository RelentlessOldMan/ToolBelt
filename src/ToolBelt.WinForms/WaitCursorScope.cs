// ToolBelt.WinForms drop-in — Windows-only (net8.0-windows), self-contained (BCL + WinForms).
using System;
using System.Windows.Forms;

namespace ToolBelt.WinForms
{
    /// <summary>
    /// A scope that shows the wait (hourglass) cursor application-wide for its lifetime, then restores the
    /// previous state. Wrap a slow synchronous operation in a <c>using</c> block:
    /// <code>using (WaitCursorScope.Begin()) { DoSlowWork(); }</code>
    /// Restores <see cref="Application.UseWaitCursor"/> to whatever it was (so nested scopes compose).
    /// </summary>
    public sealed class WaitCursorScope : IDisposable
    {
        private readonly bool _previous;
        private bool _disposed;

        public WaitCursorScope()
        {
            _previous = Application.UseWaitCursor;
            Application.UseWaitCursor = true;
        }

        /// <summary>Begins a wait-cursor scope.</summary>
        public static WaitCursorScope Begin() => new WaitCursorScope();

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Application.UseWaitCursor = _previous;
        }
    }
}
