using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using ToolBelt.Net;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Net
{
    public sealed class HostInfoTests
    {
        public void LocalHostNameIsPresent()
        {
            Check.True(!string.IsNullOrEmpty(HostInfo.LocalHostName));
        }

        public void ResolvesLocalhostToLoopback()
        {
            var addresses = HostInfo.Resolve("localhost", TimeSpan.FromSeconds(5));
            Check.True(addresses.Count > 0);
            Check.True(addresses.Any(IPAddress.IsLoopback), "localhost should resolve to a loopback address");
        }

        public void LocalAddressesNonEmpty()
        {
            Check.True(HostInfo.LocalAddresses().Count > 0);
        }

        public void InterfacesIncludeAddresses()
        {
            var interfaces = HostInfo.Interfaces();
            Check.True(interfaces.Count > 0, "should enumerate at least one interface");
            Check.True(interfaces.Any(i => i.Addresses.Count > 0), "at least one interface should have an address");
        }

        public void ResolveUnknownHostThrowsQuickly()
        {
            Check.Throws<SocketException>(
                () => HostInfo.Resolve("no-such-host.invalid.toolbelt.test", TimeSpan.FromSeconds(5)));
        }

        public void InvalidArguments_Throw()
        {
            Check.Throws<ArgumentNullException>(() => HostInfo.Resolve(null!, TimeSpan.FromSeconds(1)));
            Check.Throws<ArgumentOutOfRangeException>(() => HostInfo.Resolve("localhost", TimeSpan.Zero));
        }
    }
}
