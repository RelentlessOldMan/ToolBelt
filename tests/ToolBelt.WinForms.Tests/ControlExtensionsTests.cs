using System;
using System.Threading;
using System.Windows.Forms;
using ToolBelt.Tests.Framework;
using ToolBelt.WinForms;

namespace ToolBelt.WinForms.Tests
{
    public sealed class ControlExtensionsTests
    {
        // Without a created handle / on the owning thread, InvokeRequired is false, so the delegate runs
        // inline — the deterministically testable path.

        public void InvokeIfRequired_RunsInlineOnSameThread()
        {
            RunSta(() =>
            {
                using var control = new Control();
                bool ran = false;
                control.InvokeIfRequired(() => ran = true);
                Check.True(ran, "action ran inline");
            });
        }

        public void InvokeIfRequired_Func_ReturnsValue()
        {
            RunSta(() =>
            {
                using var control = new Control();
                int result = control.InvokeIfRequired(() => 21 * 2);
                Check.Equal(42, result);
            });
        }

        public void BeginInvokeIfRequired_RunsInlineOnSameThread()
        {
            RunSta(() =>
            {
                using var control = new Control();
                bool ran = false;
                control.BeginInvokeIfRequired(() => ran = true);
                Check.True(ran, "action ran inline");
            });
        }

        public void Validation_Throws()
        {
            RunSta(() =>
            {
                using var control = new Control();
                Check.Throws<ArgumentNullException>(() => ControlExtensions.InvokeIfRequired(null!, () => { }));
                Check.Throws<ArgumentNullException>(() => control.InvokeIfRequired((Action)null!));
                Check.Throws<ArgumentNullException>(() => control.InvokeIfRequired((Func<int>)null!));
            });
        }

        private static void RunSta(Action body)
        {
            Exception? error = null;
            var t = new Thread(() =>
            {
                try { body(); }
                catch (Exception ex) { error = ex; }
            }) { IsBackground = true };
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join();
            if (error is not null) throw error;
        }
    }
}
