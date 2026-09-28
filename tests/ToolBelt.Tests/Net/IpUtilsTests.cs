using System;
using System.Net;
using ToolBelt.Net;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Net
{
    public sealed class IpUtilsTests
    {
        public void IsPrivateV4()
        {
            Check.True(IpUtils.IsPrivate(IPAddress.Parse("10.1.2.3")));
            Check.True(IpUtils.IsPrivate(IPAddress.Parse("172.16.0.1")));
            Check.True(IpUtils.IsPrivate(IPAddress.Parse("172.31.255.255")));
            Check.False(IpUtils.IsPrivate(IPAddress.Parse("172.32.0.1")));
            Check.True(IpUtils.IsPrivate(IPAddress.Parse("192.168.0.1")));
            Check.False(IpUtils.IsPrivate(IPAddress.Parse("8.8.8.8")));
        }

        public void IsPrivateV6()
        {
            Check.True(IpUtils.IsPrivate(IPAddress.Parse("fc00::1")));
            Check.True(IpUtils.IsPrivate(IPAddress.Parse("fd12:3456::1")));
            Check.False(IpUtils.IsPrivate(IPAddress.Parse("2001:db8::1")));
        }

        public void IsLoopback()
        {
            Check.True(IpUtils.IsLoopback(IPAddress.Parse("127.0.0.1")));
            Check.True(IpUtils.IsLoopback(IPAddress.Parse("::1")));
            Check.False(IpUtils.IsLoopback(IPAddress.Parse("10.0.0.1")));
        }

        public void NetmaskFromPrefix()
        {
            Check.Equal("255.255.255.0", IpUtils.NetmaskFromPrefix(24).ToString());
            Check.Equal("255.255.0.0", IpUtils.NetmaskFromPrefix(16).ToString());
            Check.Equal("0.0.0.0", IpUtils.NetmaskFromPrefix(0).ToString());
            Check.Equal("255.255.255.255", IpUtils.NetmaskFromPrefix(32).ToString());
        }

        public void PrefixFromNetmask()
        {
            Check.Equal(24, IpUtils.PrefixFromNetmask(IPAddress.Parse("255.255.255.0")));
            Check.Equal(0, IpUtils.PrefixFromNetmask(IPAddress.Parse("0.0.0.0")));
            Check.Equal(32, IpUtils.PrefixFromNetmask(IPAddress.Parse("255.255.255.255")));
        }

        public void NonContiguousNetmask_Throws()
        {
            Check.Throws<ArgumentException>(() => IpUtils.PrefixFromNetmask(IPAddress.Parse("255.0.255.0")));
        }

        // Round-trip: prefix -> netmask -> prefix for every valid IPv4 prefix length.
        public void Property_PrefixNetmaskRoundTrip()
        {
            for (int prefix = 0; prefix <= 32; prefix++)
                Check.Equal(prefix, IpUtils.PrefixFromNetmask(IpUtils.NetmaskFromPrefix(prefix)), $"prefix {prefix}");
        }

        public void InvalidPrefix_Throws()
        {
            Check.Throws<ArgumentOutOfRangeException>(() => IpUtils.NetmaskFromPrefix(33));
        }
    }
}
