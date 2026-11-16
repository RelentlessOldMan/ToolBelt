using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using ToolBelt.Net;
using ToolBelt.Tests.Framework;
using ToolBelt.Time;

namespace ToolBelt.Tests.Time
{
    /// <summary>Regression tests for the round 92 review (security, net, time, sixth pass). The scheduler's jitter
    /// regression is in <see cref="SecurityNetTimeReview5Tests"/>, next to the round 91 tests of the code it fixes.</summary>
    public sealed class SecurityNetTimeReview6Tests
    {
        // ---- TcpLineClient: bad bytes between a CR and its LF ----

        private static void WithServer(byte[] payload, Action<TcpLineClient> read)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                Task server = Task.Run(async () =>
                {
                    using TcpClient conn = await listener.AcceptTcpClientAsync();
                    using NetworkStream s = conn.GetStream();
                    s.Write(payload, 0, payload.Length);
                    conn.Client.Shutdown(SocketShutdown.Send);
                    await Task.Delay(200);
                });
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                using (var client = new TcpLineClient("127.0.0.1", port, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5),
                           encoding: new UTF8Encoding(false, true)))
                    read(client);
                server.Wait(TimeSpan.FromSeconds(5));
            }
            finally { listener.Stop(); }
        }

        public void Tcp_BadBytesBetweenCrAndLfDropOnlyThemselves()
        {
            WithServer(new byte[] { 0x61, 0x0D, 0xFF, 0x0A, 0x62, 0x0A }, c =>
            {
                Check.Equal("a", c.ReadLine());
                Check.Throws<DecoderFallbackException>(() => c.ReadLine());
                Check.Equal("b", c.ReadLine());
                Check.Null(c.ReadLine());
            });
        }

        public void Tcp_ACrLfWithNoBadBytesIsStillOneBreak()
        {
            WithServer(Encoding.UTF8.GetBytes("a\r\nb\r\n\r\nc"), c =>
            {
                Check.Equal("a", c.ReadLine());
                Check.Equal("b", c.ReadLine());
                Check.Equal("", c.ReadLine());
                Check.Equal("c", c.ReadLine());
                Check.Null(c.ReadLine());
            });
        }

        // ---- RelativeTime: years are calendar years ----

        // Whole calendar years from a to b (a <= b), counted one at a time.
        private static int NaiveYears(DateTime a, DateTime b)
        {
            int n = 0;
            while (n < 9998 && a.AddYears(n + 1) <= b) n++;
            return n;
        }

        public void RelativeTime_YearsMatchCalendarYears()
        {
            var rng = new Random(92);
            for (int trial = 0; trial < 2000; trial++)
            {
                var a = new DateTime(rng.Next(1, 9999), rng.Next(1, 13), rng.Next(1, 29), rng.Next(24), rng.Next(60), 0, DateTimeKind.Utc);
                if (rng.Next(4) == 0)   // a Feb 29 start
                {
                    int y = a.Year;
                    while (!DateTime.IsLeapYear(y)) y = y == 1 ? 4 : y - 1;
                    a = new DateTime(y, 2, 29, 12, 0, 0, DateTimeKind.Utc);
                }
                long maxDays = (DateTime.MaxValue - a).Ticks / TimeSpan.TicksPerDay;
                if (maxDays < 400) continue;
                long span = trial % 3 == 0 ? rng.Next(365, 3000) : (long)(rng.NextDouble() * maxDays);
                if (span < 365) span = 365;
                var b = a.AddDays(span).AddMinutes(rng.Next(-30, 30));
                if (b - a < TimeSpan.FromDays(365)) continue;
                int years = Math.Max(1, NaiveYears(a, b));
                string want = years == 1 ? "1 year" : years + " years";
                var ta = new DateTimeOffset(a);
                var tb = new DateTimeOffset(b);
                Check.Equal(want + " ago", RelativeTime.Format(ta, tb), $"{a:o} -> {b:o}");
                Check.Equal("in " + want, RelativeTime.Format(tb, ta), $"{b:o} <- {a:o}");
            }
        }

        public void RelativeTime_TheCalendarsWholeSpanIsUnder9999Years()
        {
            Check.Equal("9998 years ago", RelativeTime.Format(DateTimeOffset.MinValue, DateTimeOffset.MaxValue));
            Check.Equal("2000 years ago", RelativeTime.Format(new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(4000, 1, 1, 0, 0, 0, TimeSpan.Zero)));
            Check.Equal("1 year ago", RelativeTime.Format(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2024, 12, 31, 0, 0, 0, TimeSpan.Zero)));
        }
    }
}
