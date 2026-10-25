// ToolBelt.WinForms drop-in — Windows-only (net8.0-windows), self-contained (BCL + WinForms).
using System;
using System.Threading;
using System.Windows.Forms;

namespace ToolBelt.WinForms
{
    /// <summary>
    /// Helpers for marshalling work onto a control's UI thread. Each method runs the delegate inline when
    /// already on the owning thread (or before the handle exists — as with <see cref="Control.InvokeRequired"/>, a
    /// control whose handle is not yet created cannot tell which thread owns it) and marshals via
    /// <see cref="Control.Invoke(Delegate)"/> / <see cref="Control.BeginInvoke(Delegate)"/> otherwise — so
    /// callers can update controls from background threads without sprinkling <c>InvokeRequired</c> checks.
    /// A disposed control is never touched: the synchronous helpers throw <see cref="ObjectDisposedException"/>, and
    /// <see cref="BeginInvokeIfRequired"/> drops the action (also when the handle is destroyed while it posts), so a
    /// worker reporting progress to a form the user just closed does not crash.
    /// A control with no window handle called from a thread that cannot be a UI thread (not STA — e.g. a thread-pool
    /// worker) is treated the same way: the handle is gone (a dialog closed but not yet disposed) or not yet created, so
    /// there is no thread to marshal to, and running the action there would create windows on the worker. The synchronous
    /// helpers throw <see cref="InvalidOperationException"/>; <see cref="BeginInvokeIfRequired"/> drops the action.
    /// </summary>
    public static class ControlExtensions
    {
        /// <summary>Runs <paramref name="action"/> on the control's UI thread, synchronously.</summary>
        public static void InvokeIfRequired(this Control control, Action action)
        {
            if (control is null) throw new ArgumentNullException(nameof(control));
            if (action is null) throw new ArgumentNullException(nameof(action));
            ThrowIfDisposed(control);
            if (control.InvokeRequired)
                control.Invoke(action);
            else if (NoUiThread(control))
                throw NoHandle(control);
            else
                action();
        }

        /// <summary>Runs <paramref name="func"/> on the control's UI thread, synchronously, and returns its result.</summary>
        public static T InvokeIfRequired<T>(this Control control, Func<T> func)
        {
            if (control is null) throw new ArgumentNullException(nameof(control));
            if (func is null) throw new ArgumentNullException(nameof(func));
            ThrowIfDisposed(control);
            if (control.InvokeRequired)
                return (T)control.Invoke(func);
            if (NoUiThread(control)) throw NoHandle(control);
            return func();
        }

        /// <summary>
        /// Posts <paramref name="action"/> to the control's UI thread asynchronously (fire-and-forget) when a
        /// marshal is required; runs it inline otherwise. Does nothing if the control is (being) disposed.
        /// </summary>
        public static void BeginInvokeIfRequired(this Control control, Action action)
        {
            if (control is null) throw new ArgumentNullException(nameof(control));
            if (action is null) throw new ArgumentNullException(nameof(action));
            if (control.IsDisposed || control.Disposing)
                return;
            if (control.InvokeRequired)
            {
                try { control.BeginInvoke(action); }
                catch (InvalidOperationException) when (control.IsDisposed || control.Disposing || !control.IsHandleCreated)
                {
                    // The handle went away between the check and the post: the control is gone, so is the work.
                }
            }
            else if (!NoUiThread(control))
                action();
        }

        // InvokeRequired is false when no handle exists in the parent chain; on a non-STA thread that cannot mean "this is
        // the UI thread", so there is nowhere safe to run the delegate.
        private static bool NoUiThread(Control control)
            => !control.IsHandleCreated && Thread.CurrentThread.GetApartmentState() != ApartmentState.STA;

        private static InvalidOperationException NoHandle(Control control)
            => new InvalidOperationException($"{control.GetType().Name} has no window handle (closed, or not yet shown), so there is no UI thread to marshal to.");

        private static void ThrowIfDisposed(Control control)
        {
            if (control.IsDisposed) throw new ObjectDisposedException(control.GetType().Name);
        }
    }
}
