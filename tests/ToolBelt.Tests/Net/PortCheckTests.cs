using System;
using System.Net;
using System.Net.Sockets;
using ToolBelt.Net;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Net
{
    public sealed class PortCheckTests
    {
        public void FindFreePortReturnsUsablePort()
        {
            int port = PortCheck.FindFreePort();
            Check.True(port > 0 && port <= 65535, port.ToString());
        }

        public void ReachableWhenSomethingListens()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                Check.True(PortCheck.IsReachable("127.0.0.1", port, TimeSpan.FromSeconds(2)));
            }
            finally { listener.Stop(); }
        }

        public void NotReachableWhenNothingListens()
        {
            int port = PortCheck.FindFreePort(); // released, so nothing is listening
            Check.False(PortCheck.IsReachable("127.0.0.1", port, TimeSpan.FromMilliseconds(500)));
        }

        public void InvalidArguments_Throw()
        {
            Check.Throws<ArgumentNullException>(() => PortCheck.IsReachable(null!, 80, TimeSpan.FromSeconds(1)));
            Check.Throws<ArgumentOutOfRangeException>(() => PortCheck.IsReachable("h", 70000, TimeSpan.FromSeconds(1)));
            Check.Throws<ArgumentOutOfRangeException>(() => PortCheck.IsReachable("h", 80, TimeSpan.Zero));
        }
    }
}
