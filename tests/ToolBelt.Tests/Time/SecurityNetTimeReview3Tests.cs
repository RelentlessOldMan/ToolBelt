using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using ToolBelt.Cli;
using ToolBelt.Collections;
using ToolBelt.Net;
using ToolBelt.Security;
using ToolBelt.Tests.Framework;
using ToolBelt.Time;

namespace ToolBelt.Tests.Time
{
    /// <summary>
    /// Regression tests for the round 48 review (security, net, time, third pass). The checked-build ones (unsigned index
    /// tricks, Pbkdf2 past block 255) fail only in the overflow-checked build; the IPv6 connect only on .NET Framework.
    /// </summary>
    public sealed class SecurityNetTimeReview3Tests
    {
        // ---- lone surrogates: the lenient UTF-8 encoder made them U+FFFD, so different strings collided ----

        public void PasswordHasher_LoneSurrogateThrowsRatherThanMatchingAnother()
        {
            string stored = PasswordHasher.Hash("pw�", 1);
            var ex = Check.Throws<ArgumentException>(() => PasswordHasher.Verify("pw\uD800", stored));
            Check.Equal("password", ex.ParamName);
            Check.Equal("password", Check.Throws<ArgumentException>(() => PasswordHasher.Hash("\uDFFF", 1)).ParamName);
            Check.True(PasswordHasher.Verify("pw�", stored));
            Check.True(PasswordHasher.Verify("p\U0001F600", PasswordHasher.Hash("p\U0001F600", 1)));   // a pair is fine
        }

        public void KeyDerivation_LoneSurrogateThrows()
        {
            byte[] salt = new byte[16];
            Check.Equal("password", Check.Throws<ArgumentException>(() => KeyDerivation.Pbkdf2("x\uDC00", salt, 1, 32)).ParamName);
            Check.Equal("password", Check.Throws<ArgumentException>(() => KeyDerivation.Pbkdf2("\uD800x", salt, 1, 32)).ParamName);
        }

        public void HashingAndHmac_LoneSurrogateThrows()
        {
            Check.Equal("text", Check.Throws<ArgumentException>(() => Hashing.Sha256("a\uD800")).ParamName);
            Check.Equal("text", Check.Throws<ArgumentException>(() => Hashing.Sha512("\uDFFF")).ParamName);
            Check.Equal("text", Check.Throws<ArgumentException>(() => Hmac.Sha256(new byte[32], "\uDFFF")).ParamName);
            Check.Equal(Hashing.ToHex(Hashing.Sha256(new byte[] { 0xF0, 0x9F, 0x98, 0x80 })), Hashing.ToHex(Hashing.Sha256("\U0001F600")));
        }

        public void AuthenticatedEncryption_LoneSurrogateThrows()
        {
            var key = new byte[32];
            Check.Equal("plaintext", Check.Throws<ArgumentException>(() => AuthenticatedEncryption.Encrypt(key, "\uD800")).ParamName);
        }

        public void SecretsFile_LoneSurrogatePassphraseIsAnArgumentException()
        {
            var file = new SecretsFile();
            file.Set("k", "v");
            Check.Equal("passphrase", Check.Throws<ArgumentException>(() => file.ToBytes("pp\uD800", 1000)).ParamName);
            byte[] data = file.ToBytes("pp", 1000);
            Check.Equal("passphrase", Check.Throws<ArgumentException>(() => SecretsFile.FromBytes(data, "\uD800")).ParamName);
        }

        // ---- checked build ----

        public void Pbkdf2_PastBlock255MatchesTheShorterKey()
        {
            // Block 256's index byte (byte)256 threw OverflowException in the checked build.
            byte[] salt = { 1, 2, 3, 4 };
            byte[] shorter = KeyDerivation.Pbkdf2("pw", salt, 1, 8160);
            byte[] longer = KeyDerivation.Pbkdf2("pw", salt, 1, 8193);
            Check.True(shorter.SequenceEqual(longer.Take(8160)));
        }

        public void NegativeIndex_IsArgumentOutOfRange()
        {
            // (uint)index threw OverflowException for a negative index in the checked build.
            Check.Throws<ArgumentOutOfRangeException>(() => ToolBelt.Guards.Guard.Index(-1, 5));
            var buffer = new CircularBuffer<int>(4); buffer.Add(1);
            Check.Throws<ArgumentOutOfRangeException>(() => _ = buffer[-1]);
            var deque = new Deque<int>(); deque.PushBack(1);
            Check.Throws<ArgumentOutOfRangeException>(() => _ = deque[-1]);
            var fenwick = new FenwickTree(4);
            Check.Throws<ArgumentOutOfRangeException>(() => fenwick.Add(-1, 1));
            Check.Throws<ArgumentOutOfRangeException>(() => fenwick.PrefixSum(-1));
            Check.Throws<ArgumentOutOfRangeException>(() => fenwick.RangeSum(-1, 2));
            Check.Throws<ArgumentOutOfRangeException>(() => fenwick.RangeSum(0, -1));
            Check.Throws<ArgumentOutOfRangeException>(() => new DisjointSet(4).Find(-1));
            Check.Throws<ArgumentOutOfRangeException>(() => new ConsoleTable("a").AlignColumn(-1, ColumnAlignment.Right));
        }

        // ---- IpUtils, CidrRange ----

        public void IsLoopback_UnwrapsIPv4Mapped()
        {
            Check.True(IpUtils.IsLoopback(IPAddress.Parse("::ffff:127.0.0.2")));
            Check.True(IpUtils.IsLoopback(IPAddress.Parse("::ffff:127.0.0.1")));
            Check.False(IpUtils.IsLoopback(IPAddress.Parse("::ffff:10.0.0.1")));
        }

        public void CidrRange_RejectsBracketsPortsAndScopes()
        {
            foreach (string text in new[] { "[::1]:80/128", "[fe80::1%5]:99/64", "[::1]/128", "fe80::1%eth0/64", "fe80::1%5/64", "::ffff:010.0.0.1/104" })
                Check.False(CidrRange.TryParse(text, out _), text);
            Check.True(CidrRange.TryParse("::ffff:10.0.0.1/104", out _));
            Check.True(CidrRange.TryParse("2001:DB8::/32", out var r));
            Check.Equal("2001:db8::/32", r.ToString());
        }

        // ---- PortCheck, TcpLineClient: IPv6 on .NET Framework ----

        public void PortCheck_And_TcpLineClient_ReachIPv6Loopback()
        {
            var listener = new TcpListener(IPAddress.IPv6Loopback, 0);
            listener.Start();
            try
            {
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                Task server = Task.Run(async () =>
                {
                    using (var a = await listener.AcceptTcpClientAsync().ConfigureAwait(false)) { }   // PortCheck's probe
                    using var b = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
                    var line = System.Text.Encoding.ASCII.GetBytes("hello\n");
                    await b.GetStream().WriteAsync(line, 0, line.Length).ConfigureAwait(false);
                    await Task.Delay(500).ConfigureAwait(false);
                });
                Check.True(PortCheck.IsReachable("::1", port, TimeSpan.FromSeconds(5)));
                using (var client = new TcpLineClient("::1", port, readTimeout: TimeSpan.FromSeconds(5)))
                    Check.Equal("hello", client.ReadLine());
                Check.True(server.Wait(5000));
                Check.True(PortCheck.IsReachable("127.0.0.1", ((IPEndPoint)StartV4(out var v4).LocalEndpoint).Port, TimeSpan.FromSeconds(5)));
                v4.Stop();
            }
            finally { listener.Stop(); }
        }

        private static TcpListener StartV4(out TcpListener l)
        {
            l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            return l;
        }

        // ---- CronSchedule ----

        public void Cron_ZoneOverloadAtMinValueFindsDayOne()
        {
            // The search started a day in (to keep wall times in the calendar), skipping 0001-01-01 entirely.
            var s = new CronSchedule("0 12 * * *");
            Check.Equal(new DateTimeOffset(1, 1, 1, 12, 0, 0, TimeSpan.Zero), s.GetNextOccurrence(DateTimeOffset.MinValue, TimeZoneInfo.Utc));
            var tokyo = TimeZoneInfo.FindSystemTimeZoneById("Tokyo Standard Time");
            Check.Equal(new DateTimeOffset(1, 1, 1, 12, 0, 0, TimeSpan.FromHours(9)), s.GetNextOccurrence(DateTimeOffset.MinValue, tokyo));
            var eastern = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
            TimeSpan off = eastern.GetUtcOffset(new DateTime(1, 1, 2, 0, 0, 0, DateTimeKind.Utc));
            Check.Equal(new DateTimeOffset(1, 1, 1, 12, 0, 0, off), s.GetNextOccurrence(DateTimeOffset.MinValue, eastern));
            Check.Equal(new DateTimeOffset(1, 1, 1, 0, 1, 0, TimeSpan.Zero),
                new CronSchedule("* * * * *").GetNextOccurrence(DateTimeOffset.MinValue, TimeZoneInfo.Utc));
        }

        public void Cron_GetNextOccurrences_HugeCountDoesNotPreallocate()
        {
            // new List(int.MaxValue) threw OutOfMemoryException before searching; the calendar ends first.
            var s = new CronSchedule("0 0 * * *");
            Check.Throws<InvalidOperationException>(() => s.GetNextOccurrences(new DateTimeOffset(9999, 6, 1, 0, 0, 0, TimeSpan.Zero), int.MaxValue));
        }

        // ---- CronScheduler ----

        private static CronScheduler Idle(DateTimeOffset[] clock, List<Exception> errors) =>
            new CronScheduler((j, e) => { lock (errors) errors.Add(e); }, () => clock[0],
                (t, ct) => Task.Delay(Timeout.Infinite, ct), TimeZoneInfo.Utc);

        public void CronScheduler_AddAfterStart_NeverMatchingGoesToOnError()
        {
            var clock = new[] { new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) };
            var errors = new List<Exception>();
            using var s = Idle(clock, errors);
            s.Start();
            var job = s.Add("feb30", "0 0 30 2 *", () => { });
            Check.Equal(null, job.NextRun);
            Check.Equal(1, errors.Count);
            Check.True(errors[0] is InvalidOperationException);
        }

        public void CronScheduler_UseAfterDisposeThrows()
        {
            var clock = new[] { new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) };
            var s = Idle(clock, new List<Exception>());
            s.Dispose();
            Check.Throws<ObjectDisposedException>(() => s.Start());
            Check.Throws<ObjectDisposedException>(() => s.Add("x", "* * * * *", () => { }));
        }

        public void CronScheduler_TwoJobsDisposingTogetherDoNotDeadlock()
        {
            var clock = new[] { new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) };
            var s = Idle(clock, new List<Exception>());
            int returned = 0;
            var both = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var barrier = new Barrier(2);
            for (int i = 0; i < 2; i++)
                s.Add("j" + i, "* * * * *", () =>
                {
                    barrier.SignalAndWait(5000);   // both runs are under way before either disposes
                    s.Dispose();
                    if (Interlocked.Increment(ref returned) == 2) both.TrySetResult(true);
                });
            s.Start();
            clock[0] = clock[0].AddMinutes(2);
            Check.Equal(2, s.RunDue());
            Check.True(both.Task.Wait(10000), $"{Volatile.Read(ref returned)} of 2 Dispose calls returned");
        }

        // ---- BusinessDays, UnixTime, KeyDerivation parameter names ----

        public void BusinessDays_PastTheCalendarBlamesCount()
        {
            foreach (var (start, count) in new[] { (DateTime.MinValue, 2_700_000), (DateTime.MaxValue, 1), (DateTime.MinValue, -1), (DateTime.MaxValue.AddDays(-3), 5) })
            {
                var ex = Check.Throws<ArgumentOutOfRangeException>(() => BusinessDays.AddBusinessDays(start, count));
                Check.Equal("count", ex.ParamName, $"{start:yyyy-MM-dd} {count}");
            }
            Check.Equal(new DateTime(9999, 12, 31), BusinessDays.AddBusinessDays(new DateTime(9999, 12, 30), 1));   // Thursday → Friday
        }

        public void UnixTime_OutOfRangeBlamesTheArgument()
        {
            Check.Equal("seconds", Check.Throws<ArgumentOutOfRangeException>(() => UnixTime.FromUnixSeconds(long.MaxValue)).ParamName);
            Check.Equal("seconds", Check.Throws<ArgumentOutOfRangeException>(() => UnixTime.FromUnixSeconds(long.MinValue)).ParamName);
            Check.Equal("milliseconds", Check.Throws<ArgumentOutOfRangeException>(() => UnixTime.FromUnixMilliseconds(long.MinValue)).ParamName);
            Check.Equal("milliseconds", Check.Throws<ArgumentOutOfRangeException>(() => UnixTime.FromUnixMilliseconds(long.MaxValue)).ParamName);
            Check.Equal(DateTimeOffset.FromUnixTimeSeconds(253402300799), UnixTime.FromUnixSeconds(253402300799));
        }

        public void GenerateSalt_NegativeBlamesLength()
        {
            Check.Equal("length", Check.Throws<ArgumentOutOfRangeException>(() => KeyDerivation.GenerateSalt(-1)).ParamName);
        }
    }
}
