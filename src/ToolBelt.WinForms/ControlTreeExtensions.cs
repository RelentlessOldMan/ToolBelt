// ToolBelt.WinForms drop-in — Windows-only (net8.0-windows), self-contained (BCL + WinForms).
using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace ToolBelt.WinForms
{
    /// <summary>
    /// Walks a control's child hierarchy. <see cref="Descendants"/> yields every control beneath a parent
    /// (depth-first), <see cref="DescendantsOfType{T}"/> filters by type, and <see cref="FindByName"/> locates
    /// a control by its <see cref="Control.Name"/> anywhere in the subtree. Iterative, so deep trees never
    /// overflow the stack.
    /// </summary>
    public static class ControlTreeExtensions
    {
        /// <summary>Every descendant control of <paramref name="control"/>, depth-first (excludes the control itself).</summary>
        public static IEnumerable<Control> Descendants(this Control control)
        {
            if (control is null) throw new ArgumentNullException(nameof(control));
            return Iterate(control);

            static IEnumerable<Control> Iterate(Control root)
            {
                var stack = new Stack<Control>();
                PushChildren(root, stack);
                while (stack.Count > 0)
                {
                    Control current = stack.Pop();
                    yield return current;
                    PushChildren(current, stack);
                }
            }
        }

        /// <summary>Descendant controls of type <typeparamref name="T"/>.</summary>
        public static IEnumerable<T> DescendantsOfType<T>(this Control control) where T : Control
        {
            if (control is null) throw new ArgumentNullException(nameof(control));
            return Iterate(control);

            static IEnumerable<T> Iterate(Control root)
            {
                foreach (Control c in root.Descendants())
                    if (c is T typed)
                        yield return typed;
            }
        }

        /// <summary>The first descendant whose <see cref="Control.Name"/> equals <paramref name="name"/> (ordinal), or null.</summary>
        public static Control? FindByName(this Control control, string name)
        {
            if (name is null) throw new ArgumentNullException(nameof(name));
            foreach (Control c in control.Descendants())
                if (string.Equals(c.Name, name, StringComparison.Ordinal))
                    return c;
            return null;
        }

        private static void PushChildren(Control parent, Stack<Control> stack)
        {
            Control.ControlCollection children = parent.Controls;
            // Push in reverse so enumeration visits children in their declared order.
            for (int i = children.Count - 1; i >= 0; i--)
                stack.Push(children[i]);
        }
    }
}
