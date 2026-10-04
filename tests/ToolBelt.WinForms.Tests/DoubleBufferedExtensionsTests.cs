using System;
using System.Threading;
using System.Windows.Forms;
using ToolBelt.Tests.Framework;
using ToolBelt.WinForms;

namespace ToolBelt.WinForms.Tests
{
    public sealed class DoubleBufferedExtensionsTests
    {
        public void Enable_ThenRead_RoundTrips()
        {
            RunSta(() =>
            {
                using var control = new Panel();
                Check.False(control.IsDoubleBuffered(), "off by default");

                control.EnableDoubleBuffering();
                Check.True(control.IsDoubleBuffered(), "on after enable");

                control.EnableDoubleBuffering(false);
                Check.False(control.IsDoubleBuffered(), "off after disable");
            });
        }

        public void Validation_Throws()
        {
            Check.Throws<ArgumentNullException>(() => DoubleBufferedExtensions.EnableDoubleBuffering(null!));
            Check.Throws<ArgumentNullException>(() => DoubleBufferedExtensions.IsDoubleBuffered(null!));
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
