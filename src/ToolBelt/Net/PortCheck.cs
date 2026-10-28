// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace ToolBelt.Net
{
    /// <summary>
    /// TCP port helpers: test whether a port is accepting connections within a timeout, and find a free
    /// local port. The second is what test fixtures need to avoid hard-coding ports.
    /// </summary>
    public static class PortCheck
    {
        // Rounds up to whole milliseconds; anything past int.MaxValue ms (about 24.8 days) waits indefinitely.
        private static int WaitMilliseconds(TimeSpan timeout)
        {
            double ms = Math.Ceiling(timeout.TotalMilliseconds);
            return ms >= int.MaxValue ? Timeout.Infinite : (int)ms;
        }

        /// <summary>True if a TCP connection to <paramref name="host"/>:<paramref name="port"/> succeeds within <paramref name="timeout"/>.</summary>
        public static bool IsReachable(string host, int port, TimeSpan timeout)
        {
            if (host is null) throw new ArgumentNullException(nameof(host));
            if (port < 0 || port > 65535) throw new ArgumentOutOfRangeException(nameof(port), port, "Port must be in [0, 65535].");
            if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout), timeout, "Timeout must be positive.");

            var client = NewClient();
            try
            {
                Task connect;
                bool completed;
                try
                {
                    connect = client.ConnectAsync(host, port);   // may throw before it returns a task
                    completed = connect.Wait(WaitMilliseconds(timeout));
                }
                catch { return false; } // connection refused / DNS failure surface here

                if (!completed)
                {
                    // Observe the eventual fault so it is not an unobserved task exception.
                    _ = connect.ContinueWith(t => { _ = t.Exception; }, TaskScheduler.Default);
                    return false;
                }
                return client.Connected;
            }
            finally
            {
                client.Dispose();
            }
        }

        // On .NET Framework a plain TcpClient is IPv4-only (connecting to "::1" throws); a dual-mode IPv6 socket reaches
        // both families, as the default one does on .NET Core.
        private static TcpClient NewClient()
            => Socket.OSSupportsIPv6 ? new TcpClient(AddressFamily.InterNetworkV6) { Client = { DualMode = true } } : new TcpClient();

        /// <summary>Reserves and releases an ephemeral loopback port, returning the number that was free.</summary>
        public static int FindFreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try { return ((IPEndPoint)listener.LocalEndpoint).Port; }
            finally { listener.Stop(); }
        }
    }
}
