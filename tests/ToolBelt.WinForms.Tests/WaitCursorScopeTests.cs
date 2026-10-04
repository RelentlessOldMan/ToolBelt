using System;
using System.Threading;
using System.Windows.Forms;
using ToolBelt.Tests.Framework;
using ToolBelt.WinForms;

namespace ToolBelt.WinForms.Tests
{
    public sealed class WaitCursorScopeTests
    {
        public void Begin_SetsAndRestoresWaitCursor()
        {
            RunSta(() =>
            {
                Application.UseWaitCursor = false;
                using (WaitCursorScope.Begin())
                    Check.True(Application.UseWaitCursor, "wait cursor on inside scope");
                Check.False(Application.UseWaitCursor, "restored after scope");
            });
        }

        public void NestedScopes_RestorePreviousState()
        {
            RunSta(() =>
            {
                Application.UseWaitCursor = false;
                using (WaitCursorScope.Begin())
                {
                    using (WaitCursorScope.Begin())
                        Check.True(Application.UseWaitCursor, "inner on");
                    Check.True(Application.UseWaitCursor, "outer still on after inner disposes");
                }
                Check.False(Application.UseWaitCursor, "all restored");
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
