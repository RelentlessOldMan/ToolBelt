using System;
using System.Threading;
using System.Windows.Forms;
using ToolBelt.Tests.Framework;
using ToolBelt.WinForms;

namespace ToolBelt.WinForms.Tests
{
    public sealed class LayoutSuspenderTests
    {
        public void Scope_SuspendsAndResumes_WithoutError()
        {
            RunSta(() =>
            {
                using var panel = new Panel();
                using (LayoutSuspender.Begin(panel))
                {
                    // Add several children while layout is suspended.
                    panel.Controls.Add(new Button());
                    panel.Controls.Add(new Label());
                }
                // Resumed cleanly; children are present.
                Check.Equal(2, panel.Controls.Count);
            });
        }

        public void Dispose_IsIdempotent()
        {
            RunSta(() =>
            {
                using var panel = new Panel();
                var scope = LayoutSuspender.Begin(panel);
                scope.Dispose();
                scope.Dispose(); // second dispose must be a no-op, not an unbalanced ResumeLayout
            });
        }

        public void NullControl_Throws()
        {
            Check.Throws<ArgumentNullException>(() => LayoutSuspender.Begin(null!));
            Check.Throws<ArgumentNullException>(() => new LayoutSuspender(null!));
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
