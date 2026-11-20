using System;
using System.Globalization;
using System.IO;
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
    /// <summary>
    /// Round 192 review regressions (security, net, time, eleventh pass): each failed on the code before its fix.
    /// </summary>
    public sealed class SecurityNetTimeReview192Tests
    {
        // ---- HttpDownload ----

        private sealed class RawServer : IDisposable
        {
            private readonly TcpListener _listener = new TcpListener(IPAddress.Loopback, 0);
            private int _requests;
            public string Url { get; }
            public int Requests => Volatile.Read(ref _requests);

            public RawServer(Func<string, NetworkStream, Task> handler)
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
                                    Interlocked.Increment(ref _requests);
                                    await handler(head.ToString(), ns);
                                }
                                catch { }
                            }
                        });
                    }
                });
            }

            public void Dispose() { try { _listener.Stop(); } catch { } }
        }

        private static Task Respond(NetworkStream ns, string statusLine, byte[] body, params string[] headers)
        {
            var sb = new StringBuilder("HTTP/1.1 " + statusLine + "\r\nConnection: close\r\n");
            foreach (string h in headers) sb.Append(h).Append("\r\n");
            sb.Append("Content-Length: ").Append(body.Length).Append("\r\n\r\n");
            byte[] head = Encoding.ASCII.GetBytes(sb.ToString());
            ns.Write(head, 0, head.Length);
            ns.Write(body, 0, body.Length);
            ns.Flush();
            return Task.CompletedTask;
        }

        // A server that answered every request with the same short range from byte 0 ("bytes 0-5/20") made a Resume
        // download loop forever, even with MaxAttempts = 1: each answer rewrote the partial file to the same 6 bytes, and
        // the retry for "more to fetch" was not counted as an attempt. Only the caller's token stopped it.
        public async Task HttpDownload_ShortRangeThatAddsNothing_FailsInsteadOfLooping()
        {
            byte[] six = Encoding.ASCII.GetBytes("hello ");
            using var server = new RawServer((head, ns) => Respond(ns, "206 Partial Content", six, "Content-Range: bytes 0-5/20"));
            string dest = Path.Combine(Path.GetTempPath(), "toolbelt-dl192-" + Guid.NewGuid().ToString("N"));
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                var ex = await Check.ThrowsAsync<HttpDownloadException>(() =>
                    HttpDownload.ToFileAsync(server.Url, dest, new HttpDownloadOptions { Resume = true, MaxAttempts = 1 }, cts.Token));
                Check.False(cts.IsCancellationRequested, "it ran until the token stopped it");
                Check.True(server.Requests <= 3, $"{server.Requests} requests");
                Check.True(ex.Message.Contains("adds nothing"), ex.Message);
            }
            finally
            {
                foreach (string p in new[] { dest, dest + ".part" }) try { File.Delete(p); } catch { }
            }
        }

        // The first short range from byte 0 is progress (nothing was there before), and the rest is then asked for.
        public async Task HttpDownload_ShortRangesThatGrow_StillComplete()
        {
            byte[] body = Encoding.ASCII.GetBytes("hello world, again");
            using var server = new RawServer((head, ns) =>
            {
                int from = 0;
                foreach (string line in head.Split(new[] { "\r\n" }, StringSplitOptions.None))
                    if (line.StartsWith("Range: bytes=", StringComparison.OrdinalIgnoreCase))
                        from = int.Parse(line.Substring(13).TrimEnd('-'), CultureInfo.InvariantCulture);
                int to = Math.Min(from + 4, body.Length - 1);
                var slice = new byte[to - from + 1];
                Array.Copy(body, from, slice, 0, slice.Length);
                return Respond(ns, "206 Partial Content", slice, $"Content-Range: bytes {from}-{to}/{body.Length}");
            });
            string dest = Path.Combine(Path.GetTempPath(), "toolbelt-dl192-" + Guid.NewGuid().ToString("N"));
            try
            {
                var r = await HttpDownload.ToFileAsync(server.Url, dest, new HttpDownloadOptions { Resume = true, MaxAttempts = 1 });
                Check.Equal((long)body.Length, r.BytesDownloaded);
                Check.Equal(Encoding.ASCII.GetString(body), File.ReadAllText(dest));
            }
            finally
            {
                foreach (string p in new[] { dest, dest + ".part" }) try { File.Delete(p); } catch { }
            }
        }

        // ---- CronSchedule at the end of the calendar ----

        // Reaching 9999-12-31 was reported as "the expression is unsatisfiable", and GetNextOccurrences threw away the
        // occurrences it had found. Graded against a day count for random starts in the calendar's last year.
        public void Cron_EndOfCalendar_IsNotUnsatisfiable_AndOccurrencesAreKept()
        {
            var yearly = new CronSchedule("0 0 1 1 *");
            var after = new DateTimeOffset(9999, 1, 2, 0, 0, 0, TimeSpan.Zero);
            var ex = Check.Throws<InvalidOperationException>(() => yearly.GetNextOccurrence(after));
            Check.False(ex.Message.Contains("unsatisfiable"), ex.Message);
            ex = Check.Throws<InvalidOperationException>(() => yearly.GetNextOccurrence(after, TimeZoneInfo.Utc));
            Check.False(ex.Message.Contains("unsatisfiable"), ex.Message);
            Check.Equal(0, yearly.GetNextOccurrences(after, 5).Count);

            // A truly impossible expression still says so, at any date.
            var never = new CronSchedule("0 0 30 2 *");
            ex = Check.Throws<InvalidOperationException>(() => never.GetNextOccurrence(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)));
            Check.True(ex.Message.Contains("unsatisfiable"), ex.Message);
            Check.Throws<InvalidOperationException>(() => never.GetNextOccurrences(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), 3));

            var daily = new CronSchedule("0 0 * * *");
            var rng = new Random(19201);
            for (int trial = 0; trial < 200; trial++)
            {
                var start = new DateTimeOffset(new DateTime(9999, 1, 1).AddDays(rng.Next(365)), TimeSpan.Zero);
                int left = (int)(new DateTime(9999, 12, 31) - start.DateTime).TotalDays;
                int count = rng.Next(1, 400);
                var got = daily.GetNextOccurrences(start, count);
                Check.Equal(Math.Min(count, left), got.Count, start.ToString("o", CultureInfo.InvariantCulture));
                for (int i = 0; i < got.Count; i++) Check.Equal(start.AddDays(i + 1), got[i]);
            }
        }

        // ---- Hashing ----

        // An empty path failed inside FileStream, with ParamName null on .NET Framework (and the hash object was made
        // before a null path was checked, and never disposed).
        public void Hashing_FileWithEmptyOrNullPath_NamesThePath()
        {
            Check.Equal("path", Check.Throws<ArgumentException>(() => Hashing.Sha256File("")).ParamName);
            Check.Equal("path", Check.Throws<ArgumentException>(() => Hashing.Sha512File("")).ParamName);
            Check.Equal("path", Check.Throws<ArgumentNullException>(() => Hashing.Sha256File(null!)).ParamName);
            Check.Equal("path", Check.Throws<ArgumentNullException>(() => Hashing.Sha512File(null!)).ParamName);
        }

        // ---- CidrRange ----

        // A block of IPv4-mapped addresses (::ffff:10.0.0.0/104) did not contain the IPv4 address it names, though an
        // IPv4 block contained the mapped form. Graded over random addresses and mapped blocks against the mapped form;
        // other IPv6 blocks (::/0 among them) still hold no IPv4 address.
        public void Cidr_MappedBlock_ContainsTheIpv4AddressesItNames()
        {
            Check.True(CidrRange.Parse("::ffff:10.0.0.0/104").Contains(IPAddress.Parse("10.1.2.3")));
            Check.False(CidrRange.Parse("::ffff:10.0.0.0/104").Contains(IPAddress.Parse("11.1.2.3")));
            Check.False(CidrRange.Parse("::/0").Contains(IPAddress.Parse("10.1.2.3")));
            Check.False(CidrRange.Parse("::/64").Contains(IPAddress.Parse("10.1.2.3")));
            var rng = new Random(19202);
            var bytes = new byte[4];
            for (int trial = 0; trial < 5000; trial++)
            {
                rng.NextBytes(bytes);
                var v4 = new IPAddress(bytes);
                rng.NextBytes(bytes);
                var block = new CidrRange(new IPAddress(bytes).MapToIPv6(), 96 + rng.Next(33));
                Check.Equal(block.Contains(v4.MapToIPv6()), block.Contains(v4), $"{block} {v4}");
            }
        }

        // The prefix took a leading zero ("/024"), a space after the slash ("/ 24") and any run of zeros
        // ("/0000000000000000032"), though leading-zero octets are refused. Swept over every prefix.
        public void Cidr_PrefixIsPlainDecimal()
        {
            foreach (string bad in new[] { "10.0.0.0/024", "10.0.0.0/ 24", "10.0.0.0/0000000000000000032", "10.0.0.0/08",
                                           "10.0.0.0/+24", "10.0.0.0/00", "10.0.0.0/2 4", "::/0128", "::/ 64" })
                Check.False(CidrRange.TryParse(bad, out _), bad);
            for (int p = 0; p <= 128; p++)
            {
                string s = p.ToString(CultureInfo.InvariantCulture);
                if (p <= 32)
                {
                    Check.True(CidrRange.TryParse("10.0.0.0/" + s, out var r) && r.PrefixLength == p, s);
                    Check.False(CidrRange.TryParse("10.0.0.0/0" + s, out _), "0" + s);
                }
                Check.True(CidrRange.TryParse("2001:db8::/" + s, out var r6) && r6.PrefixLength == p, s);
                Check.False(CidrRange.TryParse("2001:db8::/0" + s, out _), "0" + s);
            }
            Check.True(CidrRange.TryParse("10.0.0.0/24 ", out _), "trailing space");
        }

#if !NETFRAMEWORK   // SecretsFile is net8.0-only (AES-GCM)
        // ---- SecretsFile ----

        // A folder at the path quietly gave an empty store, and only the first save failed.
        public void SecretsFile_LoadOrCreateOnAFolder_Throws()
        {
            string dir = Path.Combine(Path.GetTempPath(), "toolbelt-sf192-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                Check.Equal("path", Check.Throws<ArgumentException>(() => SecretsFile.LoadOrCreate(dir, "passphrase")).ParamName);
                Check.NotNull(SecretsFile.LoadOrCreate(Path.Combine(dir, "missing.bin"), "passphrase"));
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }
#endif

        // ---- HumanDuration ----

        // "- 5s" (a space after the sign), "1d1d" (a repeated unit, summed) and "1s 1m" (units out of order) parsed, but
        // "5 s" did not. Graded over random durations written in every allowed spacing.
        public void HumanDuration_Grammar_SignUnitsAndSpacing()
        {
            foreach (string bad in new[] { "- 5s", "-  5s", "1d1d", "1h 1h", "1ms500ms", "1s 1m", "5m 1h", "1ms 1s", "-", "- " })
                Check.False(HumanDuration.TryParse(bad, out _), bad);
            Check.Equal(TimeSpan.FromSeconds(5), HumanDuration.Parse("5 s"));
            Check.Equal(TimeSpan.FromMilliseconds(5), HumanDuration.Parse("5 ms"));
            Check.Equal(TimeSpan.FromSeconds(-5), HumanDuration.Parse("-5s"));
            Check.Equal(new TimeSpan(1, 2, 3, 4, 5), HumanDuration.Parse("1d 2 h3m 4 s 5ms"));
            var rng = new Random(19203);
            string[] units = { "d", "h", "m", "s", "ms" };
            long[] per = { TimeSpan.TicksPerDay, TimeSpan.TicksPerHour, TimeSpan.TicksPerMinute, TimeSpan.TicksPerSecond, TimeSpan.TicksPerMillisecond };
            for (int trial = 0; trial < 3000; trial++)
            {
                var sb = new StringBuilder();
                long ticks = 0;
                bool any = false;
                for (int u = 0; u < units.Length; u++)
                {
                    if (rng.Next(2) == 0 && !(u == units.Length - 1 && !any)) continue;
                    int n = rng.Next(1000);
                    ticks += n * per[u];
                    if (any && rng.Next(2) == 0) sb.Append(' ');
                    sb.Append(n);
                    if (rng.Next(2) == 0) sb.Append(' ');
                    sb.Append(units[u]);
                    any = true;
                }
                bool neg = rng.Next(2) == 0;
                string text = (neg ? "-" : "") + sb;
                Check.True(HumanDuration.TryParse(text, out var got), text);
                Check.Equal(neg ? -ticks : ticks, got.Ticks, text);
            }
        }

        // ---- Iso8601Duration ----

        // P1W2D parsed as 9 days (ISO 8601 makes the week form stand alone), and digits past the tick were dropped, so
        // PT1.99999999S gave 1.9999999 s. Graded against exact decimal rounding (half up) over random fractions.
        public void Iso8601Duration_WeeksAlone_AndSubTickDigitsRound()
        {
            foreach (string bad in new[] { "P1W2D", "P1WT1H", "P1W0D", "-P1W1D" })
                Check.False(Iso8601Duration.TryParse(bad, out _), bad);
            Check.Equal(TimeSpan.FromDays(7), Iso8601Duration.Parse("P1W"));
            Check.Equal(TimeSpan.FromDays(-14), Iso8601Duration.Parse("-P2W"));
            Check.Equal(TimeSpan.FromSeconds(2), Iso8601Duration.Parse("PT1.99999999S"));
            Check.Equal(1L, Iso8601Duration.Parse("PT0.00000005S").Ticks);
            Check.Equal(0L, Iso8601Duration.Parse("PT0.00000004999S").Ticks);
            Check.Equal(TimeSpan.FromMinutes(1), Iso8601Duration.Parse("PT59.999999999S"));
            var rng = new Random(19204);
            for (int trial = 0; trial < 5000; trial++)
            {
                int whole = rng.Next(100000);
                var frac = new StringBuilder();
                int len = 8 + rng.Next(12);
                for (int i = 0; i < len; i++) frac.Append((char)('0' + rng.Next(10)));
                string text = "PT" + whole + (rng.Next(2) == 0 ? "." : ",") + frac + "S";
                decimal exact = decimal.Parse(whole + "." + frac, CultureInfo.InvariantCulture) * TimeSpan.TicksPerSecond;
                long expected = (long)decimal.Floor(exact + 0.5m);
                Check.Equal(expected, Iso8601Duration.Parse(text).Ticks, text);
            }
        }

        // ---- PasswordHasher ----

        // NeedsRehash(hash, -5) answered false, as though the stored hash were strong enough, where Hash refuses -5.
        public void PasswordHasher_NeedsRehash_ChecksTheIterationCount()
        {
            string hash = PasswordHasher.Hash("pw", 1000);
            foreach (int bad in new[] { -5, 0, int.MinValue, PasswordHasher.MaxIterations + 1, int.MaxValue })
                Check.Equal("currentIterations", Check.Throws<ArgumentOutOfRangeException>(() => PasswordHasher.NeedsRehash(hash, bad)).ParamName);
            Check.True(PasswordHasher.NeedsRehash(hash, 1001));
            Check.False(PasswordHasher.NeedsRehash(hash, 1000));
            Check.False(PasswordHasher.NeedsRehash(hash, 1));
        }

        // Verify took strings Hash never writes, though the docs say they throw: a leading zero in the iteration count,
        // whitespace around the salt or digest, a trailing newline (FromBase64String skips whitespace).
        public void PasswordHasher_Verify_RefusesNonCanonicalEncodings()
        {
            string hash = PasswordHasher.Hash("pw", 1000);
            Check.True(PasswordHasher.Verify("pw", hash));
            string[] p = hash.Split('$');
            foreach (string bad in new[]
            {
                string.Join("$", p[0], "01000", p[2], p[3]),
                string.Join("$", p[0], p[1], " " + p[2], p[3]),
                string.Join("$", p[0], p[1], p[2] + " ", p[3]),
                string.Join("$", p[0], p[1], p[2], " " + p[3]),
                hash + "\n",
                hash + "\r\n",
                string.Join("$", p[0], p[1], p[2].Insert(4, "\t"), p[3]),
            })
            {
                Check.Throws<FormatException>(() => PasswordHasher.Verify("pw", bad), bad);
                Check.Throws<FormatException>(() => PasswordHasher.NeedsRehash(bad, 1000), bad);
            }
        }

        // ---- Ports ----

        // Port 0 was accepted: TcpLineClient failed later with a SocketException and PortCheck answered false, as though
        // the port were closed. No connection can be made to it.
        public void Ports_ZeroIsRefused()
        {
            Check.Equal("port", Check.Throws<ArgumentOutOfRangeException>(() => PortCheck.IsReachable("127.0.0.1", 0, TimeSpan.FromSeconds(1))).ParamName);
            Check.Equal("port", Check.Throws<ArgumentOutOfRangeException>(() => new TcpLineClient("127.0.0.1", 0, TimeSpan.FromSeconds(1)).Dispose()).ParamName);
        }
    }
}
