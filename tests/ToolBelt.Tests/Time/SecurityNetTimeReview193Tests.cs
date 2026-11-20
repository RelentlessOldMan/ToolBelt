using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ToolBelt.Net;
using ToolBelt.Tests.Framework;
using ToolBelt.Time;

namespace ToolBelt.Tests.Time
{
    /// <summary>
    /// Round 193 review regressions (security, net, time, twelfth pass): each failed on the code before its fix, except
    /// where a comment says it pins behaviour whose docs were corrected.
    /// </summary>
    public sealed class SecurityNetTimeReview193Tests
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

        private static string NewDest() => Path.Combine(Path.GetTempPath(), "toolbelt-dl193-" + Guid.NewGuid().ToString("N"));

        private static void Clean(string dest)
        {
            foreach (string p in new[] { dest, dest + ".part" }) try { File.Delete(p); } catch { }
        }

        // A 416 "bytes */1000" for a 1000-byte partial file finished the download before the MaxBytes check, so a file
        // past the limit was kept, though the same file resumed through a 206 was refused.
        public async Task HttpDownload_CompleteBy416_IsHeldToMaxBytes()
        {
            using var server = new RawServer((head, ns) => Respond(ns, "416 Range Not Satisfiable", new byte[0], "Content-Range: bytes */1000"));
            string dest = NewDest();
            try
            {
                File.WriteAllBytes(dest + ".part", new byte[1000]);
                var ex = await Check.ThrowsAsync<HttpDownloadException>(() =>
                    HttpDownload.ToFileAsync(server.Url, dest, new HttpDownloadOptions { Resume = true, MaxAttempts = 1, MaxBytes = 500 }));
                Check.True(ex.Message.Contains("MaxBytes"), ex.Message);
                Check.False(File.Exists(dest), "the destination was written");
                Check.False(File.Exists(dest + ".part"), "the partial file was kept");
            }
            finally { Clean(dest); }
        }

        // The same 416 path never called OnProgress, though every other path reports at least once.
        public async Task HttpDownload_CompleteBy416_ReportsProgress()
        {
            using var server = new RawServer((head, ns) => Respond(ns, "416 Range Not Satisfiable", new byte[0], "Content-Range: bytes */1000"));
            string dest = NewDest();
            try
            {
                File.WriteAllBytes(dest + ".part", new byte[1000]);
                var calls = new List<(long, long?)>();
                var r = await HttpDownload.ToFileAsync(server.Url, dest, new HttpDownloadOptions
                {
                    Resume = true, MaxAttempts = 1, OnProgress = (d, t) => { lock (calls) calls.Add((d, t)); },
                });
                Check.Equal(1000L, r.BytesDownloaded);
                Check.True(calls.Count > 0, "no progress was reported");
                Check.Equal(((long)1000, (long?)1000), calls[calls.Count - 1]);
            }
            finally { Clean(dest); }
        }

        // ---- CidrRange ----

        // Round 192 refused a space after the slash but not one before it: "1.2.3.4 /32" parsed. Whitespace may surround
        // the whole text only.
        public void Cidr_NoSpaceBeforeTheSlash()
        {
            foreach (string t in new[] { "1.2.3.4 /32", "10.0.0.0\t/8", "::1 /128", "1.2.3.4/ 32" })
                Check.False(CidrRange.TryParse(t, out _), t);
            foreach (string t in new[] { " 1.2.3.4/32 ", "1.2.3.4/32", "  ::1/128\t" })
                Check.True(CidrRange.TryParse(t, out _), t);
        }

        // ---- TcpLineClient ----

        // Pins behaviour (its doc was corrected): each invalid sequence counts as a character toward MaxLineLength, not
        // each run, so four 0xFF bytes in a row overrun a limit of 3. Counting a run once would let a peer stream bad
        // bytes forever without a terminator.
        public void TcpLineClient_EachBadSequenceCountsTowardMaxLineLength()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                var accept = Task.Run(async () =>
                {
                    using TcpClient c = await listener.AcceptTcpClientAsync();
                    byte[] data = { 0xFF, 0xFF, 0xFF, 0xFF, 0x0A, 0x6F, 0x6B, 0x0A };
                    NetworkStream s = c.GetStream();
                    await s.WriteAsync(data, 0, data.Length);
                    await Task.Delay(3000);
                });
                using (var client = new TcpLineClient("127.0.0.1", port, connectTimeout: TimeSpan.FromSeconds(5),
                    readTimeout: TimeSpan.FromSeconds(3), encoding: new UTF8Encoding(false, true)))
                {
                    client.MaxLineLength = 3;
                    Check.Throws<InvalidDataException>(() => client.ReadLine());
                    Check.Equal("ok", client.ReadLine());
                }
            }
            finally { listener.Stop(); }
        }

        // ---- CronSchedule and CronScheduler ----

        // Round 192's end-of-calendar message was given by the zone overload whenever its walk stopped early, so an
        // impossible date (31 September) read as "before the end of the calendar", and GetNextOccurrences would have
        // taken it for the calendar's end. Both overloads now agree.
        public void Cron_ImpossibleDateWithAZone_IsUnsatisfiable()
        {
            var s = new CronSchedule("*/3 16 31-31 9 */7");
            var after = new DateTimeOffset(5228, 2, 16, 0, 0, 0, TimeSpan.Zero);
            var plain = Check.Throws<InvalidOperationException>(() => s.GetNextOccurrence(after));
            var zoned = Check.Throws<InvalidOperationException>(() => s.GetNextOccurrence(after, TimeZoneInfo.Utc));
            Check.True(plain.Message.Contains("unsatisfiable"), plain.Message);
            Check.Equal(plain.Message, zoned.Message);
            // Guard: the calendar's real end is still told apart.
            var end = Check.Throws<InvalidOperationException>(() => new CronSchedule("0 0 1 1 *").GetNextOccurrence(new DateTimeOffset(9999, 1, 2, 0, 0, 0, TimeSpan.Zero), TimeZoneInfo.Utc));
            Check.False(end.Message.Contains("unsatisfiable"), end.Message);
        }

        private static readonly Func<TimeSpan, CancellationToken, Task> Never = (t, ct) => Task.Delay(Timeout.Infinite, ct);

        private static async Task Settle(CronScheduler s)
        {
            for (int i = 0; i < 400; i++)
            {
                bool any = false;
                foreach (var j in s.Jobs) any |= j.IsRunning;
                if (!any) return;
                await Task.Delay(5);
            }
        }

        // A wildcard job counted from now after any step back, so a reading 10 ms back over a minute's start ran that
        // minute again: "0 * * * *" and "* * * * *" ran 10:00 twice, though "0 0-23 * * *" (the same schedule) ran once.
        public async Task CronScheduler_StepBackOverAMinuteStart_DoesNotRunItTwice()
        {
            var t0 = new DateTimeOffset(2026, 3, 2, 10, 0, 0, TimeSpan.Zero);
            DateTimeOffset now = t0.AddSeconds(-30);
            using var s = new CronScheduler(null, () => now, Never, TimeZoneInfo.Utc);
            var hourly = s.Add("hourly", "0 * * * *", () => { });
            var minutely = s.Add("minutely", "* * * * *", () => { });
            var ranged = s.Add("ranged", "0 0-23 * * *", () => { });
            s.Start();
            now = t0.AddMilliseconds(5);
            Check.Equal(3, s.RunDue());
            await Settle(s);
            now = t0.AddMilliseconds(-5);
            s.RunDue();
            now = t0.AddMilliseconds(15);
            Check.Equal(0, s.RunDue());
            await Settle(s);
            foreach (var j in new[] { hourly, minutely, ranged }) Check.Equal(1, j.RunCount, j.Name);
            // Guard: a long step back still replays a wildcard job's minutes, as cron does.
            now = t0.AddHours(-2).AddSeconds(30);
            s.RunDue();
            now = t0.AddHours(-2).AddMinutes(1);
            Check.Equal(1, s.RunDue());
            await Settle(s);
            Check.Equal(2, minutely.RunCount);
        }
    }
}
