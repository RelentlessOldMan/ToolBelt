using System;
using System.Linq;
using System.Net;
using System.Numerics;
using ToolBelt.Net;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Net
{
    public sealed class CidrRangeTests
    {
        public void ParseAndNetwork()
        {
            var cidr = CidrRange.Parse("192.168.1.130/24");
            Check.Equal("192.168.1.0", cidr.Network.ToString()); // host bits masked off
            Check.Equal(24, cidr.PrefixLength);
            Check.Equal("192.168.1.0/24", cidr.ToString());
        }

        public void Contains()
        {
            var cidr = CidrRange.Parse("10.0.0.0/8");
            Check.True(cidr.Contains(IPAddress.Parse("10.255.1.1")));
            Check.False(cidr.Contains(IPAddress.Parse("11.0.0.1")));
            Check.False(cidr.Contains(IPAddress.Parse("::1"))); // different family
        }

        public void CountAndBroadcast()
        {
            var cidr = CidrRange.Parse("192.168.1.0/24");
            Check.Equal((BigInteger)256, cidr.AddressCount);
            Check.Equal("192.168.1.255", cidr.BroadcastAddress.ToString());
        }

        public void EnumerateSmallBlock()
        {
            var cidr = CidrRange.Parse("192.168.1.0/30"); // 4 addresses
            var all = cidr.Addresses().Select(a => a.ToString()).ToArray();
            Check.True(all.SequenceEqual(new[] { "192.168.1.0", "192.168.1.1", "192.168.1.2", "192.168.1.3" }));
        }

        public void IPv6()
        {
            var cidr = CidrRange.Parse("2001:db8::/32");
            Check.True(cidr.Contains(IPAddress.Parse("2001:db8:1234::1")));
            Check.False(cidr.Contains(IPAddress.Parse("2001:db9::1")));
            Check.Equal(BigInteger.One << 96, cidr.AddressCount);
        }

        public void InvalidInputs()
        {
            Check.False(CidrRange.TryParse("not-cidr", out _));
            Check.False(CidrRange.TryParse("10.0.0.0/33", out _));   // prefix too large for IPv4
            Check.Throws<FormatException>(() => CidrRange.Parse("10.0.0.0"));
            Check.Throws<ArgumentOutOfRangeException>(() => new CidrRange(IPAddress.Parse("10.0.0.0"), 40));
        }

        // Differential: Contains matches a naive uint bit-mask reference for random IPv4 blocks.
        public void Property_ContainsMatchesBitmask()
        {
            var rng = new Random(36);
            for (int t = 0; t < 5000; t++)
            {
                uint netRaw = (uint)rng.Next();
                int prefix = rng.Next(0, 33);
                uint mask = prefix == 0 ? 0u : uint.MaxValue << (32 - prefix);
                var cidr = new CidrRange(FromUInt(netRaw), prefix);

                uint testRaw = (uint)rng.Next();
                bool expected = (testRaw & mask) == (netRaw & mask);
                bool actual = cidr.Contains(FromUInt(testRaw));
                Check.Equal(expected, actual, $"t{t}: net={netRaw} prefix={prefix} test={testRaw}");
            }
        }

        private static IPAddress FromUInt(uint v)
            => new IPAddress(new[] { (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v });
    }
}
