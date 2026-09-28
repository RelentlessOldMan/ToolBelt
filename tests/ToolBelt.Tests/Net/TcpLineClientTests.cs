using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using ToolBelt.Net;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Net
{
    public sealed class TcpLineClientTests
    {
        // Starts a loopback echo server that reflects each received line. Returns the port.
        private static (TcpListener Listener, Task Server) StartEchoServer()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            Task server = Task.Run(async () =>
            {
                using TcpClient conn = await listener.AcceptTcpClientAsync();
                using NetworkStream s = conn.GetStream();
                using var r = new StreamReader(s);
                using var w = new StreamWriter(s) { AutoFlush = true, NewLine = "\n" };
                string? line;
                while ((line = r.ReadLine()) != null) w.WriteLine(line);
            });
            return (listener, server);
        }

        public void SendReceiveEchoes()
        {
            var (listener, _) = StartEchoServer();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            try
            {
                using var client = new TcpLineClient("127.0.0.1", port,
                    connectTimeout: TimeSpan.FromSeconds(5), readTimeout: TimeSpan.FromSeconds(5));
                Check.Equal("hello", client.SendReceive("hello"));
                Check.Equal("world", client.SendReceive("world"));
            }
            finally { listener.Stop(); }
        }

        public void ConnectTimeoutOrRefusalThrows()
        {
            int deadPort = PortCheck.FindFreePort(); // nothing listening -> connection refused
            Check.Throws<Exception>(() => new TcpLineClient("127.0.0.1", deadPort, connectTimeout: TimeSpan.FromMilliseconds(500)));
        }

        public void NullHost_Throws()
        {
            Check.Throws<ArgumentNullException>(() => new TcpLineClient(null!, 80));
        }
    }
}
