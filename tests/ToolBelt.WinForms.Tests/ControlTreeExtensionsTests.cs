using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Forms;
using ToolBelt.Tests.Framework;
using ToolBelt.WinForms;

namespace ToolBelt.WinForms.Tests
{
    public sealed class ControlTreeExtensionsTests
    {
        public void Descendants_VisitsWholeSubtree()
        {
            RunSta(() =>
            {
                using var root = new Panel { Name = "root" };
                var button = new Button { Name = "btn" };
                var inner = new Panel { Name = "inner" };
                var text = new TextBox { Name = "txt" };
                inner.Controls.Add(text);
                root.Controls.Add(button);
                root.Controls.Add(inner);

                var all = new List<Control>(root.Descendants());
                Check.Equal(3, all.Count); // button, inner, text — not root itself
                Check.True(all.Contains(button) && all.Contains(inner) && all.Contains(text), "all descendants present");
            });
        }

        public void DescendantsOfType_Filters()
        {
            RunSta(() =>
            {
                using var root = new Panel();
                root.Controls.Add(new Button());
                var inner = new Panel();
                inner.Controls.Add(new TextBox());
                inner.Controls.Add(new Button());
                root.Controls.Add(inner);

                var buttons = new List<Button>(root.DescendantsOfType<Button>());
                Check.Equal(2, buttons.Count);
            });
        }

        public void FindByName_LocatesNestedControl()
        {
            RunSta(() =>
            {
                using var root = new Panel();
                var inner = new Panel();
                var target = new TextBox { Name = "needle" };
                inner.Controls.Add(target);
                root.Controls.Add(inner);

                Check.True(ReferenceEquals(target, root.FindByName("needle")), "found by name");
                Check.Null(root.FindByName("missing"));
            });
        }

        public void Validation_Throws()
        {
            RunSta(() =>
            {
                using var root = new Panel();
                // Null-check is eager (not deferred until enumeration).
                Check.Throws<ArgumentNullException>(() => ControlTreeExtensions.Descendants(null!));
                Check.Throws<ArgumentNullException>(() => ControlTreeExtensions.DescendantsOfType<Button>(null!));
                Check.Throws<ArgumentNullException>(() => root.FindByName(null!));
            });
        }

        private static void RunSta(Action body)
        {
            Exception? error = null;
            var t = new Thread(() => { try { body(); } catch (Exception ex) { error = ex; } }) { IsBackground = true };
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join();
            if (error is not null) throw error;
        }
    }
}
