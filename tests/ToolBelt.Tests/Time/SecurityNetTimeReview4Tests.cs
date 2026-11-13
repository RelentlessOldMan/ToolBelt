using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
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
    /// <summary>Regression tests for the round 64 review (security, net, time, fourth pass).</summary>
    public sealed class SecurityNetTimeReview4Tests
    {
        // ---- CronSchedule: the zone overload in the calendar's last minute ----

        public void Cron_TheZoneOverloadInTheLastMinuteThrowsInvalidOperation()
        {
            var every = new CronSchedule("* * * * *");
            var pacific = TimeZoneInfo.CreateCustomTimeZone("Minus8", TimeSpan.FromHours(-8), "Minus8", "Minus8");
            foreach (TimeZoneInfo zone in new[] { TimeZoneInfo.Utc, pacific })
            {
                Check.Throws<InvalidOperationException>(() => every.GetNextOccurrence(DateTimeOffset.MaxValue, zone));
                Check.Throws<InvalidOperationException>(() => every.GetNextOccurrence(new DateTimeOffset(9999, 12, 31, 23, 59, 10, TimeSpan.Zero), zone));
            }
            Check.Equal(new DateTimeOffset(9999, 12, 31, 23, 59, 0, TimeSpan.Zero),
                every.GetNextOccurrence(new DateTimeOffset(9999, 12, 31, 23, 58, 30, TimeSpan.Zero), TimeZoneInfo.Utc));
        }

        // ---- CronScheduler ----

        public void Scheduler_StatusReadsAreNeverTorn()
        {
            var scheduler = new CronScheduler(clock: () => new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero), delay: (t, ct) => Task.Delay(Timeout.Infinite, ct), timeZone: TimeZoneInfo.Utc);
            ScheduledJob job = scheduler.Add("j", "* * * * *", () => { });
            var set = (Action<ScheduledJob, DateTimeOffset?>)Delegate.CreateDelegate(typeof(Action<ScheduledJob, DateTimeOffset?>),
                typeof(ScheduledJob).GetProperty(nameof(ScheduledJob.NextRun))!.GetSetMethod(nonPublic: true)!);
            var a = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.FromHours(-8));
            var b = new DateTimeOffset(2027, 7, 1, 12, 30, 0, TimeSpan.FromHours(5));
            bool stop = false;
            var writer = new Thread(() => { while (!Volatile.Read(ref stop)) { set(job, a); set(job, b); set(job, null); } }) { IsBackground = true };
            writer.Start();
            int torn = 0;
            var watch = Stopwatch.StartNew();
            try
            {
                while (watch.ElapsedMilliseconds < 1000 && torn == 0)
                {
                    DateTimeOffset? v = job.NextRun;
                    if (v is DateTimeOffset d && !(d.EqualsExact(a) || d.EqualsExact(b))) torn++;
                }
            }
            finally { Volatile.Write(ref stop, true); writer.Join(); }
            Check.Equal(0, torn);
        }

        private sealed class NeverRuns : SynchronizationContext
        {
            public override void Post(SendOrPostCallback d, object? state) { }
            public override void Send(SendOrPostCallback d, object? state) { }
        }

        public void Scheduler_RunDueOnABlockedContextDoesNotDeadlockDispose()
        {
            var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var clock = new[] { start };
            var scheduler = new CronScheduler(clock: () => clock[0], delay: (t, ct) => Task.Delay(Timeout.Infinite, ct), timeZone: TimeZoneInfo.Utc);
            int ran = 0;
            scheduler.Add("j", "* * * * *", () => Interlocked.Increment(ref ran));
            scheduler.Start();
            clock[0] = start.AddMinutes(5);
            SynchronizationContext? previous = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(new NeverRuns());   // a UI thread that is about to block
            try { Check.Equal(1, scheduler.RunDue()); }
            finally { SynchronizationContext.SetSynchronizationContext(previous); }
            Check.True(Task.Run(() => scheduler.Dispose()).Wait(TimeSpan.FromSeconds(10)), "Dispose never returned");
            Check.Equal(1, Volatile.Read(ref ran));
        }

        // ---- TcpLineClient: a strict encoding's decode errors ----

        private static Task Serve(TcpListener listener, byte[] payload) => Task.Run(async () =>
        {
            using TcpClient conn = await listener.AcceptTcpClientAsync();
            using NetworkStream s = conn.GetStream();
            s.Write(payload, 0, payload.Length);
            conn.Client.Shutdown(SocketShutdown.Send);
            await Task.Delay(200);
        });

        private static void WithServer(byte[] payload, Action<TcpLineClient> read)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                Task server = Serve(listener, payload);
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                using (var client = new TcpLineClient("127.0.0.1", port, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5),
                           encoding: new UTF8Encoding(false, throwOnInvalidBytes: true)))
                    read(client);
                server.Wait(TimeSpan.FromSeconds(5));
            }
            finally { listener.Stop(); }
        }

        private static byte[] Bytes(params object[] parts)
        {
            var all = new System.Collections.Generic.List<byte>();
            foreach (object p in parts) all.AddRange(p is string s ? Encoding.UTF8.GetBytes(s) : (byte[])p);
            return all.ToArray();
        }

        public void Tcp_ABadByteDropsOnlyItsLine()
        {
            WithServer(Bytes("abc\n", new byte[] { 0xFF }, "def\nghi\n"), c =>
            {
                Check.Equal("abc", c.ReadLine());
                Check.Throws<DecoderFallbackException>(() => c.ReadLine());
                Check.Equal("ghi", c.ReadLine());
                Check.Null(c.ReadLine());
            });
        }

        public void Tcp_AnUnfinishedCharacterBeforeALineBreakKeepsTheBreak()
        {
            WithServer(Bytes("a\nb", new byte[] { 0xE2 }, "\nc\n"), c =>
            {
                Check.Equal("a", c.ReadLine());
                Check.Throws<DecoderFallbackException>(() => c.ReadLine());
                Check.Equal("c", c.ReadLine());
                Check.Null(c.ReadLine());
            });
        }

        public void Tcp_AStreamEndingInsideACharacterThrowsOnceThenEnds()
        {
            WithServer(Bytes("ab\n", new byte[] { 0xE2, 0x82 }), c =>
            {
                Check.Equal("ab", c.ReadLine());
                Check.Throws<DecoderFallbackException>(() => c.ReadLine());
                Check.Null(c.ReadLine());
                Check.Null(c.ReadLine());
            });
        }

        public void Tcp_ValidMultibyteTextAcrossReadsIsUnchanged()
        {
            string line = string.Concat(Enumerable.Repeat("€\U0001F600x", 5000));   // splits characters across 4 KB reads
            WithServer(Bytes(line, "\n", line, "\n"), c =>
            {
                Check.Equal(line, c.ReadLine());
                Check.Equal(line, c.ReadLine());
                Check.Null(c.ReadLine());
            });
        }

        // ---- HttpDownload ----

        private sealed class OneFileServer : IDisposable
        {
            private readonly HttpListener _listener = new HttpListener();
            public string Url { get; }

            public OneFileServer(byte[] body)
            {
                int port = PortCheck.FindFreePort();
                Url = $"http://localhost:{port}/file";
                _listener.Prefixes.Add($"http://localhost:{port}/");
                _listener.Start();
                _ = Task.Run(async () =>
                {
                    while (_listener.IsListening)
                    {
                        HttpListenerContext ctx;
                        try { ctx = await _listener.GetContextAsync(); }
                        catch { break; }
                        ctx.Response.ContentLength64 = body.Length;
                        ctx.Response.OutputStream.Write(body, 0, body.Length);
                        ctx.Response.Close();
                    }
                });
            }

            public void Dispose() { try { _listener.Stop(); _listener.Close(); } catch { } }
        }

        private static string TempPath() => Path.Combine(Path.GetTempPath(), "toolbelt-r64-" + Guid.NewGuid().ToString("N"));

        public async Task Download_KeepsTheDestinationsAttributes()
        {
            byte[] body = Encoding.ASCII.GetBytes("new contents");
            using var server = new OneFileServer(body);
            string dest = TempPath();
            try
            {
                File.WriteAllText(dest, "old");
                File.SetAttributes(dest, FileAttributes.Hidden);
                await HttpDownload.ToFileAsync(server.Url, dest);
                Check.True(File.ReadAllBytes(dest).SequenceEqual(body));
                Check.True((File.GetAttributes(dest) & FileAttributes.Hidden) != 0, File.GetAttributes(dest).ToString());
            }
            finally
            {
                if (File.Exists(dest)) { File.SetAttributes(dest, FileAttributes.Normal); File.Delete(dest); }
            }
        }

        public async Task Download_ALocalFileFailureIsNotRetried()
        {
            string notADirectory = TempPath();
            File.WriteAllText(notADirectory, "x");
            try
            {
                var options = new HttpDownloadOptions { MaxAttempts = 3, RetryBaseDelay = TimeSpan.FromSeconds(3) };
                var watch = Stopwatch.StartNew();
                await Check.ThrowsAsync<IOException>(() => HttpDownload.ToFileAsync("http://127.0.0.1:9/x", Path.Combine(notADirectory, "f.bin"), options));
                Check.True(watch.Elapsed < TimeSpan.FromSeconds(2.5), $"took {watch.Elapsed}: the attempt was retried");
            }
            finally { File.Delete(notADirectory); }
        }

        // ---- UnixTime: local times near the calendar's ends ----

        private static long ExpectedSeconds(DateTime v)
        {
            long utcTicks = v.Kind == DateTimeKind.Utc ? v.Ticks : v.Ticks - TimeZoneInfo.Local.GetUtcOffset(v).Ticks;
            return (long)Math.Floor((decimal)utcTicks / TimeSpan.TicksPerSecond) - 62135596800L;
        }

        private static long ExpectedMilliseconds(DateTime v)
        {
            long utcTicks = v.Kind == DateTimeKind.Utc ? v.Ticks : v.Ticks - TimeZoneInfo.Local.GetUtcOffset(v).Ticks;
            return (long)Math.Floor((decimal)utcTicks / TimeSpan.TicksPerMillisecond) - 62135596800000L;
        }

        public void Unix_LocalTimesNearTheCalendarEndsAreNotClamped()
        {
            var cases = new[]
            {
                new DateTime(9999, 12, 31, 20, 0, 0, DateTimeKind.Local),
                new DateTime(9999, 12, 31, 12, 0, 0, DateTimeKind.Unspecified),
                DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Local),
                DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Local),
                new DateTime(1, 1, 1, 3, 0, 0, 1, DateTimeKind.Local),
                new DateTime(1, 1, 1, 0, 0, 0, DateTimeKind.Unspecified).AddTicks(1),
            };
            foreach (DateTime v in cases)
            {
                Check.Equal(ExpectedSeconds(v), UnixTime.ToUnixSeconds(v), v.ToString("o"));
                Check.Equal(ExpectedMilliseconds(v), UnixTime.ToUnixMilliseconds(v), v.ToString("o"));
            }
            Check.True(UnixTime.ToUnixSeconds(DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Local))
                != UnixTime.ToUnixSeconds(new DateTime(9999, 12, 31, 20, 0, 0, DateTimeKind.Local))
                || TimeZoneInfo.Local.GetUtcOffset(DateTime.MaxValue) >= TimeSpan.Zero);
        }

        public void Unix_MidRangeLocalTimesMatchToUniversalTime()
        {
            var rng = new System.Random(64);
            for (int i = 0; i < 2000; i++)
            {
                long ticks = (long)(rng.NextDouble() * (DateTime.MaxValue.Ticks - 10 * TimeSpan.TicksPerDay)) + 5 * TimeSpan.TicksPerDay;
                var v = new DateTime(ticks, i % 2 == 0 ? DateTimeKind.Local : DateTimeKind.Unspecified);
                Check.Equal(new DateTimeOffset(v.ToUniversalTime()).ToUnixTimeSeconds(), UnixTime.ToUnixSeconds(v));
                Check.Equal(new DateTimeOffset(v.ToUniversalTime()).ToUnixTimeMilliseconds(), UnixTime.ToUnixMilliseconds(v));
            }
        }

        // ---- small ones ----

        public void IpUtils_AScopedLoopbackIsLoopback()
        {
            Check.True(IpUtils.IsLoopback(IPAddress.Parse("::1%1")));
            Check.True(IpUtils.IsLoopback(IPAddress.Parse("::1")));
            Check.False(IpUtils.IsLoopback(IPAddress.Parse("fe80::1%1")));
        }

        public void RelativeTime_UnderAYearIsNeverTwelveMonths()
        {
            var now = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
            Check.Equal("11 months ago", RelativeTime.Format(now.AddDays(-362), now));
            Check.Equal("in 11 months", RelativeTime.Format(now.AddDays(364.5), now));
            Check.Equal("1 year ago", RelativeTime.Format(now.AddDays(-365), now));
            for (int d = 0; d < 365; d++) Check.False(RelativeTime.Format(now.AddDays(-d), now).StartsWith("12 month"), d.ToString());
        }

        public void HumanDuration_WhitespaceBeforeTheSign()
        {
            Check.Equal(TimeSpan.FromMinutes(-5), HumanDuration.Parse(" -5m"));
            Check.Equal(-TimeSpan.FromMinutes(90), HumanDuration.Parse("\t -1h 30m"));
            Check.Equal(TimeSpan.FromMinutes(5), HumanDuration.Parse(" 5m"));
            Check.False(HumanDuration.TryParse("   ", out _));
        }

        public void TimeOfDayRange_ToStringKeepsSeconds()
        {
            Check.Equal("[09:00:30, 09:00:45)", new TimeOfDayRange(new TimeSpan(9, 0, 30), new TimeSpan(9, 0, 45)).ToString());
            Check.Equal("[09:00:00.5, 10:00)", new TimeOfDayRange(new TimeSpan(9, 0, 0) + TimeSpan.FromMilliseconds(500), new TimeSpan(10, 0, 0)).ToString());
            Check.Equal("[22:00, 06:00)", new TimeOfDayRange(new TimeSpan(22, 0, 0), new TimeSpan(6, 0, 0)).ToString());
        }

        public void CryptoRandom_HugeLengthsThrowArgumentOutOfRange()
        {
            Check.Equal("digits", Check.Throws<ArgumentOutOfRangeException>(() => CryptoRandom.NumericCode(int.MaxValue)).ParamName);
            Check.Equal("length", Check.Throws<ArgumentOutOfRangeException>(() => CryptoRandom.Password(int.MaxValue)).ParamName);
            Check.Equal(6, CryptoRandom.NumericCode(6).Length);
        }

#if !NETFRAMEWORK
        public void Aead_DecryptToStringRejectsInvalidUtf8()
        {
            var key = new byte[32];
            byte[] message = AuthenticatedEncryption.Encrypt(key, new byte[] { 0x61, 0xFF, 0x62 });
            Check.Throws<DecoderFallbackException>(() => AuthenticatedEncryption.DecryptToString(key, message));
            Check.Equal("a€", AuthenticatedEncryption.DecryptToString(key, AuthenticatedEncryption.Encrypt(key, "a€")));
        }

        public void Secrets_NullKeyOrPassphraseThrowsArgumentNull()
        {
            var store = new SecretsFile();
            Check.Equal(typeof(ArgumentNullException), Check.Throws<ArgumentException>(() => store.Set(null!, "v")).GetType());
            Check.Equal(typeof(ArgumentNullException), Check.Throws<ArgumentException>(() => store.ToBytes(null!)).GetType());
            Check.Equal(typeof(ArgumentNullException), Check.Throws<ArgumentException>(() => SecretsFile.FromBytes(new byte[64], null!)).GetType());
            Check.Equal("passphrase", Check.Throws<ArgumentException>(() => store.ToBytes(null!)).ParamName);
        }

        public void Secrets_SaveKeepsTheFileAttributes()
        {
            string path = TempPath();
            try
            {
                var store = new SecretsFile();
                store.Set("k", "v1");
                store.Save(path, "pass", 1000);
                File.SetAttributes(path, FileAttributes.Hidden);
                store.Set("k", "v2");
                store.Save(path, "pass", 1000);
                Check.True((File.GetAttributes(path) & FileAttributes.Hidden) != 0, File.GetAttributes(path).ToString());
                Check.True(SecretsFile.Load(path, "pass").TryGet("k", out string? v) && v == "v2");
            }
            finally
            {
                if (File.Exists(path)) { File.SetAttributes(path, FileAttributes.Normal); File.Delete(path); }
            }
        }
#endif
    }
}
