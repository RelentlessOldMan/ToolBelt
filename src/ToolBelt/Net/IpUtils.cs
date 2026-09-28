// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Net;
using System.Net.Sockets;

namespace ToolBelt.Net
{
    /// <summary>
    /// Address classification and netmask/prefix conversions that the framework does not provide directly:
    /// private-range and loopback detection, and IPv4 netmask &lt;-&gt; prefix-length conversion. Pure and
    /// table/bit driven, so trivially testable.
    /// </summary>
    public static class IpUtils
    {
        /// <summary>True for RFC 1918 IPv4 ranges (10/8, 172.16/12, 192.168/16) or IPv6 unique-local (fc00::/7).</summary>
        public static bool IsPrivate(IPAddress address)
        {
            if (address is null) throw new ArgumentNullException(nameof(address));
            if (address.AddressFamily == AddressFamily.InterNetwork)
            {
                byte[] b = address.GetAddressBytes();
                if (b[0] == 10) return true;                              // 10.0.0.0/8
                if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return true; // 172.16.0.0/12
                if (b[0] == 192 && b[1] == 168) return true;              // 192.168.0.0/16
                return false;
            }
            if (address.AddressFamily == AddressFamily.InterNetworkV6)
            {
                byte[] b = address.GetAddressBytes();
                return (b[0] & 0xFE) == 0xFC; // fc00::/7 unique local
            }
            return false;
        }

        /// <summary>True for the IPv4 or IPv6 loopback ranges (127.0.0.0/8, ::1).</summary>
        public static bool IsLoopback(IPAddress address)
        {
            if (address is null) throw new ArgumentNullException(nameof(address));
            return IPAddress.IsLoopback(address);
        }

        /// <summary>Builds the IPv4 netmask for a prefix length, e.g. 24 -> 255.255.255.0.</summary>
        public static IPAddress NetmaskFromPrefix(int prefixLength)
        {
            if (prefixLength < 0 || prefixLength > 32)
                throw new ArgumentOutOfRangeException(nameof(prefixLength), prefixLength, "Prefix must be in [0, 32].");
            uint mask = prefixLength == 0 ? 0u : uint.MaxValue << (32 - prefixLength);
            return new IPAddress(new[] { (byte)(mask >> 24), (byte)(mask >> 16), (byte)(mask >> 8), (byte)mask });
        }

        /// <summary>Converts an IPv4 netmask (contiguous ones) to its prefix length.</summary>
        public static int PrefixFromNetmask(IPAddress netmask)
        {
            if (netmask is null) throw new ArgumentNullException(nameof(netmask));
            if (netmask.AddressFamily != AddressFamily.InterNetwork)
                throw new ArgumentException("A netmask must be an IPv4 address.", nameof(netmask));

            byte[] b = netmask.GetAddressBytes();
            uint value = ((uint)b[0] << 24) | ((uint)b[1] << 16) | ((uint)b[2] << 8) | b[3];

            int prefix = 0;
            bool seenZero = false;
            for (int i = 31; i >= 0; i--)
            {
                bool bit = ((value >> i) & 1) == 1;
                if (bit)
                {
                    if (seenZero) throw new ArgumentException("Netmask bits must be contiguous.", nameof(netmask));
                    prefix++;
                }
                else
                {
                    seenZero = true;
                }
            }
            return prefix;
        }
    }
}
