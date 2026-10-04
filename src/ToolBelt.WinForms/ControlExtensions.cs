// ToolBelt.WinForms drop-in — Windows-only (net8.0-windows), self-contained (BCL + WinForms).
using System;
using System.Windows.Forms;

namespace ToolBelt.WinForms
{
    /// <summary>
    /// Helpers for marshalling work onto a control's UI thread. Each method runs the delegate inline when
    /// already on the owning thread (or before the handle exists) and marshals via
    /// <see cref="Control.Invoke(Delegate)"/> / <see cref="Control.BeginInvoke(Delegate)"/> otherwise — so
    /// callers can update controls from background threads without sprinkling <c>InvokeRequired</c> checks.
    /// </summary>
    public static class ControlExtensions
    {
        /// <summary>Runs <paramref name="action"/> on the control's UI thread, synchronously.</summary>
        public static void InvokeIfRequired(this Control control, Action action)
        {
            if (control is null) throw new ArgumentNullException(nameof(control));
            if (action is null) throw new ArgumentNullException(nameof(action));
            if (control.InvokeRequired)
                control.Invoke(action);
            else
                action();
        }

        /// <summary>Runs <paramref name="func"/> on the control's UI thread, synchronously, and returns its result.</summary>
        public static T InvokeIfRequired<T>(this Control control, Func<T> func)
        {
            if (control is null) throw new ArgumentNullException(nameof(control));
            if (func is null) throw new ArgumentNullException(nameof(func));
            if (control.InvokeRequired)
                return (T)control.Invoke(func);
            return func();
        }

        /// <summary>
        /// Posts <paramref name="action"/> to the control's UI thread asynchronously (fire-and-forget) when a
        /// marshal is required; runs it inline otherwise.
        /// </summary>
        public static void BeginInvokeIfRequired(this Control control, Action action)
        {
            if (control is null) throw new ArgumentNullException(nameof(control));
            if (action is null) throw new ArgumentNullException(nameof(action));
            if (control.InvokeRequired)
                control.BeginInvoke(action);
            else
                action();
        }
    }
}
