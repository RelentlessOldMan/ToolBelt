using System;
using System.Threading;
using ToolBelt.Tests.Framework;
using ToolBelt.Wpf;

namespace ToolBelt.Wpf.Tests
{
    public sealed class BindingProxyTests
    {
        // BindingProxy is a Freezable (a DependencyObject); exercise it on an STA thread.

        public void Data_RoundTrips()
        {
            RunSta(() =>
            {
                var proxy = new BindingProxy();
                Check.Null(proxy.Data);

                proxy.Data = "hello";
                Check.Equal("hello", (string)proxy.Data!);

                proxy.Data = 42;
                Check.Equal(42, (int)proxy.Data!);
            });
        }

        public void Clone_CopiesData()
        {
            RunSta(() =>
            {
                var proxy = new BindingProxy { Data = "payload" };
                var clone = (BindingProxy)proxy.Clone();
                Check.Equal("payload", (string)clone.Data!);

                // Clone is independent.
                clone.Data = "changed";
                Check.Equal("payload", (string)proxy.Data!);
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
