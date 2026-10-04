// ToolBelt.WinForms drop-in — Windows-only (net8.0-windows), self-contained (BCL + WinForms).
using System;
using System.Reflection;
using System.Windows.Forms;

namespace ToolBelt.WinForms
{
    /// <summary>
    /// Toggles a control's double buffering to cut flicker on custom-drawn or list-heavy controls. The
    /// framework's <c>DoubleBuffered</c> property is protected, so this sets it through reflection — handy
    /// for controls (like <see cref="DataGridView"/> or <see cref="ListView"/>) that do not expose it.
    /// </summary>
    public static class DoubleBufferedExtensions
    {
        private static readonly PropertyInfo DoubleBufferedProperty =
            typeof(Control).GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Control.DoubleBuffered is unavailable on this runtime.");

        /// <summary>Enables (or disables) double buffering on <paramref name="control"/>.</summary>
        public static void EnableDoubleBuffering(this Control control, bool enabled = true)
        {
            if (control is null) throw new ArgumentNullException(nameof(control));
            DoubleBufferedProperty.SetValue(control, enabled);
        }

        /// <summary>Reads a control's current double-buffering flag.</summary>
        public static bool IsDoubleBuffered(this Control control)
        {
            if (control is null) throw new ArgumentNullException(nameof(control));
            return DoubleBufferedProperty.GetValue(control) is bool b && b;
        }
    }
}
