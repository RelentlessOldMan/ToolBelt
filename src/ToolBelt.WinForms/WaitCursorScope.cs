// ToolBelt.WinForms drop-in — Windows-only (net8.0-windows), self-contained (BCL + WinForms).
using System;
using System.Windows.Forms;

namespace ToolBelt.WinForms
{
    /// <summary>
    /// A scope that shows the wait (hourglass) cursor application-wide for its lifetime, then restores the
    /// previous state. Wrap a slow synchronous operation in a <c>using</c> block:
    /// <code>using (WaitCursorScope.Begin()) { DoSlowWork(); }</code>
    /// Open scopes are counted, so they nest and may be disposed in any order: when the last one closes,
    /// <see cref="Application.UseWaitCursor"/> returns to what it was before the first one opened.
    /// </summary>
    public sealed class WaitCursorScope : IDisposable
    {
        private static readonly object s_gate = new object();
        private static int s_open;
        private static bool s_before;
        private bool _disposed;

        public WaitCursorScope()
        {
            lock (s_gate)
            {
                if (s_open++ == 0) s_before = Application.UseWaitCursor;
                Application.UseWaitCursor = true;
            }
        }

        /// <summary>Begins a wait-cursor scope.</summary>
        public static WaitCursorScope Begin() => new WaitCursorScope();

        public void Dispose()
        {
            lock (s_gate)
            {
                if (_disposed) return;
                _disposed = true;
                if (--s_open == 0) Application.UseWaitCursor = s_before;
            }
        }
    }
}
