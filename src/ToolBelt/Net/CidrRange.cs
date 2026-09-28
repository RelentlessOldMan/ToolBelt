// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Numerics;

namespace ToolBelt.Net
{
    /// <summary>
    /// A CIDR block (an address plus a prefix length), for both IPv4 and IPv6. Membership and network
    /// computations use byte-level masking, so they work uniformly across families. Pure bit math — ideal
    /// differential-test material against a naive bit-by-bit reference.
    /// </summary>
    public readonly struct CidrRange
    {
        /// <summary>The network (masked) address of the block.</summary>
        public IPAddress Network { get; }
        public int PrefixLength { get; }
        public AddressFamily Family => Network.AddressFamily;

        public CidrRange(IPAddress address, int prefixLength)
        {
            if (address is null) throw new ArgumentNullException(nameof(address));
            int bits = TotalBits(address.AddressFamily);
            if (address.AddressFamily != AddressFamily.InterNetwork && address.AddressFamily != AddressFamily.InterNetworkV6)
                throw new ArgumentException("Only IPv4 and IPv6 addresses are supported.", nameof(address));
            if (prefixLength < 0 || prefixLength > bits)
                throw new ArgumentOutOfRangeException(nameof(prefixLength), prefixLength, $"Prefix must be in [0, {bits}].");

            PrefixLength = prefixLength;
            Network = new IPAddress(Mask(address.GetAddressBytes(), prefixLength));
        }

        public static CidrRange Parse(string text)
        {
            if (!TryParse(text, out CidrRange range))
                throw new FormatException($"'{text}' is not valid CIDR notation.");
            return range;
        }

        public static bool TryParse(string text, out CidrRange range)
        {
            range = default;
            if (string.IsNullOrWhiteSpace(text)) return false;
            int slash = text.IndexOf('/');
            if (slash < 0) return false;
            if (!IPAddress.TryParse(text.Substring(0, slash).Trim(), out IPAddress? addr) || addr is null) return false;
            if (!int.TryParse(text.Substring(slash + 1).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int prefix)) return false;

            int bits = TotalBits(addr.AddressFamily);
            if (prefix < 0 || prefix > bits) return false;
            range = new CidrRange(addr, prefix);
            return true;
        }

        /// <summary>True if <paramref name="address"/> is in this block (same family and same masked network).</summary>
        public bool Contains(IPAddress address)
        {
            if (address is null) throw new ArgumentNullException(nameof(address));
            if (address.AddressFamily != Family) return false;
            byte[] masked = Mask(address.GetAddressBytes(), PrefixLength);
            byte[] network = Network.GetAddressBytes();
            for (int i = 0; i < masked.Length; i++)
                if (masked[i] != network[i]) return false;
            return true;
        }

        /// <summary>The total number of addresses in the block (2^(bits - prefix)).</summary>
        public BigInteger AddressCount => BigInteger.One << (TotalBits(Family) - PrefixLength);

        /// <summary>The IPv4 broadcast address (network OR inverted mask). IPv6 has no broadcast.</summary>
        public IPAddress BroadcastAddress
        {
            get
            {
                if (Family != AddressFamily.InterNetwork)
                    throw new InvalidOperationException("Broadcast address applies to IPv4 only.");
                byte[] net = Network.GetAddressBytes();
                byte[] mask = MaskBytes(4, PrefixLength);
                var result = new byte[4];
                for (int i = 0; i < 4; i++) result[i] = (byte)(net[i] | ~mask[i]);
                return new IPAddress(result);
            }
        }

        /// <summary>Enumerates every IPv4 address in the block (lazy — beware very large blocks).</summary>
        public IEnumerable<IPAddress> Addresses()
        {
            if (Family != AddressFamily.InterNetwork)
                throw new InvalidOperationException("Address enumeration is supported for IPv4 only.");
            uint start = ToUInt32(Network.GetAddressBytes());
            uint count = PrefixLength == 0 ? uint.MaxValue : (1u << (32 - PrefixLength));
            for (uint offset = 0; ; offset++)
            {
                yield return new IPAddress(FromUInt32(start + offset));
                if (offset == count - 1) break; // avoid uint wrap past the last address
            }
        }

        public override string ToString()
            => Network + "/" + PrefixLength.ToString(CultureInfo.InvariantCulture);

        // ---- helpers ----

        private static int TotalBits(AddressFamily family)
            => family == AddressFamily.InterNetworkV6 ? 128 : 32;

        private static byte[] Mask(byte[] address, int prefixLength)
        {
            byte[] mask = MaskBytes(address.Length, prefixLength);
            var result = new byte[address.Length];
            for (int i = 0; i < address.Length; i++)
                result[i] = (byte)(address[i] & mask[i]);
            return result;
        }

        private static byte[] MaskBytes(int byteCount, int prefixLength)
        {
            var mask = new byte[byteCount];
            int full = prefixLength / 8;
            int remainder = prefixLength % 8;
            for (int i = 0; i < full && i < byteCount; i++) mask[i] = 0xFF;
            if (remainder > 0 && full < byteCount)
                mask[full] = (byte)(0xFF << (8 - remainder));
            return mask;
        }

        private static uint ToUInt32(byte[] b) => ((uint)b[0] << 24) | ((uint)b[1] << 16) | ((uint)b[2] << 8) | b[3];
        private static byte[] FromUInt32(uint v) => new[] { (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v };
    }
}
