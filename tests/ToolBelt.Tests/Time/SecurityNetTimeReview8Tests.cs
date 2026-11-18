using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using ToolBelt.Net;
using ToolBelt.Security;
using ToolBelt.Tests.Framework;
using ToolBelt.Time;

namespace ToolBelt.Tests.Time
{
    // Round 142 review regressions (Security, net, time, eighth pass): each failed on the code before its fix.
    public sealed class SecurityNetTimeReview8Tests
    {
        // ---- CronSchedule: a day-of-week step from a single value ----

        // "7/1" was out of range though 7 alone is Sunday; a step from a value runs to Saturday (round 13), so it is
        // Sunday alone.
        public void Cron_ADayOfWeekStepFromSevenIsSunday()
        {
            var saturday = new DateTimeOffset(2026, 11, 21, 0, 0, 0, TimeSpan.Zero);
            var sundays = new CronSchedule("0 0 * * 7/1").GetNextOccurrences(saturday, 3).ToArray();
            Check.True(sundays.All(d => d.DayOfWeek == DayOfWeek.Sunday), string.Join(",", sundays));
            // Guard: "*/2" over day-of-week stays 0, 2, 4, 6 (Sunday once).
            var stars = new CronSchedule("0 0 * * */2").GetNextOccurrences(saturday, 4).Select(d => (int)d.DayOfWeek);
            Check.Equal("0,2,4,6", string.Join(",", stars));
        }

        // ---- TcpLineClient: a lone surrogate ----

        public void TcpLineClient_SendRefusesALoneSurrogate()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                using var client = new TcpLineClient("127.0.0.1", port, connectTimeout: TimeSpan.FromSeconds(5), readTimeout: TimeSpan.FromSeconds(5));
                foreach (string bad in new[] { "a\uD800b", "\uDC00", "x\uD83D" })
                {
                    var ex = Check.Throws<ArgumentException>(() => client.Send(bad));
                    Check.Equal("line", ex.ParamName);
                }
                client.Send("ok \U0001F600");   // guard: a pair is fine
            }
            finally { listener.Stop(); }
        }

        // ---- CronScheduler: a job added after Start ----

        // The loop's wait, worked out before the job existed, ran on: a "* * * * *" job added just before a minute-long
        // wait first ran a minute late. Here the delay never ends by itself, so only a wake makes the loop pass again.
        public void Scheduler_AddAfterStartWakesTheLoop()
        {
            var now = new DateTimeOffset(2026, 1, 1, 11, 0, 30, TimeSpan.Zero);
            int delays = 0;
            using var s = new CronScheduler(clock: () => now,
                delay: (t, ct) => { Interlocked.Increment(ref delays); return Task.Delay(Timeout.Infinite, ct); }, timeZone: TimeZoneInfo.Utc);
            s.Add("nightly", "0 3 * * *", () => { });
            s.Start();
            Check.True(SpinWait.SpinUntil(() => Volatile.Read(ref delays) == 1, 5000), "the loop parked");
            s.Add("minutely", "* * * * *", () => { });
            Check.True(SpinWait.SpinUntil(() => Volatile.Read(ref delays) >= 2, 5000), "the loop did not wake for the new job");
        }

        // ---- HttpDownload ----

        private sealed class Server : IDisposable
        {
            private readonly HttpListener _listener = new HttpListener();
            public string Url { get; }
            public int Requests;

            public Server(Action<HttpListenerContext> handler)
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
                        Interlocked.Increment(ref Requests);
                        try { handler(ctx); } catch { try { ctx.Response.Abort(); } catch { } }
                    }
                });
            }

            public void Dispose() { try { _listener.Stop(); _listener.Close(); } catch { } }
        }

        private static void Partial(HttpListenerContext ctx, string? contentRange, string body)
        {
            ctx.Response.StatusCode = 206;
            if (contentRange != null) ctx.Response.AddHeader("Content-Range", contentRange);
            byte[] b = System.Text.Encoding.ASCII.GetBytes(body);
            ctx.Response.ContentLength64 = b.Length;
            ctx.Response.OutputStream.Write(b, 0, b.Length);
            ctx.Response.Close();
        }

        private static string TempPath() => Path.Combine(Path.GetTempPath(), "toolbelt-r142-" + Guid.NewGuid().ToString("N"));

        // A 206 starting elsewhere than the byte asked for kept the partial, so every later call failed the same way.
        public async Task HttpDownload_AMisplacedRangeDiscardsThePartial()
        {
            using var server = new Server(ctx => Partial(ctx, "bytes 5-9/20", "abcde"));
            string dest = TempPath();
            try
            {
                File.WriteAllBytes(dest + ".part", new byte[10]);
                var options = new HttpDownloadOptions { Resume = true, MaxAttempts = 1 };
                var ex = await Check.ThrowsAsync<HttpDownloadException>(() => HttpDownload.ToFileAsync(server.Url, dest, options));
                Check.True(ex.Message.Contains("byte 5"), ex.Message);
                Check.False(File.Exists(dest + ".part"), "the partial was kept");
            }
            finally { File.Delete(dest + ".part"); File.Delete(dest); }
        }

        // A 206 with no Content-Range to a plain GET: the message read "starting at byte  for".
        public async Task HttpDownload_AnUnaskedRangeSaysSo()
        {
            using var server = new Server(ctx => Partial(ctx, null, "abcde"));
            string dest = TempPath();
            try
            {
                var ex = await Check.ThrowsAsync<HttpDownloadException>(() => HttpDownload.ToFileAsync(server.Url, dest, new HttpDownloadOptions { MaxAttempts = 1 }));
                Check.False(ex.Message.Contains("byte  "), ex.Message);
                Check.True(ex.Message.Contains("Content-Range"), ex.Message);
            }
            finally { File.Delete(dest); }
        }

        public async Task HttpDownload_ValidatesArgumentsBeforeAnyWork()
        {
            using var server = new Server(ctx => { ctx.Response.StatusCode = 200; ctx.Response.Close(); });
            string dest = TempPath();
            var ex = await Check.ThrowsAsync<ArgumentException>(() => HttpDownload.ToFileAsync(server.Url, dest, new HttpDownloadOptions { ExpectedSha256Hex = "not-hex" }));
            Check.Equal("options", ex.ParamName);
            ex = await Check.ThrowsAsync<ArgumentException>(() => HttpDownload.ToFileAsync(server.Url, dest, new HttpDownloadOptions { ExpectedSha256Hex = new string('g', 64) }));
            Check.Equal("options", ex.ParamName);
            Check.Equal(0, Volatile.Read(ref server.Requests));
            string folder = TempPath();
            ex = await Check.ThrowsAsync<ArgumentException>(() => HttpDownload.ToFileAsync("", Path.Combine(folder, "a.bin")));
            Check.Equal("url", ex.ParamName);
            ex = await Check.ThrowsAsync<ArgumentException>(() => HttpDownload.ToFileAsync("file", Path.Combine(folder, "a.bin")));
            Check.Equal("url", ex.ParamName);
            Check.False(Directory.Exists(folder), "the folder was created");
            ex = await Check.ThrowsAsync<ArgumentException>(() => HttpDownload.ToFileAsync(server.Url, ""));
            Check.Equal("destinationPath", ex.ParamName);
        }

        // ---- CryptoRandom, KeyDerivation ----

        public void CryptoRandom_ACountPastTheLargestArrayIsAnArgumentError()
        {
            var ex = Check.Throws<ArgumentOutOfRangeException>(() => CryptoRandom.Bytes(int.MaxValue));
            Check.Equal("count", ex.ParamName);
            ex = Check.Throws<ArgumentOutOfRangeException>(() => KeyDerivation.GenerateSalt(int.MaxValue));
            Check.Equal("length", ex.ParamName);
        }

        // ---- PortCheck, HostInfo: an empty host ----

        public void PortCheckAndHostInfo_RefuseAnEmptyHost()
        {
            foreach (string host in new[] { "", "   " })
            {
                var ex = Check.Throws<ArgumentException>(() => PortCheck.IsReachable(host, 80, TimeSpan.FromSeconds(1)));
                Check.Equal("host", ex.ParamName);
                ex = Check.Throws<ArgumentException>(() => HostInfo.Resolve(host, TimeSpan.FromSeconds(5)));
                Check.Equal("host", ex.ParamName);
            }
        }

#if !NETFRAMEWORK   // SecretsFile is net8.0-only (AES-GCM)
        // ---- SecretsFile: the passphrase checked when the file is missing too ----

        public void SecretsFile_LoadOrCreateChecksThePassphraseUpFront()
        {
            string missing = TempPath();
            var nul = Check.Throws<ArgumentNullException>(() => SecretsFile.LoadOrCreate(missing, null!));
            Check.Equal("passphrase", nul.ParamName);
            var empty = Check.Throws<ArgumentException>(() => SecretsFile.LoadOrCreate(missing, ""));
            Check.Equal("passphrase", empty.ParamName);
            Check.Equal(0, SecretsFile.LoadOrCreate(missing, "pass").Count);   // guard
        }
#endif

        // ---- CronScheduler.Remove(null) ----

        public void Scheduler_RemoveNullThrows()
        {
            using var s = new CronScheduler();
            var ex = Check.Throws<ArgumentNullException>(() => s.Remove(null!));
            Check.Equal("job", ex.ParamName);
        }
    }
}
