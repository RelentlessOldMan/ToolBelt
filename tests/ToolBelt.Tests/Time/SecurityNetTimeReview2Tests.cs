using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using ToolBelt.Net;
using ToolBelt.Security;
using ToolBelt.Tests.Framework;
using ToolBelt.Time;

namespace ToolBelt.Tests.Time
{
    /// <summary>Regression tests for the round 27 review (security, net, time, second pass).</summary>
    public sealed class SecurityNetTimeReview2Tests
    {
        private static readonly TimeZoneInfo Eastern = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        private static readonly TimeSpan Est = TimeSpan.FromHours(-5), Edt = TimeSpan.FromHours(-4);

        // ---- CronSchedule ----

        public void Cron_SpringForwardMakeUp_RunsWhenAfterIsInTheLastMinuteBeforeTheGap()
        {
            // 2026-03-08 02:00 EST -> 03:00 EDT. 02:30 is skipped, so it runs at the transition, whichever second of
            // 01:59 the search starts from.
            var s = new CronSchedule("30 2 * * *");
            var expected = new DateTimeOffset(2026, 3, 8, 3, 0, 0, Edt);
            Check.Equal(expected, s.GetNextOccurrence(new DateTimeOffset(2026, 3, 8, 1, 58, 30, Est), Eastern));
            Check.Equal(expected, s.GetNextOccurrence(new DateTimeOffset(2026, 3, 8, 1, 59, 30, Est), Eastern));
            Check.Equal(expected, s.GetNextOccurrence(new DateTimeOffset(2026, 3, 8, 1, 59, 0, Est), Eastern));
        }

        public void Cron_SpringForwardMakeUp_AfterTheEarlierRunThatHour()
        {
            // "30,59 1,2": after the 01:59 run comes the make-up for the skipped 02:30, not the next day's 01:30.
            var s = new CronSchedule("30,59 1,2 * * *");
            Check.Equal(new DateTimeOffset(2026, 3, 8, 3, 0, 0, Edt),
                s.GetNextOccurrence(new DateTimeOffset(2026, 3, 8, 1, 59, 0, Est), Eastern));
        }

        // ---- CidrRange ----

        public void Cidr_LeadingZeroOctet_IsRejectedNotReadAsOctal()
        {
            Check.False(CidrRange.TryParse("010.0.0.0/8", out _));
            Check.False(CidrRange.TryParse("192.168.010.0/24", out _));
            Check.False(CidrRange.TryParse("010.001.002.003/32", out _));
            Check.Equal("0.0.0.0/0", CidrRange.Parse("0.0.0.0/0").ToString());
            Check.Equal("10.0.0.0/8", CidrRange.Parse("10.0.0.0/8").ToString());
        }

        // ---- HumanDuration ----

        public void HumanDuration_SubMillisecond_FormatsAsZeroAndRoundTrips()
        {
            Check.Equal("0ms", HumanDuration.Format(TimeSpan.FromTicks(1)));
            Check.Equal("0ms", HumanDuration.Format(TimeSpan.FromTicks(-1)));
            Check.Equal(TimeSpan.Zero, HumanDuration.Parse(HumanDuration.Format(TimeSpan.FromTicks(-9999))));
        }

        // ---- TcpLineClient ----

        private static (TcpListener Listener, int Port) Listen()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return (listener, ((IPEndPoint)listener.LocalEndpoint).Port);
        }

        private static byte[] ReadExactly(NetworkStream ns, int count)
        {
            var buf = new byte[count];
            int got = 0;
            while (got < count)
            {
                int n = ns.Read(buf, got, count - got);
                if (n == 0) break;
                got += n;
            }
            return buf.Take(got).ToArray();
        }

        public async Task TcpLineClient_Utf8WithPreamble_SendsNoByteOrderMark()
        {
            var (listener, port) = Listen();
            try
            {
                var accept = listener.AcceptTcpClientAsync();
                using var client = new TcpLineClient("127.0.0.1", port, readTimeout: TimeSpan.FromSeconds(5), encoding: Encoding.UTF8);
                using TcpClient server = await accept;
                NetworkStream ns = server.GetStream();
                ns.ReadTimeout = 5000;
                client.Send("HI");
                client.Send("YO");
                Check.Equal("HI\nYO\n", Encoding.ASCII.GetString(ReadExactly(ns, 6)));
            }
            finally { listener.Stop(); }
        }

        public async Task TcpLineClient_PeerByteOrderMark_IsDropped()
        {
            var (listener, port) = Listen();
            try
            {
                var accept = listener.AcceptTcpClientAsync();
                using var client = new TcpLineClient("127.0.0.1", port, readTimeout: TimeSpan.FromSeconds(5));
                using TcpClient server = await accept;
                byte[] data = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.ASCII.GetBytes("ready\nx\n")).ToArray();
                server.GetStream().Write(data, 0, data.Length);
                Check.Equal("ready", client.ReadLine());
                Check.Equal("x", client.ReadLine());
            }
            finally { listener.Stop(); }
        }

        public async Task TcpLineClient_OverlongLine_KeepsTheLinesAfterIt()
        {
            var (listener, port) = Listen();
            try
            {
                var accept = listener.AcceptTcpClientAsync();
                using var client = new TcpLineClient("127.0.0.1", port, readTimeout: TimeSpan.FromSeconds(5)) { MaxLineLength = 5 };
                using TcpClient server = await accept;
                byte[] data = Encoding.ASCII.GetBytes("abcdefgh\nOK\n");
                server.GetStream().Write(data, 0, data.Length);
                Check.Throws<InvalidDataException>(() => client.ReadLine());
                Check.Equal("OK", client.ReadLine());
            }
            finally { listener.Stop(); }
        }

        public async Task TcpLineClient_OverlongLine_TailArrivingLaterIsNotALine()
        {
            var (listener, port) = Listen();
            try
            {
                var accept = listener.AcceptTcpClientAsync();
                using var client = new TcpLineClient("127.0.0.1", port, readTimeout: TimeSpan.FromSeconds(5)) { MaxLineLength = 5 };
                using TcpClient server = await accept;
                NetworkStream ns = server.GetStream();
                byte[] head = Encoding.ASCII.GetBytes("abcdefgh");
                ns.Write(head, 0, head.Length);
                Check.Throws<InvalidDataException>(() => client.ReadLine());
                byte[] rest = Encoding.ASCII.GetBytes("ij\nOK\n");
                ns.Write(rest, 0, rest.Length);
                Check.Equal("OK", client.ReadLine());
            }
            finally { listener.Stop(); }
        }

        // ---- HttpDownload ----

        public async Task Http_ReadTimeoutBeyondTimerLimit_WaitsInsteadOfThrowing()
        {
            byte[] body = Encoding.ASCII.GetBytes("hello world");
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            Task serve = Task.Run(async () =>
            {
                using TcpClient c = await listener.AcceptTcpClientAsync();
                NetworkStream ns = c.GetStream();
                var head = new StringBuilder();
                int b;
                while ((b = ns.ReadByte()) >= 0)
                {
                    head.Append((char)b);
                    if (head.Length >= 4 && head.ToString(head.Length - 4, 4) == "\r\n\r\n") break;
                }
                byte[] response = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nConnection: close\r\nContent-Length: {body.Length}\r\n\r\n")
                    .Concat(body).ToArray();
                ns.Write(response, 0, response.Length);
            });
            string dest = Path.Combine(Path.GetTempPath(), "toolbelt-dl27-" + Guid.NewGuid().ToString("N"));
            try
            {
                var r = await HttpDownload.ToFileAsync($"http://127.0.0.1:{port}/file", dest,
                    new HttpDownloadOptions { MaxAttempts = 1, ReadTimeout = TimeSpan.FromDays(60) });
                Check.Equal(11L, r.BytesDownloaded);
                Check.True(File.ReadAllBytes(dest).SequenceEqual(body));
                await serve;
            }
            finally
            {
                listener.Stop();
                foreach (string p in new[] { dest, dest + ".part" }) try { File.Delete(p); } catch { }
            }
        }

        /// <summary>One-connection-per-request HTTP server; the handler gets the request head and writes the response.</summary>
        private sealed class OneShotServer : IDisposable
        {
            private readonly TcpListener _listener = new TcpListener(IPAddress.Loopback, 0);
            public string Url { get; }
            public int Requests;

            public OneShotServer(Action<NetworkStream> respond)
            {
                _listener.Start();
                Url = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/file";
                _ = Task.Run(async () =>
                {
                    while (true)
                    {
                        TcpClient c;
                        try { c = await _listener.AcceptTcpClientAsync(); } catch { break; }
                        using (c)
                        {
                            try
                            {
                                NetworkStream ns = c.GetStream();
                                var head = new StringBuilder();
                                int b;
                                while ((b = ns.ReadByte()) >= 0)
                                {
                                    head.Append((char)b);
                                    if (head.Length >= 4 && head.ToString(head.Length - 4, 4) == "\r\n\r\n") break;
                                }
                                System.Threading.Interlocked.Increment(ref Requests);
                                respond(ns);
                            }
                            catch { }
                        }
                    }
                });
            }

            public void Dispose() { try { _listener.Stop(); } catch { } }
        }

        private static void Write(NetworkStream ns, string head, byte[] body)
        {
            byte[] h = Encoding.ASCII.GetBytes(head);
            ns.Write(h, 0, h.Length);
            ns.Write(body, 0, body.Length);
            ns.Flush();
        }

        private static string TempFile() => Path.Combine(Path.GetTempPath(), "toolbelt-dl27-" + Guid.NewGuid().ToString("N"));

        private static void Cleanup(string dest)
        {
            foreach (string p in new[] { dest, dest + ".part" }) try { File.Delete(p); } catch { }
        }

        public async Task Http_NegativeRetryBaseDelay_IsRejectedUpFront()
        {
            using var server = new OneShotServer(ns => Write(ns, "HTTP/1.1 500 Oops\r\nConnection: close\r\nContent-Length: 0\r\n\r\n", Array.Empty<byte>()));
            string dest = TempFile();
            try
            {
                // -1 ms is Task.Delay's "forever": the download used to hang after the first 500.
                Task<HttpDownloadResult> run = HttpDownload.ToFileAsync(server.Url, dest,
                    new HttpDownloadOptions { MaxAttempts = 3, RetryBaseDelay = TimeSpan.FromMilliseconds(-1) });
                Check.True(await Task.WhenAny(run, Task.Delay(10000)) == run, "the download hung");
                await Check.ThrowsAsync<ArgumentOutOfRangeException>(() => run);
                Check.Equal(0, server.Requests);
            }
            finally { Cleanup(dest); }
        }

        public async Task Http_SlowProgressCallback_IsNotAStalledServer()
        {
            byte[] body = Encoding.ASCII.GetBytes("hello");
            using var server = new OneShotServer(ns => Write(ns, "HTTP/1.1 200 OK\r\nConnection: close\r\nContent-Length: 5\r\n\r\n", body));
            string dest = TempFile();
            try
            {
                var r = await HttpDownload.ToFileAsync(server.Url, dest, new HttpDownloadOptions
                {
                    MaxAttempts = 1,
                    ReadTimeout = TimeSpan.FromMilliseconds(500),
                    OnProgress = (done, total) => { if (done > 0) System.Threading.Thread.Sleep(1200); },
                });
                Check.Equal(5L, r.BytesDownloaded);
                Check.True(File.ReadAllBytes(dest).SequenceEqual(body));
            }
            finally { Cleanup(dest); }
        }

        public async Task Http_206WithoutContentRange_IsNotAppended()
        {
            using var server = new OneShotServer(ns =>
                Write(ns, "HTTP/1.1 206 Partial Content\r\nConnection: close\r\nContent-Length: 11\r\n\r\n", Encoding.ASCII.GetBytes("HELLO WORLD")));
            string dest = TempFile();
            try
            {
                File.WriteAllText(dest + ".part", "HELLO");
                await Check.ThrowsAsync<HttpDownloadException>(() =>
                    HttpDownload.ToFileAsync(server.Url, dest, new HttpDownloadOptions { Resume = true, MaxAttempts = 1 }));
                Check.False(File.Exists(dest));
                Check.False(File.Exists(dest + ".part"));
            }
            finally { Cleanup(dest); }
        }

        // ---- PasswordHasher / KeyDerivation ----

        public void PasswordHasher_ForgedLongDigest_IsRejected()
        {
            string salt = Convert.ToBase64String(new byte[16]);
            string forged = "PBKDF2-SHA256$1000$" + salt + "$" + Convert.ToBase64String(new byte[32 * 64]);
            Check.Throws<FormatException>(() => PasswordHasher.Verify("pw", forged));
            Check.Throws<FormatException>(() => PasswordHasher.Verify("pw", "PBKDF2-SHA256$1000$" + salt + "$" + Convert.ToBase64String(new byte[1])));
            Check.True(PasswordHasher.Verify("pw", PasswordHasher.Hash("pw", 1000)));
        }

        public void KeyDerivation_HugeKeyLengthAndUnknownHash_AreArgumentErrors()
        {
            byte[] salt = new byte[16];
            var ex = Check.Throws<ArgumentOutOfRangeException>(() => KeyDerivation.Pbkdf2("pw", salt, 1, int.MaxValue));
            Check.Equal("keyLength", ex.ParamName);
            Check.Equal("hash", Check.Throws<ArgumentOutOfRangeException>(() => KeyDerivation.Pbkdf2("pw", salt, 1, 32, (KdfHash)99)).ParamName);

            // A partial last block is still the prefix of the longer output.
            byte[] full = KeyDerivation.Pbkdf2("pw", salt, 2, 64);
            Check.True(full.Take(33).SequenceEqual(KeyDerivation.Pbkdf2("pw", salt, 2, 33)));
        }

        // ---- CronScheduler ----

        private static readonly Func<TimeSpan, System.Threading.CancellationToken, Task> Never =
            (t, ct) => Task.Delay(System.Threading.Timeout.Infinite, ct);

        public void CronScheduler_StartWithAnImpossibleSchedule_StillSchedulesTheOthers()
        {
            var errors = new System.Collections.Generic.List<string>();
            var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            using var s = new CronScheduler((j, e) => { lock (errors) errors.Add(j.Name); }, () => now, Never, TimeZoneInfo.Utc);
            var bad = s.Add("feb30", "0 0 30 2 *", () => { });
            var good = s.Add("hourly", "0 * * * *", () => { });
            s.Start();
            Check.Null(bad.NextRun);
            Check.Equal(new DateTimeOffset(2026, 1, 1, 1, 0, 0, TimeSpan.Zero), good.NextRun);
            lock (errors) Check.Equal("feb30", string.Join(",", errors));
        }

        public void CronScheduler_ThrowingOnError_DoesNotEscapeRunDue()
        {
            var now = new DateTimeOffset(9998, 12, 31, 23, 59, 0, TimeSpan.Zero);
            using var s = new CronScheduler((j, e) => throw new InvalidOperationException("handler"), () => now, Never, TimeZoneInfo.Utc);
            var job = s.Add("newyear", "0 0 1 1 *", () => { });
            s.Start();
            now = new DateTimeOffset(9999, 1, 1, 0, 0, 0, TimeSpan.Zero);
            Check.Equal(1, s.RunDue());   // the job runs; its next time is past the calendar, and onError throws
            Check.Null(job.NextRun);
        }

        public void CronScheduler_DisposeFromOnErrorInTheLoop_DoesNotDeadlock()
        {
            int calls = 0;
            var disposed = new System.Threading.ManualResetEventSlim();
            CronScheduler? s = null;
            s = new CronScheduler((j, e) => { s!.Dispose(); disposed.Set(); },
                () => System.Threading.Interlocked.Increment(ref calls) == 1
                    ? new DateTimeOffset(9998, 12, 31, 23, 59, 0, TimeSpan.Zero)
                    : new DateTimeOffset(9999, 1, 1, 0, 0, 0, TimeSpan.Zero),
                Never, TimeZoneInfo.Utc);
            s.Add("newyear", "0 0 1 1 *", () => { });
            s.Start();   // first clock read: next run 9999-01-01; the loop then finds it due with no later occurrence
            Check.True(disposed.Wait(TimeSpan.FromSeconds(10)), "Dispose from onError did not return");
        }

        // ---- CidrRange / IpUtils ----

        public void Cidr_Default_ThrowsInvalidOperationInsteadOfNullReference()
        {
            CidrRange.TryParse("nonsense", out CidrRange r);
            Check.Throws<InvalidOperationException>(() => r.Contains(IPAddress.Loopback));
            Check.Throws<InvalidOperationException>(() => _ = r.Family);
            Check.Equal("", r.ToString());
        }

        public void IpUtils_IsPrivate_UnwrapsIPv4MappedAddresses()
        {
            Check.True(IpUtils.IsPrivate(IPAddress.Parse("::ffff:10.0.0.1")));
            Check.True(IpUtils.IsPrivate(IPAddress.Parse("::ffff:192.168.1.1")));
            Check.False(IpUtils.IsPrivate(IPAddress.Parse("::ffff:8.8.8.8")));
        }

        // ---- SecretsFile ----

        public void SecretsFile_LoneSurrogate_IsRejectedInsteadOfAltered()
        {
            var s = new SecretsFile();
            Check.Throws<ArgumentException>(() => s.Set("a", "x\uD800y"));
            Check.Throws<ArgumentException>(() => s.Set("\uD800", "1"));
            Check.Throws<ArgumentException>(() => s.Set("k", "\uDC00"));
            Check.False(s.Contains("a"));

            // A proper pair still round-trips.
            s.Set("emoji😀", "v😀");
            var back = SecretsFile.FromBytes(s.ToBytes("pw", 1000), "pw");
            Check.True(back.TryGet("emoji😀", out string? v));
            Check.Equal("v😀", v);
        }
    }
}
