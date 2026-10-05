using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ToolBelt.Diagnostics;
using ToolBelt.Logging;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Logging
{
    public sealed class SyslogAndFirstChanceTests
    {
        private static readonly DateTimeOffset T = new DateTimeOffset(2026, 3, 1, 9, 30, 15, 123, TimeSpan.FromHours(1)).AddTicks(4560);

        // ---------- syslog format ----------

        public void Format_IsRfc5424()
        {
            var e = new LogEvent(T, LogLevel.Error, "Motion.Axis", "stall detected", null);
            Check.Equal("<131>1 2026-03-01T09:30:15.123456+01:00 rig-7 hexapod 4242 Motion.Axis - stall detected",
                SyslogSink.Format(e, SyslogFacility.Local0, "rig-7", "hexapod", "4242"));   // 16*8 + 3
        }

        public void Format_SeverityNilValuesAndSanitising()
        {
            Check.Equal(2, SyslogSink.Severity(LogLevel.Fatal));
            Check.Equal(4, SyslogSink.Severity(LogLevel.Warning));
            Check.Equal(6, SyslogSink.Severity(LogLevel.Info));
            Check.Equal(7, SyslogSink.Severity(LogLevel.Trace));
            var e = new LogEvent(T, LogLevel.Info, "", "msg", new InvalidOperationException("boom"));
            string line = SyslogSink.Format(e, SyslogFacility.User, "my host", "app name ünï", "");
            Check.True(line.StartsWith("<14>1 ", StringComparison.Ordinal), line);
            Check.True(Regex.IsMatch(line, @"^<14>1 \S+ my_host app_name_\S+ - - - msg \| System.InvalidOperationException: boom"), line);
            string longCat = SyslogSink.Format(new LogEvent(T, LogLevel.Info, new string('c', 100), "m", null), SyslogFacility.User, "h", "a", "1");
            Check.True(longCat.Contains(" " + new string('c', 32) + " - m"), "MSGID capped at 32");
        }

        // ---------- syslog transport ----------

        public async Task Udp_DeliversADatagram()
        {
            using var server = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            int port = ((IPEndPoint)server.Client.LocalEndPoint!).Port;
            using (var sink = new SyslogSink("127.0.0.1", port, SyslogTransport.Udp, SyslogFacility.Local3, "tb"))
                sink.Emit(new LogEvent(DateTimeOffset.Now, LogLevel.Warning, "cat", "hello udp", null));
            var receive = server.ReceiveAsync();
            Check.True(await Task.WhenAny(receive, Task.Delay(5000)) == receive, "datagram received");
            string text = Encoding.UTF8.GetString(receive.Result.Buffer);
            Check.True(text.StartsWith("<156>1 ", StringComparison.Ordinal) && text.EndsWith(" tb " + System.Diagnostics.Process.GetCurrentProcess().Id + " cat - hello udp", StringComparison.Ordinal), text);
        }

        public void Udp_TruncatesOnACharacterBoundary()
        {
            using var server = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            server.Client.ReceiveTimeout = 5000;
            int port = ((IPEndPoint)server.Client.LocalEndPoint!).Port;
            using (var sink = new SyslogSink("127.0.0.1", port))
                sink.Emit(new LogEvent(DateTimeOffset.Now, LogLevel.Info, "c", new string('é', 3000), null));
            IPEndPoint? from = null;
            byte[] data = server.Receive(ref from);
            Check.True(data.Length <= 2048, $"{data.Length} bytes");
            Check.False(Encoding.UTF8.GetString(data).Contains("�"), "no split UTF-8 sequence");
        }

        public async Task Tcp_UsesOctetCountingFraming()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var accept = listener.AcceptTcpClientAsync();
            using (var sink = new SyslogSink("127.0.0.1", port, SyslogTransport.Tcp))
            {
                sink.Emit(new LogEvent(DateTimeOffset.Now, LogLevel.Info, "a", "first", null));
                sink.Emit(new LogEvent(DateTimeOffset.Now, LogLevel.Info, "b", "second ünï", null));
                Check.Equal(0L, sink.FailedSends);
            }
            using var client = await accept;
            var ms = new MemoryStream();
            await client.GetStream().CopyToAsync(ms);
            listener.Stop();
            byte[] all = ms.ToArray();
            var frames = new List<string>();
            int pos = 0;
            while (pos < all.Length)
            {
                int space = Array.IndexOf(all, (byte)' ', pos);
                int len = int.Parse(Encoding.ASCII.GetString(all, pos, space - pos));
                frames.Add(Encoding.UTF8.GetString(all, space + 1, len));
                pos = space + 1 + len;
            }
            Check.Equal(2, frames.Count);
            Check.True(frames[0].EndsWith(" a - first", StringComparison.Ordinal), frames[0]);
            Check.True(frames[1].EndsWith(" b - second ünï", StringComparison.Ordinal), frames[1]);
        }

        public async Task Tcp_ReconnectsAfterTheCollectorDropsTheConnection()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                using var sink = new SyslogSink("127.0.0.1", port, SyslogTransport.Tcp);
                var firstAccept = listener.AcceptTcpClientAsync();
                sink.Emit(new LogEvent(DateTimeOffset.Now, LogLevel.Info, "a", "one", null));
                Check.True(await Task.WhenAny(firstAccept, Task.Delay(5000)) == firstAccept, "first connection");
                using (TcpClient first = await firstAccept)
                {
                    first.LingerState = new LingerOption(true, 0);                       // abortive close: RST
                }
                await Task.Delay(100);
                // Writes into a reset socket fail (possibly only on the second write); the sink must reconnect.
                var accept = listener.AcceptTcpClientAsync();
                for (int i = 0; i < 20 && !accept.IsCompleted; i++)
                {
                    sink.Emit(new LogEvent(DateTimeOffset.Now, LogLevel.Info, "b", "again", null));
                    await Task.Delay(50);
                }
                Check.True(accept.IsCompleted, "sink reconnected");
                (await accept).Dispose();
            }
            finally
            {
                listener.Stop();
            }
        }

        public void Tcp_FailuresAreCountedNotThrown()
        {
            int port;
            using (var probe = new TcpListener(IPAddress.Loopback, 0)) { probe.Start(); port = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop(); }
            using var sink = new SyslogSink("127.0.0.1", port, SyslogTransport.Tcp);
            sink.Emit(new LogEvent(DateTimeOffset.Now, LogLevel.Error, "c", "nobody listening", null));
            Check.Equal(1L, sink.FailedSends);
        }

        // ---------- first-chance monitor ----------

        public void FirstChance_SeesSwallowedExceptions()
        {
            var seen = new List<FirstChanceRecord>();
            using (var m = new FirstChanceMonitor(r => { lock (seen) seen.Add(r); }))
            {
                m.ThrottleWindow = TimeSpan.Zero;
                try { throw new FormatException("hidden-" + nameof(FirstChance_SeesSwallowedExceptions)); } catch { }
                try { throw new OperationCanceledException(); } catch { }               // ignored by default
            }
            try { throw new FormatException("after dispose " + nameof(FirstChance_SeesSwallowedExceptions)); } catch { }
            lock (seen)
            {
                Check.True(seen.Exists(r => r.Exception.Message == "hidden-" + nameof(FirstChance_SeesSwallowedExceptions)));
                Check.False(seen.Exists(r => r.Exception is OperationCanceledException));
                Check.False(seen.Exists(r => r.Exception.Message.StartsWith("after dispose", StringComparison.Ordinal)));
            }
        }

        public void FirstChance_ThrottlesAndCounts()
        {
            var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var reports = new List<FirstChanceRecord>();
            using var m = new FirstChanceMonitor(reports.Add, attach: false, clock: () => now);
            for (int i = 0; i < 5; i++) m.Observe(new IOException("disk"));
            Check.Equal(1, reports.Count);
            now = now.AddSeconds(11);
            m.Observe(new IOException("disk"));
            Check.Equal(2, reports.Count);
            Check.Equal(4, reports[1].SuppressedSinceLast);
            Check.Equal(6, reports[1].Occurrence);
            Check.True(reports[1].ToString().Contains("(+4 similar suppressed)"));
            m.Observe(new IOException("other"));                                       // a different message is its own signature
            Check.Equal(3, reports.Count);
            Check.Equal(6, m.Counts()["System.IO.IOException: disk"]);
        }

        public void FirstChance_FiltersIgnoresAndSurvivesABadReporter()
        {
            var reports = new List<FirstChanceRecord>();
            using var m = new FirstChanceMonitor(reports.Add, attach: false);
            m.Ignore<ArgumentException>();
            m.Observe(new ArgumentNullException("x"));                                 // subclass of an ignored type
            m.Filter = e => !e.Message.Contains("noise");
            m.Observe(new InvalidOperationException("noise"));
            m.Unignore<OperationCanceledException>();
            m.Observe(new TaskCanceledException());
            Check.Equal(1, reports.Count);
            Check.True(reports[0].Exception is TaskCanceledException);

            using var bad = new FirstChanceMonitor(_ => throw new Exception("reporter broke"), attach: false);
            bad.Observe(new Exception("x"));                                           // must not throw
            using var recursive = new FirstChanceMonitor(_ => { try { throw new Exception("inside"); } catch { } });
            try { throw new Exception("outer " + nameof(FirstChance_FiltersIgnoresAndSurvivesABadReporter)); } catch { }
        }
    }
}
