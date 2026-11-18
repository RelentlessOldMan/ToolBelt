using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ToolBelt.Net;
using ToolBelt.Security;
using ToolBelt.Tests.Framework;
using ToolBelt.Time;

namespace ToolBelt.Tests.Time
{
    /// <summary>Regression tests for the round 91 review (security, net, time, fifth pass).</summary>
    public sealed class SecurityNetTimeReview5Tests
    {
        // ---- CronScheduler: a clock that goes back, throws, or glitches ----

        // The loop's delay never ends, and Start waits until the loop has made its first pass and parked in it, so the
        // tests' own RunDue calls are the only passes after that.
        private sealed class Parked
        {
            public readonly ManualResetEventSlim InDelay = new ManualResetEventSlim();
            public CronScheduler Scheduler = null!;
            public void Start()
            {
                Scheduler.Start();
                Check.True(InDelay.Wait(5000), "the loop parked");
            }
        }

        private static CronScheduler Scheduler(Func<DateTimeOffset> clock, out Parked parked, List<string>? errors = null)
        {
            var p = new Parked();
            p.Scheduler = new CronScheduler(onError: (j, e) => errors?.Add(j?.Name ?? "<scheduler>"), clock: clock,
                delay: (t, ct) => { p.InDelay.Set(); return Task.Delay(Timeout.Infinite, ct); }, timeZone: TimeZoneInfo.Utc);
            parked = p;
            return p.Scheduler;
        }

        // RunDue, then waits for the started run to finish, so the next tick isn't skipped as an overlap.
        private static int Tick(CronScheduler s, ScheduledJob job)
        {
            int started = s.RunDue();
            Check.True(SpinWait.SpinUntil(() => !job.IsRunning, 5000), "the job finished");
            return started;
        }

        public void Scheduler_AClockCorrectedBackAYearKeepsRunning()
        {
            DateTimeOffset now = new DateTimeOffset(2027, 1, 1, 10, 0, 0, TimeSpan.Zero);   // a year fast
            using var s = Scheduler(() => now, out Parked loop);
            ScheduledJob job = s.Add("hourly", "0 * * * *", () => { });
            loop.Start();
            now = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);   // corrected
            int started = 0;
            for (int h = 1; h <= 48; h++) { now = now.AddHours(1); started += Tick(s, job); }
            Check.True(started >= 47, $"started {started} of 48 hourly runs");
            Check.True(job.NextRun is DateTimeOffset n && n.Year == 2026, $"NextRun {job.NextRun}");
        }

        // Round 124: a fixed-time job (an hourly "0 * * * *" is a wildcard job in cron, which runs by the clock).
        public void Scheduler_AShortStepBackDoesNotRepeatARun()
        {
            DateTimeOffset now = new DateTimeOffset(2026, 1, 1, 9, 59, 0, TimeSpan.Zero);
            using var s = Scheduler(() => now, out Parked loop);
            ScheduledJob job = s.Add("daily", "0 10 * * *", () => { });
            loop.Start();
            now = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
            Check.Equal(1, Tick(s, job));
            now = new DateTimeOffset(2026, 1, 1, 9, 30, 0, TimeSpan.Zero);   // a 30-minute correction
            Check.Equal(0, Tick(s, job));
            now = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
            Check.Equal(0, Tick(s, job));   // 10:00 already ran
            now = new DateTimeOffset(2026, 1, 2, 10, 0, 0, TimeSpan.Zero);
            Check.Equal(1, Tick(s, job));
        }

        public void Scheduler_AClockThatThrowsDuringAddIsRetried()
        {
            DateTimeOffset now = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
            bool fail = false;
            var errors = new List<string>();
            using var s = Scheduler(() => fail ? throw new InvalidOperationException("clock glitch") : now, out Parked loop, errors);
            loop.Start();
            fail = true;
            loop.InDelay.Reset();
            ScheduledJob job = s.Add("hourly", "0 * * * *", () => { });
            // Add wakes the loop (round 142), whose pass meets the same glitch; let it park again before the clock heals.
            Check.True(loop.InDelay.Wait(5000), "the loop parked again");
            fail = false;
            Check.True(errors.Count >= 1 && errors.All(e => e == "<scheduler>"), string.Join(",", errors));
            int started = 0;
            for (int h = 0; h < 24; h++) { now = now.AddHours(1); started += Tick(s, job); }
            Check.True(started >= 23, $"started {started}");
            Check.True(job.NextRun.HasValue);
        }

        public void Scheduler_AGlitchToTheCalendarsEndIsRecovered()
        {
            DateTimeOffset now = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
            var errors = new List<string>();
            using var s = Scheduler(() => now, out Parked loop, errors);
            ScheduledJob job = s.Add("hourly", "0 * * * *", () => { });
            loop.Start();
            now = new DateTimeOffset(9999, 12, 31, 23, 59, 30, TimeSpan.Zero);
            Tick(s, job);
            now = new DateTimeOffset(2026, 1, 1, 11, 0, 0, TimeSpan.Zero);
            int started = 0;
            for (int h = 0; h < 24; h++) { now = now.AddHours(1); started += Tick(s, job); }
            Check.True(started >= 23, $"started {started}");
            Check.True(job.NextRun.HasValue);
        }

        public void Scheduler_JitterBackAroundAnAddKeepsADueRun()
        {
            DateTimeOffset now = new DateTimeOffset(2026, 1, 1, 10, 1, 0, 10, TimeSpan.Zero);
            using var s = Scheduler(() => now, out Parked loop);
            loop.Start();
            Check.Equal(0, s.RunDue());
            now = new DateTimeOffset(2026, 1, 1, 10, 0, 59, 990, TimeSpan.Zero);
            ScheduledJob job = s.Add("minutely", "* * * * *", () => { });
            Check.Equal(new DateTimeOffset(2026, 1, 1, 10, 1, 0, TimeSpan.Zero), job.NextRun);
            now = new DateTimeOffset(2026, 1, 1, 10, 1, 0, 5, TimeSpan.Zero);   // back 5 ms from the last pass
            Check.Equal(1, Tick(s, job));
            Check.Equal(new DateTimeOffset(2026, 1, 1, 10, 2, 0, TimeSpan.Zero), job.NextRun);
        }

        // ---- CronSchedule: schedules that recur less often than every 8 years ----

        // Midnight on day dom of month mon whose weekday is in the */step set, found a day at a time.
        private static DateTimeOffset Naive(DateTimeOffset after, int dom, int mon, int step)
        {
            for (DateTime d = after.DateTime.Date.AddDays(1); ; d = d.AddDays(1))
                if (d.Day == dom && d.Month == mon && (int)d.DayOfWeek % step == 0)
                    return new DateTimeOffset(d, after.Offset);
        }

        public void Cron_RareButValidSchedulesAreFound()
        {
            var rng = new Random(91);
            var pacific = TimeZoneInfo.CreateCustomTimeZone("Minus8", TimeSpan.FromHours(-8), "Minus8", "Minus8");
            for (int trial = 0; trial < 200; trial++)
            {
                int step = trial < 2 ? (trial == 0 ? 6 : 7) : rng.Next(1, 8);
                int mon = trial < 2 ? 2 : rng.Next(1, 13);
                int dom = trial < 2 ? 29 : rng.Next(1, mon == 2 ? 30 : 31);
                var after = new DateTimeOffset(rng.Next(1900, 2300), rng.Next(1, 13), rng.Next(1, 29), 0, 0, 0, TimeSpan.Zero);
                var cron = new CronSchedule($"0 0 {dom} {mon} */{step}");
                DateTimeOffset want = Naive(after, dom, mon, step);
                Check.Equal(want, cron.GetNextOccurrence(after), $"0 0 {dom} {mon} */{step} after {after}");
                Check.Equal(want, cron.GetNextOccurrence(after, TimeZoneInfo.Utc), $"zone: 0 0 {dom} {mon} */{step} after {after}");
                DateTimeOffset localAfter = after.ToOffset(TimeSpan.FromHours(-8));
                Check.Equal(Naive(localAfter, dom, mon, step), cron.GetNextOccurrence(localAfter, pacific), $"pacific: 0 0 {dom} {mon} */{step} after {after}");
            }
        }

        public void Cron_FebruaryTwentyNinthOnAWeekendIsSixteenYearsAway()
        {
            var cron = new CronSchedule("0 0 29 2 */6");
            var after = new DateTimeOffset(2032, 3, 1, 0, 0, 0, TimeSpan.Zero);   // 2032-02-29 was a Sunday
            Check.Equal(new DateTimeOffset(2048, 2, 29, 0, 0, 0, TimeSpan.Zero), cron.GetNextOccurrence(after));
            Check.Equal(new DateTimeOffset(2048, 2, 29, 0, 0, 0, TimeSpan.Zero), cron.GetNextOccurrence(after, TimeZoneInfo.Utc));
        }

        public void Cron_AnImpossibleScheduleStillThrowsQuickly()
        {
            var cron = new CronSchedule("0 0 30 2 *");
            var watch = System.Diagnostics.Stopwatch.StartNew();
            Check.Throws<InvalidOperationException>(() => cron.GetNextOccurrence(new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero)));
            Check.Throws<InvalidOperationException>(() => cron.GetNextOccurrence(new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero), TimeZoneInfo.Utc));
            Check.True(watch.ElapsedMilliseconds < 5000, $"{watch.ElapsedMilliseconds} ms");
        }

        // ---- Iso8601Duration: the decimal comma ----

        public void Iso_TheDecimalCommaIsAccepted()
        {
            Check.Equal(TimeSpan.FromSeconds(1.5), Iso8601Duration.Parse("PT1,5S"));
            Check.Equal(TimeSpan.FromMilliseconds(250), Iso8601Duration.Parse("PT0,25S"));
            Check.Equal(Iso8601Duration.Parse("P1DT2H3M4.125S"), Iso8601Duration.Parse("P1DT2H3M4,125S"));
            Check.False(Iso8601Duration.TryParse("PT1,S", out _));
            Check.False(Iso8601Duration.TryParse("PT,5S", out _));
            Check.False(Iso8601Duration.TryParse("PT1,,5S", out _));
        }

        // ---- CidrRange ----

        public void Cidr_AnIpv4MappedAddressMatchesItsIpv4Block()
        {
            var block = CidrRange.Parse("203.0.113.0/24");
            Check.True(block.Contains(IPAddress.Parse("203.0.113.7").MapToIPv6()));
            Check.False(block.Contains(IPAddress.Parse("203.0.114.7").MapToIPv6()));
            Check.True(CidrRange.Parse("::ffff:0:0/96").Contains(IPAddress.Parse("203.0.113.7").MapToIPv6()));
            Check.False(CidrRange.Parse("::ffff:0:0/96").Contains(IPAddress.Parse("203.0.113.7")));
            Check.False(block.Contains(IPAddress.Parse("2001:db8::cb00:7107")));
        }

        public void Cidr_ParseNullThrowsArgumentNull()
        {
            Check.Equal("text", Check.Throws<ArgumentNullException>(() => CidrRange.Parse(null!)).ParamName);
        }

        // ---- TcpLineClient: bad bytes under a strict encoding ----

        private static void WithServer(Encoding encoding, byte[][] parts, Action<TcpLineClient> read)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                Task server = Task.Run(async () =>
                {
                    using TcpClient conn = await listener.AcceptTcpClientAsync();
                    using NetworkStream s = conn.GetStream();
                    foreach (byte[] part in parts)
                    {
                        s.Write(part, 0, part.Length);
                        s.Flush();
                        await Task.Delay(150);   // so each part is a separate read
                    }
                    conn.Client.Shutdown(SocketShutdown.Send);
                    await Task.Delay(200);
                });
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                using (var client = new TcpLineClient("127.0.0.1", port, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5), encoding: encoding))
                    read(client);
                server.Wait(TimeSpan.FromSeconds(5));
            }
            finally { listener.Stop(); }
        }

        private static byte[] Utf8(string s) => Encoding.UTF8.GetBytes(s);
        private static byte[] Join(params byte[][] parts) => parts.SelectMany(p => p).ToArray();

        public void Tcp_ACharacterSplitAcrossReadsSurvivesABadByteLater()
        {
            var strict = new UTF8Encoding(false, true);
            WithServer(strict, new[]
            {
                Join(Utf8("a\nx"), new byte[] { 0xE2, 0x82 }),
                Join(new byte[] { 0xAC }, Utf8("\nb"), new byte[] { 0xFF }, Utf8("\nc\n")),
            }, c =>
            {
                Check.Equal("a", c.ReadLine());
                Check.Equal("x\u20AC", c.ReadLine());
                Check.Throws<DecoderFallbackException>(() => c.ReadLine());
                Check.Equal("c", c.ReadLine());
                Check.Null(c.ReadLine());
            });
        }

        public void Tcp_ALoneUtf16SurrogateDropsOnlyItsLine()
        {
            var strict = new UnicodeEncoding(false, false, true);
            WithServer(strict, new[]
            {
                Join(strict.GetBytes("ab\n"), new byte[] { 0x00, 0xD8 }, strict.GetBytes("c\ndef\n")),
            }, c =>
            {
                Check.Equal("ab", c.ReadLine());
                Check.Throws<DecoderFallbackException>(() => c.ReadLine());
                Check.Equal("def", c.ReadLine());
                Check.Null(c.ReadLine());
            });
        }

        public void Tcp_ALineWithManyBadBytesThrowsOnce()
        {
            var strict = new UTF8Encoding(false, true);
            WithServer(strict, new[] { Join(Utf8("a\n"), new byte[] { 0xFF, 0xFE, 0xFF, 0x80, 0xC0 }, Utf8("q\nb\n")) }, c =>
            {
                Check.Equal("a", c.ReadLine());
                Check.Throws<DecoderFallbackException>(() => c.ReadLine());
                Check.Equal("b", c.ReadLine());
                Check.Null(c.ReadLine());
            });
        }

        public void Tcp_BadBytesInsideAnOverlongLineAreReportedOnce()
        {
            var strict = new UTF8Encoding(false, true);
            WithServer(strict, new[] { Join(Utf8("abcdef"), new byte[] { 0xFF }, Utf8("gh\nok\n")) }, c =>
            {
                c.MaxLineLength = 3;
                Check.Throws<InvalidDataException>(() => c.ReadLine());
                Check.Equal("ok", c.ReadLine());
                Check.Null(c.ReadLine());
            });
        }

        // ---- HttpDownload: a body longer than its declared total ----

        private static (int port, TcpListener listener) RawServer(Func<Stream, Task> respond)
        {
            var l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            _ = Task.Run(async () =>
            {
                while (true)
                {
                    TcpClient c;
                    try { c = await l.AcceptTcpClientAsync(); } catch { return; }
                    _ = Task.Run(async () =>
                    {
                        using (c)
                        {
                            var s = c.GetStream();
                            var head = new StringBuilder();
                            var one = new byte[1];
                            while (!head.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
                            {
                                if (await s.ReadAsync(one, 0, 1) == 0) return;
                                head.Append((char)one[0]);
                            }
                            try { await respond(s); } catch { }
                        }
                    });
                }
            });
            return (((IPEndPoint)l.LocalEndpoint).Port, l);
        }

        public async Task Download_ABodyPastItsDeclaredTotalIsRefused()
        {
            var (port, listener) = RawServer(async s =>
            {
                byte[] head = Encoding.ASCII.GetBytes("HTTP/1.1 206 Partial Content\r\nContent-Range: bytes 5-9/10\r\nTransfer-Encoding: chunked\r\nConnection: close\r\n\r\n");
                await s.WriteAsync(head, 0, head.Length);
                byte[] chunk = new byte[65536];
                byte[] size = Encoding.ASCII.GetBytes("10000\r\n"), crlf = Encoding.ASCII.GetBytes("\r\n");
                for (int i = 0; i < 16; i++)
                {
                    await s.WriteAsync(size, 0, size.Length);
                    await s.WriteAsync(chunk, 0, chunk.Length);
                    await s.WriteAsync(crlf, 0, crlf.Length);
                }
                byte[] end = Encoding.ASCII.GetBytes("0\r\n\r\n");
                await s.WriteAsync(end, 0, end.Length);
            });
            string dest = Path.Combine(Path.GetTempPath(), "toolbelt-r91-" + Guid.NewGuid().ToString("N") + ".bin");
            File.WriteAllBytes(dest + ".part", new byte[5]);
            long most = 0;
            try
            {
                await Check.ThrowsAsync<HttpDownloadException>(() => HttpDownload.ToFileAsync($"http://127.0.0.1:{port}/f", dest,
                    new HttpDownloadOptions { Resume = true, MaxAttempts = 1, OnProgress = (d, t) => most = Math.Max(most, d) }));
                Check.True(most <= 10, $"reported {most} of 10 bytes");
                Check.False(File.Exists(dest + ".part"));
                Check.False(File.Exists(dest));
            }
            finally
            {
                listener.Stop();
                try { File.Delete(dest + ".part"); } catch { }
                try { File.Delete(dest); } catch { }
            }
        }

        // ---- SecretsFile: contents that authenticate but are malformed ----

#if NET8_0_OR_GREATER
        // A file in SecretsFile's format (magic, iterations, salt, nonce; the header is the associated data; tag, then
        // the ciphertext) holding the given plaintext, so its contents authenticate whatever they are.
        private static byte[] Sealed(byte[] plaintext, string passphrase)
        {
            byte[] magic = Encoding.ASCII.GetBytes("TBSECRETS1");
            const int iterations = 1000;
            byte[] salt = new byte[16], nonce = new byte[12];
            for (int i = 0; i < salt.Length; i++) salt[i] = (byte)(i + 1);
            for (int i = 0; i < nonce.Length; i++) nonce[i] = (byte)(i + 7);
            var header = new byte[magic.Length + 4 + salt.Length + nonce.Length];
            magic.CopyTo(header, 0);
            BitConverter.GetBytes(iterations).CopyTo(header, magic.Length);
            salt.CopyTo(header, magic.Length + 4);
            nonce.CopyTo(header, magic.Length + 4 + salt.Length);
            byte[] key = Rfc2898DeriveBytes.Pbkdf2(passphrase, salt, iterations, HashAlgorithmName.SHA256, 32);
            var cipher = new byte[plaintext.Length];
            var tag = new byte[16];
            using (var gcm = new AesGcm(key, 16)) gcm.Encrypt(nonce, plaintext, cipher, tag, header);
            return header.Concat(tag).Concat(cipher).ToArray();
        }

        public void Secrets_TheHandBuiltFormatLoads()
        {
            SecretsFile f = SecretsFile.FromBytes(Sealed(Utf8("k=YQ==\n"), "pw"), "pw");
            Check.Equal(1, f.Count);
        }

        public void Secrets_MalformedContentsThrowCryptographicException()
        {
            foreach (byte[] plain in new[]
            {
                Utf8("novalue\n"), Utf8("k=***\n"), Utf8("=YQ==\n"), Utf8("k=YQ==\nk=Yg==\n"),
                new byte[] { 0xFF, (byte)'=', (byte)'\n' }, Utf8("k=/w==\n"),   // not UTF-8: the key, then the value
            })
                Check.Throws<CryptographicException>(() => SecretsFile.FromBytes(Sealed(plain, "pw"), "pw"), Encoding.UTF8.GetString(plain));
        }
#endif
    }
}
