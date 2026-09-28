// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace ToolBelt.Net
{
    /// <summary>A network interface's name, addresses and hardware (MAC) address.</summary>
    public sealed class InterfaceInfo
    {
        internal InterfaceInfo(string name, IReadOnlyList<IPAddress> addresses, string? macAddress, bool isUp)
        {
            Name = name; Addresses = addresses; MacAddress = macAddress; IsUp = isUp;
        }

        public string Name { get; }
        public IReadOnlyList<IPAddress> Addresses { get; }
        public string? MacAddress { get; }
        public bool IsUp { get; }
    }

    /// <summary>
    /// Local host and interface information, and DNS resolution with a real timeout — the framework's own
    /// resolve has no usable timeout, a frequently painful gap.
    /// </summary>
    public static class HostInfo
    {
        /// <summary>The local machine's host name.</summary>
        public static string LocalHostName => Dns.GetHostName();

        /// <summary>The IP addresses of the local host.</summary>
        public static IReadOnlyList<IPAddress> LocalAddresses() => Dns.GetHostAddresses(Dns.GetHostName());

        /// <summary>Resolves <paramref name="host"/> to addresses, throwing <see cref="TimeoutException"/> if it takes too long.</summary>
        public static IReadOnlyList<IPAddress> Resolve(string host, TimeSpan timeout)
        {
            if (host is null) throw new ArgumentNullException(nameof(host));
            if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout), timeout, "Timeout must be positive.");

            Task<IPAddress[]> lookup = Dns.GetHostAddressesAsync(host);
            bool completed;
            try
            {
                completed = lookup.Wait((int)timeout.TotalMilliseconds);
            }
            catch (AggregateException ex)
            {
                throw ex.GetBaseException(); // surface the real DNS error (e.g. SocketException), not the wrapper
            }
            if (!completed)
            {
                _ = lookup.ContinueWith(t => { _ = t.Exception; }, TaskScheduler.Default);
                throw new TimeoutException($"DNS resolution of '{host}' timed out.");
            }
            return lookup.Result;
        }

        /// <summary>Enumerates the machine's network interfaces with their addresses and MAC.</summary>
        public static IReadOnlyList<InterfaceInfo> Interfaces()
        {
            var result = new List<InterfaceInfo>();
            foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                var addresses = new List<IPAddress>();
                foreach (UnicastIPAddressInformation ua in nic.GetIPProperties().UnicastAddresses)
                    addresses.Add(ua.Address);

                string? mac = null;
                byte[] macBytes = nic.GetPhysicalAddress().GetAddressBytes();
                if (macBytes.Length > 0) mac = BitConverter.ToString(macBytes); // AA-BB-CC-...

                result.Add(new InterfaceInfo(nic.Name, addresses, mac, nic.OperationalStatus == OperationalStatus.Up));
            }
            return result;
        }
    }
}
