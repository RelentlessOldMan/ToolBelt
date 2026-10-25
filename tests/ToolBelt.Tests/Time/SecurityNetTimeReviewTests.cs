using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ToolBelt.Net;
using ToolBelt.Security;
using ToolBelt.Tests.Framework;
using ToolBelt.Time;

namespace ToolBelt.Tests.Time
{
    /// <summary>Regression tests for the round 13 review (security, net, time).</summary>
    public sealed class SecurityNetTimeReviewTests
    {
        private static readonly TimeZoneInfo Eastern = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        private static readonly TimeSpan Est = TimeSpan.FromHours(-5), Edt = TimeSpan.FromHours(-4);

        private static DateTimeOffset Utc(int y, int mo, int d, int h, int mi) => new DateTimeOffset(y, mo, d, h, mi, 0, TimeSpan.Zero);

        // ---- CronSchedule / CronScheduler ----

        public void Cron_DayOfWeekStepFromValue_DoesNotAddSunday()
        {
            // "1/2" = Monday, Wednesday, Friday; the step must not reach 7 (Sunday).
            var s = new CronSchedule("0 0 * * 1/2");
            DateTimeOffset saturday = new DateTimeOffset(2026, 3, 7, 12, 0, 0, TimeSpan.Zero);
            Check.Equal(new DateTimeOffset(2026, 3, 9, 0, 0, 0, TimeSpan.Zero), s.GetNextOccurrence(saturday));
        }

        public void Cron_Feb29_AcrossCenturyNonLeapYear_IsFound()
        {
            // 2100 is not a leap year, so the gap between Feb 29ths is 8 years.
            var s = new CronSchedule("0 0 29 2 *");
            Check.Equal(new DateTimeOffset(2104, 2, 29, 0, 0, 0, TimeSpan.Zero),
                s.GetNextOccurrence(new DateTimeOffset(2096, 3, 1, 0, 0, 0, TimeSpan.Zero)));
        }

        public void Cron_NearMaxValue_ThrowsInvalidOperation()
        {
            var s = new CronSchedule("0 0 1 1 *");
            Check.Throws<InvalidOperationException>(() => s.GetNextOccurrence(new DateTimeOffset(9999, 6, 1, 0, 0, 0, TimeSpan.Zero)));
        }

        public void Cron_HugeStep_DoesNotOverflow()
        {
            var s = new CronSchedule("1/2147483647 * * * *");
            Check.Equal(Utc(2026, 3, 2, 10, 1), s.GetNextOccurrence(Utc(2026, 3, 2, 10, 0)));
        }

        public void Cron_StarStepDayOfMonth_IsUnrestricted()
        {
            // Like Vixie cron, a field starting with '*' is unrestricted, so "*/1" in day-of-month ANDs with the weekday.
            var s = new CronSchedule("0 0 */1 * 1");
            Check.Equal(Utc(2026, 3, 9, 0, 0), s.GetNextOccurrence(Utc(2026, 3, 3, 0, 0)));
        }

        public void Cron_Zone_SpringForward_FixedTimeRunsAtTransition()
        {
            // 2026-03-08 02:00 EST jumps to 03:00 EDT; a 02:30 job runs at the transition instead of being skipped.
            var s = new CronSchedule("30 2 * * *");
            DateTimeOffset next = s.GetNextOccurrence(new DateTimeOffset(2026, 3, 8, 1, 0, 0, Est), Eastern);
            Check.Equal(new DateTimeOffset(2026, 3, 8, 3, 0, 0, Edt), next);
            Check.Equal(Edt, next.Offset);
        }

        public void Cron_Zone_KeepsWallTimeAcrossDst()
        {
            var s = new CronSchedule("0 9 * * *");
            DateTimeOffset next = s.GetNextOccurrence(new DateTimeOffset(2026, 3, 7, 10, 0, 0, Est), Eastern);
            Check.Equal(new DateTimeOffset(2026, 3, 8, 9, 0, 0, Edt), next);
        }

        public void Cron_Zone_FallBack_FixedTimeRunsOnce()
        {
            // 2026-11-01 01:00-01:59 happens twice; a 01:30 job runs only in the first pass.
            var s = new CronSchedule("30 1 * * *");
            Check.Equal(new DateTimeOffset(2026, 11, 1, 1, 30, 0, Edt), s.GetNextOccurrence(new DateTimeOffset(2026, 11, 1, 0, 0, 0, Edt), Eastern));
            Check.Equal(new DateTimeOffset(2026, 11, 2, 1, 30, 0, Est), s.GetNextOccurrence(new DateTimeOffset(2026, 11, 1, 1, 30, 0, Edt), Eastern));
        }

        public void Cron_Zone_FallBack_WildcardRunsInBothPasses()
        {
            var s = new CronSchedule("*/30 * * * *");
            Check.Equal(new DateTimeOffset(2026, 11, 1, 1, 0, 0, Est), s.GetNextOccurrence(new DateTimeOffset(2026, 11, 1, 1, 30, 0, Edt), Eastern));
        }

        public void Scheduler_UsesTimeZone_AcrossDst()
        {
            DateTimeOffset now = new DateTimeOffset(2026, 3, 7, 10, 0, 0, Est);
            var s = new CronScheduler(clock: () => now, delay: (t, ct) => Task.Delay(Timeout.Infinite, ct), timeZone: Eastern);
            try
            {
                var job = s.Add("nine", "0 9 * * *", () => { });
                s.Start();
                Check.Equal(new DateTimeOffset(2026, 3, 8, 9, 0, 0, Edt), job.NextRun!.Value);
                now = new DateTimeOffset(2026, 3, 8, 9, 0, 0, Edt);
                Check.Equal(1, s.RunDue());
                Check.Equal(new DateTimeOffset(2026, 3, 9, 9, 0, 0, Edt), job.NextRun!.Value);
            }
            finally { s.Dispose(); }
        }

        public void Scheduler_FallBack_FixedJobDoesNotFireTwice()
        {
            DateTimeOffset now = new DateTimeOffset(2026, 11, 1, 0, 0, 0, Edt);
            var s = new CronScheduler(clock: () => now, delay: (t, ct) => Task.Delay(Timeout.Infinite, ct), timeZone: Eastern);
            try
            {
                var job = s.Add("one-thirty", "30 1 * * *", () => { });
                s.Start();
                now = new DateTimeOffset(2026, 11, 1, 1, 30, 0, Edt);
                Check.Equal(1, s.RunDue());
                now = new DateTimeOffset(2026, 11, 1, 1, 30, 0, Est);   // the repeated 01:30, an hour later
                Check.Equal(0, s.RunDue());
                Check.Equal(new DateTimeOffset(2026, 11, 2, 1, 30, 0, Est), job.NextRun!.Value);
            }
            finally { s.Dispose(); }
        }

        public void Scheduler_NextOccurrenceFailure_IsReportedNotThrown()
        {
            DateTimeOffset now = new DateTimeOffset(9999, 12, 31, 23, 0, 0, TimeSpan.Zero);
            var errors = new List<Exception>();
            var s = new CronScheduler((j, e) => { lock (errors) errors.Add(e); }, () => now, (t, ct) => Task.Delay(Timeout.Infinite, ct), TimeZoneInfo.Utc);
            try
            {
                var job = s.Add("last", "59 23 * * *", () => { });
                s.Start();
                Check.Equal(new DateTimeOffset(9999, 12, 31, 23, 59, 0, TimeSpan.Zero), job.NextRun!.Value);
                now = new DateTimeOffset(9999, 12, 31, 23, 59, 30, TimeSpan.Zero);
                Check.Equal(1, s.RunDue());                  // must not throw: there is no later occurrence
                Check.Null(job.NextRun);
                lock (errors) Check.Equal(1, errors.Count);
                Check.Equal(0, s.RunDue());
            }
            finally { s.Dispose(); }
        }

        // ---- Iso8601Duration / HumanDuration ----

        public void Iso8601_NonAsciiDigits_ReturnFalse()
        {
            Check.False(Iso8601Duration.TryParse("P١D", out _));      // Arabic-Indic one
            Check.False(Iso8601Duration.TryParse("PT१H", out _));     // Devanagari one
        }

        public void Iso8601_TrailingNewline_Rejected() => Check.False(Iso8601Duration.TryParse("PT1H\n", out _));

        public void Iso8601_EmptyTimeSection_Rejected()
        {
            Check.False(Iso8601Duration.TryParse("P1DT", out _));
            Check.False(Iso8601Duration.TryParse("P1WT", out _));
        }

        public void HumanDuration_MinValue_Formats()
        {
            string s = HumanDuration.Format(TimeSpan.MinValue);
            Check.True(s.StartsWith("-10675199d 2h 48m 5s 477ms", StringComparison.Ordinal), s);
        }

        public void HumanDuration_DaysAreNotRoundedUp()
        {
            TimeSpan v = TimeSpan.FromTicks(TimeSpan.TicksPerDay * 10_000_000 - 1);
            Check.Equal("9999999d 23h 59m 59s 999ms", HumanDuration.Format(v));
        }

        // ---- BusinessDays ----

        public void BusinessDays_CountOnMaxDate()
        {
            var last = new DateTime(9999, 12, 31);
            Check.Equal(BusinessDays.IsBusinessDay(last) ? 1 : 0, BusinessDays.CountBusinessDays(last, last));
        }

        public void BusinessDays_AddIntMinValue_ThrowsArgumentOutOfRange()
        {
            Check.Throws<ArgumentOutOfRangeException>(() => BusinessDays.AddBusinessDays(new DateTime(2026, 3, 2), int.MinValue));
        }

        // ---- CidrRange ----

        public void Cidr_AddressesOnIpv6_ThrowsEagerly()
        {
            var r = CidrRange.Parse("2001:db8::/120");
            Check.Throws<InvalidOperationException>(() => r.Addresses());
        }

        public void Cidr_ShorthandIpv4_Rejected()
        {
            Check.False(CidrRange.TryParse("10/8", out _));
            Check.False(CidrRange.TryParse("10.1/16", out _));
            Check.False(CidrRange.TryParse("0x0A000000/8", out _));
            Check.True(CidrRange.TryParse("10.0.0.0/8", out _));
            Check.True(CidrRange.TryParse("::ffff:10.0.0.0/104", out _));
        }

        // ---- Security ----

        public void ConstantTime_LoneSurrogates_AreNotEqual()
        {
            Check.False(ConstantTime.Equals("\uD800", "\uDC00"));
            Check.False(ConstantTime.Equals("a\uD800", "a�"));
            Check.True(ConstantTime.Equals("héllo", "héllo"));
        }

        public void PasswordHasher_EmptyDigest_IsFormatException()
        {
            Check.Throws<FormatException>(() => PasswordHasher.Verify("pw", "PBKDF2-SHA256$1000$c2FsdHNhbHQ=$"));
            Check.Throws<FormatException>(() => PasswordHasher.Verify("pw", "PBKDF2-SHA256$1000$$c2FsdHNhbHQ="));
        }

        public void PasswordHasher_ImplausibleIterations_IsFormatException()
        {
            Check.Throws<FormatException>(() => PasswordHasher.NeedsRehash("PBKDF2-SHA256$2000000000$c2FsdHNhbHQ=$c2FsdHNhbHQ="));
        }

        public void CryptoRandom_UndefinedClassBits_ThrowArgumentException()
        {
            Check.Throws<ArgumentException>(() => CryptoRandom.Password(12, (PasswordClasses)16));
            Check.Throws<ArgumentException>(() => CryptoRandom.Password(12, (PasswordClasses)(16 | 1)));
        }

        public void CryptoRandom_HugeToken_NamesLength()
        {
            var ex = Check.Throws<ArgumentOutOfRangeException>(() => CryptoRandom.Token(400_000_000));
            Check.Equal("length", ex.ParamName);
        }

        // ---- PortCheck / HostInfo ----

        public void PortCheck_LongTimeout_StillConnects()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                Check.True(PortCheck.IsReachable("127.0.0.1", port, TimeSpan.FromDays(30)));
            }
            finally { listener.Stop(); }
        }

        public void HostInfo_LongTimeout_Resolves()
        {
            Check.True(HostInfo.Resolve("localhost", TimeSpan.FromDays(30)).Count > 0);
        }

        // ---- TcpLineClient ----

        private static (TcpListener Listener, int Port) Listen()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return (listener, ((IPEndPoint)listener.LocalEndpoint).Port);
        }

        public async Task TcpLineClient_PartialLineSurvivesReadTimeout()
        {
            var (listener, port) = Listen();
            try
            {
                Task<TcpClient> accept = listener.AcceptTcpClientAsync();
                using var client = new TcpLineClient("127.0.0.1", port, readTimeout: TimeSpan.FromMilliseconds(200));
                using TcpClient server = await accept;
                NetworkStream ns = server.GetStream();
                byte[] first = Encoding.ASCII.GetBytes("hel");
                ns.Write(first, 0, first.Length);
                await Task.Delay(50);
                Check.Throws<IOException>(() => client.ReadLine());
                byte[] rest = Encoding.ASCII.GetBytes("lo\r\nnext\n");
                ns.Write(rest, 0, rest.Length);
                Check.Equal("hello", client.ReadLine());
                Check.Equal("next", client.ReadLine());
            }
            finally { listener.Stop(); }
        }

        public async Task TcpLineClient_MaxLineLength_Enforced()
        {
            var (listener, port) = Listen();
            try
            {
                Task<TcpClient> accept = listener.AcceptTcpClientAsync();
                using var client = new TcpLineClient("127.0.0.1", port, readTimeout: TimeSpan.FromSeconds(5)) { MaxLineLength = 1000 };
                using TcpClient server = await accept;
                byte[] junk = Enumerable.Repeat((byte)'x', 5000).ToArray();
                server.GetStream().Write(junk, 0, junk.Length);
                Check.Throws<InvalidDataException>(() => client.ReadLine());
            }
            finally { listener.Stop(); }
        }

        public async Task TcpLineClient_LongTimeouts_Accepted()
        {
            var (listener, port) = Listen();
            try
            {
                Task<TcpClient> accept = listener.AcceptTcpClientAsync();
                using var client = new TcpLineClient("127.0.0.1", port,
                    connectTimeout: TimeSpan.FromDays(30), readTimeout: TimeSpan.FromDays(30), writeTimeout: TimeSpan.FromDays(30));
                using TcpClient server = await accept;
                client.Send("ping");
                var reader = new StreamReader(server.GetStream());
                Check.Equal("ping", reader.ReadLine());
            }
            finally { listener.Stop(); }
        }

        public void TcpLineClient_BadArguments_ThrowBeforeConnecting()
        {
            Check.Throws<ArgumentOutOfRangeException>(() => new TcpLineClient("127.0.0.1", 70000));
            Check.Throws<ArgumentOutOfRangeException>(() => new TcpLineClient("127.0.0.1", 1, readTimeout: TimeSpan.FromSeconds(-1)));
            Check.Throws<ArgumentOutOfRangeException>(() => new TcpLineClient("127.0.0.1", 1, connectTimeout: TimeSpan.Zero));
        }

        public async Task TcpLineClient_SendRejectsEmbeddedNewlines()
        {
            var (listener, port) = Listen();
            try
            {
                Task<TcpClient> accept = listener.AcceptTcpClientAsync();
                using var client = new TcpLineClient("127.0.0.1", port);
                using TcpClient server = await accept;
                Check.Throws<ArgumentException>(() => client.Send("a\nb"));
                Check.Throws<ArgumentException>(() => client.Send("a\rb"));
            }
            finally { listener.Stop(); }
        }

        // ---- HttpDownload ----

        private static readonly byte[] Body = Encoding.ASCII.GetBytes("hello world");

        /// <summary>A raw HTTP/1.1 server: each connection's request head goes to the handler, which writes the response.</summary>
        private sealed class RawServer : IDisposable
        {
            private readonly TcpListener _listener = new TcpListener(IPAddress.Loopback, 0);
            private int _requests;
            public string Url { get; }
            public int Requests => Volatile.Read(ref _requests);

            public RawServer(Func<string, NetworkStream, int, Task> handler)
            {
                _listener.Start();
                Url = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/file";
                _ = Task.Run(async () =>
                {
                    while (true)
                    {
                        TcpClient c;
                        try { c = await _listener.AcceptTcpClientAsync(); } catch { break; }
                        _ = Task.Run(async () =>
                        {
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
                                    int n = Interlocked.Increment(ref _requests);
                                    await handler(head.ToString(), ns, n);
                                }
                                catch { }
                            }
                        });
                    }
                });
            }

            public void Dispose() { try { _listener.Stop(); } catch { } }
        }

        private static Task Respond(NetworkStream ns, string statusLine, byte[]? body, params string[] headers)
        {
            var sb = new StringBuilder("HTTP/1.1 " + statusLine + "\r\nConnection: close\r\n");
            foreach (string h in headers) sb.Append(h).Append("\r\n");
            if (body != null) sb.Append("Content-Length: ").Append(body.Length).Append("\r\n");
            sb.Append("\r\n");
            byte[] head = Encoding.ASCII.GetBytes(sb.ToString());
            ns.Write(head, 0, head.Length);
            if (body != null) ns.Write(body, 0, body.Length);
            ns.Flush();
            return Task.CompletedTask;
        }

        private static string RangeStart(string head)
        {
            foreach (string line in head.Split(new[] { "\r\n" }, StringSplitOptions.None))
                if (line.StartsWith("Range: bytes=", StringComparison.OrdinalIgnoreCase)) return line.Substring(13).TrimEnd('-');
            return "";
        }

        private static string TempFile() => Path.Combine(Path.GetTempPath(), "toolbelt-dl13-" + Guid.NewGuid().ToString("N"));

        private static void Cleanup(string dest)
        {
            foreach (string p in new[] { dest, dest + ".part" }) try { File.Delete(p); } catch { }
        }

        public async Task Http_ResumeOfCompleteFile_416_Succeeds()
        {
            using var server = new RawServer((head, ns, n) =>
                RangeStart(head) == "11" ? Respond(ns, "416 Range Not Satisfiable", Array.Empty<byte>(), "Content-Range: bytes */11")
                                         : Respond(ns, "200 OK", Body));
            string dest = TempFile();
            try
            {
                File.WriteAllBytes(dest, Body);
                var r = await HttpDownload.ToFileAsync(server.Url, dest, new HttpDownloadOptions { Resume = true, MaxAttempts = 1 });
                Check.Equal(11L, r.BytesDownloaded);
                Check.True(File.ReadAllBytes(dest).SequenceEqual(Body));
            }
            finally { Cleanup(dest); }
        }

        public async Task Http_206FromWrongOffset_IsNotAppended()
        {
            // The server answers a Range request with 206 but the whole body (Content-Range starts at 0).
            using var server = new RawServer((head, ns, n) => Respond(ns, "206 Partial Content", Body, "Content-Range: bytes 0-10/11"));
            string dest = TempFile();
            try
            {
                File.WriteAllBytes(dest, Body.Take(6).ToArray());
                await HttpDownload.ToFileAsync(server.Url, dest, new HttpDownloadOptions { Resume = true, MaxAttempts = 1 });
                Check.True(File.ReadAllBytes(dest).SequenceEqual(Body), Encoding.ASCII.GetString(File.ReadAllBytes(dest)));
            }
            finally { Cleanup(dest); }
        }

        public async Task Http_Other2xxDuringResume_RestartsCount()
        {
            using var server = new RawServer((head, ns, n) => Respond(ns, "203 Non-Authoritative Information", Body));
            string dest = TempFile();
            try
            {
                File.WriteAllBytes(dest, Body.Take(6).ToArray());
                var r = await HttpDownload.ToFileAsync(server.Url, dest, new HttpDownloadOptions { Resume = true, MaxAttempts = 1 });
                Check.Equal(11L, r.BytesDownloaded);
                Check.False(r.Resumed);
                Check.True(File.ReadAllBytes(dest).SequenceEqual(Body));
            }
            finally { Cleanup(dest); }
        }

        public async Task Http_BodyStall_TimesOut()
        {
            using var server = new RawServer(async (head, ns, n) =>
            {
                byte[] h = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nConnection: close\r\nContent-Length: 11\r\n\r\nhello");
                ns.Write(h, 0, h.Length);
                await Task.Delay(TimeSpan.FromSeconds(30));
            });
            string dest = TempFile();
            try
            {
                Task t = HttpDownload.ToFileAsync(server.Url, dest, new HttpDownloadOptions { MaxAttempts = 1, ReadTimeout = TimeSpan.FromMilliseconds(300) });
                Task first = await Task.WhenAny(t, Task.Delay(TimeSpan.FromSeconds(10)));
                Check.True(first == t, "the download hung on a stalled body");
                await Check.ThrowsAsync<TimeoutException>(() => t);
            }
            finally { Cleanup(dest); }
        }

        public async Task Http_FailedDownload_LeavesExistingDestinationIntact()
        {
            using var server = new RawServer((head, ns, n) =>
            {
                byte[] h = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nConnection: close\r\nContent-Length: 11\r\n\r\nhel");
                ns.Write(h, 0, h.Length);
                return Task.CompletedTask;   // closes after 3 of 11 bytes
            });
            string dest = TempFile();
            try
            {
                File.WriteAllText(dest, "previous version");
                await Check.ThrowsAsync<Exception>(() => HttpDownload.ToFileAsync(server.Url, dest, new HttpDownloadOptions { MaxAttempts = 1 }));
                Check.Equal("previous version", File.ReadAllText(dest));
            }
            finally { Cleanup(dest); }
        }

        public async Task Http_UnfollowedRedirect_IsNotRetried()
        {
            using var server = new RawServer((head, ns, n) => Respond(ns, "302 Found", Array.Empty<byte>(), "Location: http://127.0.0.1:1/elsewhere"));
            using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
            string dest = TempFile();
            try
            {
                await Check.ThrowsAsync<HttpDownloadException>(() => HttpDownload.ToFileAsync(server.Url, dest,
                    new HttpDownloadOptions { Client = client, MaxAttempts = 3, RetryBaseDelay = TimeSpan.FromMilliseconds(10) }));
                Check.Equal(1, server.Requests);
            }
            finally { Cleanup(dest); }
        }

        public async Task Http_ClientTimeout_IsRetried()
        {
            using var server = new RawServer(async (head, ns, n) =>
            {
                if (n == 1) await Task.Delay(TimeSpan.FromSeconds(3));
                await Respond(ns, "200 OK", Body);
            });
            using var client = new HttpClient { Timeout = TimeSpan.FromMilliseconds(500) };
            string dest = TempFile();
            try
            {
                await HttpDownload.ToFileAsync(server.Url, dest,
                    new HttpDownloadOptions { Client = client, MaxAttempts = 3, RetryBaseDelay = TimeSpan.FromMilliseconds(10) });
                Check.True(File.ReadAllBytes(dest).SequenceEqual(Body));
                Check.True(server.Requests >= 2);
            }
            finally { Cleanup(dest); }
        }

        public async Task Http_CallerCancellation_IsNotRetried()
        {
            using var server = new RawServer(async (head, ns, n) => { await Task.Delay(TimeSpan.FromSeconds(3)); await Respond(ns, "200 OK", Body); });
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
            string dest = TempFile();
            try
            {
                await Check.ThrowsAsync<OperationCanceledException>(() => HttpDownload.ToFileAsync(server.Url, dest,
                    new HttpDownloadOptions { MaxAttempts = 3, RetryBaseDelay = TimeSpan.FromMilliseconds(10) }, cts.Token));
                Check.Equal(1, server.Requests);
            }
            finally { Cleanup(dest); }
        }
    }
}
