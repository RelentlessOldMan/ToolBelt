using System;
using System.IO;
using System.Linq;
using System.Net;
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
    /// <summary>Regression tests for the round 124 review (security, net, time, seventh pass).</summary>
    public sealed class SecurityNetTimeReview7Tests
    {
        // ---- HttpDownload: a stale destination under Resume ----

        private static readonly byte[] Body = Encoding.ASCII.GetBytes("hello world");

        private sealed class Server : IDisposable
        {
            private readonly HttpListener _listener = new HttpListener();
            public string Url { get; }

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
                        try { handler(ctx); } catch { try { ctx.Response.Abort(); } catch { } }
                    }
                });
            }

            public void Dispose() { try { _listener.Stop(); _listener.Close(); } catch { } }
        }

        // Serves Body, honouring "bytes=N-" ranges as a server would: 206 with Content-Range, or 416 past the end.
        private static void Serve(HttpListenerContext ctx)
        {
            string? range = ctx.Request.Headers["Range"];
            long from = range != null && range.StartsWith("bytes=", StringComparison.Ordinal)
                ? long.Parse(range.Substring(6).TrimEnd('-'), System.Globalization.CultureInfo.InvariantCulture) : 0;
            if (from >= Body.Length)
            {
                ctx.Response.StatusCode = 416;
                ctx.Response.AddHeader("Content-Range", $"bytes */{Body.Length}");
                ctx.Response.ContentLength64 = 0;
                ctx.Response.Close();
                return;
            }
            ctx.Response.StatusCode = from > 0 ? 206 : 200;
            if (from > 0) ctx.Response.AddHeader("Content-Range", $"bytes {from}-{Body.Length - 1}/{Body.Length}");
            ctx.Response.ContentLength64 = Body.Length - from;
            ctx.Response.OutputStream.Write(Body, (int)from, (int)(Body.Length - from));
            ctx.Response.Close();
        }

        private static string TempFile() => Path.Combine(Path.GetTempPath(), "toolbelt-dl124-" + Guid.NewGuid().ToString("N"));

        private static async Task<Exception?> Try(Func<Task> action)
        {
            try { await action(); return null; }
            catch (Exception ex) { return ex; }
        }

        public async Task HttpDownload_AStaleDestinationUnderResume_IsReplacedByTheNextCall()
        {
            // Longer than the file (416), and the wrong start of it (checksum mismatch): the first call fails as before, and
            // said the next would start over, but every call copied the destination into the partial file again.
            string sha = "b94d27b9934d3e08a52e52d7da7dabfac484efe37a5380ee9088f7ace2efcde9";
            foreach (string stale in new[] { "an old file of 26 bytes...", "HELLO" })
            {
                using var server = new Server(Serve);
                string dest = TempFile();
                try
                {
                    File.WriteAllText(dest, stale);
                    var options = new HttpDownloadOptions { Resume = true, MaxAttempts = 1, ExpectedSha256Hex = sha };
                    Exception? first = await Try(() => HttpDownload.ToFileAsync(server.Url, dest, options));
                    Check.True(first is HttpDownloadException, $"'{stale}': first call {first?.GetType().Name}");
                    Check.Equal(stale, File.ReadAllText(dest));   // a failed download never damages the destination
                    Exception? second = await Try(() => HttpDownload.ToFileAsync(server.Url, dest, options));
                    Check.True(second == null, $"'{stale}': second call {second?.GetType().Name}: {second?.Message}");
                    Check.True(File.ReadAllBytes(dest).SequenceEqual(Body), $"'{stale}': {File.ReadAllText(dest)}");
                    Check.True(!File.Exists(dest + ".part"));
                }
                finally { File.Delete(dest); File.Delete(dest + ".part"); }
            }
        }

        public async Task HttpDownload_WithNoDestination_ARejectedPartialIsStillDeleted()
        {
            using var server = new Server(Serve);
            string dest = TempFile();
            try
            {
                File.WriteAllText(dest + ".part", "far too long a partial file");
                var options = new HttpDownloadOptions { Resume = true, MaxAttempts = 1 };
                Exception? first = await Try(() => HttpDownload.ToFileAsync(server.Url, dest, options));
                Check.True(first is HttpDownloadException, $"first call {first?.GetType().Name}");
                Check.True(!File.Exists(dest + ".part"));
                await HttpDownload.ToFileAsync(server.Url, dest, options);
                Check.True(File.ReadAllBytes(dest).SequenceEqual(Body));
            }
            finally { File.Delete(dest); File.Delete(dest + ".part"); }
        }

        // ---- CronScheduler: wildcard jobs after a short step back ----

        private sealed class Parked
        {
            public readonly ManualResetEventSlim InDelay = new ManualResetEventSlim();
        }

        public void Scheduler_AWildcardJobKeepsRunningAfterAShortStepBack()
        {
            foreach (string expression in new[] { "* * * * *", "*/5 * * * *", "0 * * * *" })
            {
                DateTimeOffset now = new DateTimeOffset(2026, 1, 1, 11, 0, 0, TimeSpan.Zero);
                var parked = new Parked();
                using var s = new CronScheduler(clock: () => now,
                    delay: (t, ct) => { parked.InDelay.Set(); return Task.Delay(Timeout.Infinite, ct); }, timeZone: TimeZoneInfo.Utc);
                ScheduledJob job = s.Add("job", expression, () => { });
                s.Start();
                Check.True(parked.InDelay.Wait(5000), "the loop parked");
                // Runs at 11:00 (or its next time), then the clock is corrected back two hours.
                now = job.NextRun!.Value;
                Check.Equal(1, Tick(s, job));
                now = now.AddHours(-2);
                int started = 0;
                for (int m = 1; m <= 110; m++) { now = now.AddMinutes(1); started += Tick(s, job); }
                // Every minute, every 5th, or every hour: as cron runs a wildcard job, by the clock as it now reads.
                int expected = expression == "* * * * *" ? 110 : expression == "*/5 * * * *" ? 22 : 2;
                Check.True(started >= expected - 1, $"'{expression}': started {started} of {expected}");
            }
        }

        private static int Tick(CronScheduler s, ScheduledJob job)
        {
            int started = s.RunDue();
            Check.True(SpinWait.SpinUntil(() => !job.IsRunning, 5000), "the job finished");
            return started;
        }

#if !NETFRAMEWORK   // AuthenticatedEncryption is net8.0-only (AES-GCM)
        // ---- AuthenticatedEncryption: a truncated blob ----

        public void Decrypt_ATruncatedBlob_FailsAsACryptographicException()
        {
            byte[] key = CryptoRandom.Bytes(32);
            byte[] blob = AuthenticatedEncryption.Encrypt(key, Encoding.UTF8.GetBytes("secret"));
            for (int n = 0; n < blob.Length; n++)
            {
                byte[] cut = blob.Take(n).ToArray();
                Exception? e = null;
                try { AuthenticatedEncryption.Decrypt(key, cut); } catch (Exception ex) { e = ex; }
                Check.True(e is CryptographicException, $"{n} bytes: {e?.GetType().Name ?? "no exception"}");
                e = null;
                try { AuthenticatedEncryption.DecryptToString(key, cut); } catch (Exception ex) { e = ex; }
                Check.True(e is CryptographicException, $"{n} bytes, as a string: {e?.GetType().Name ?? "no exception"}");
            }
            Check.Equal("secret", AuthenticatedEncryption.DecryptToString(key, blob));
        }

        // ---- SecretsFile: a key with a line break, from another writer sharing the passphrase ----

        // Encrypts plaintext lines in the documented format, as another tool would.
        private static byte[] SecretsBlob(string plaintext, string passphrase)
        {
            byte[] magic = Encoding.ASCII.GetBytes("TBSECRETS1"), salt = CryptoRandom.Bytes(16), nonce = CryptoRandom.Bytes(12);
            byte[] header = magic.Concat(BitConverter.GetBytes(1000)).Concat(salt).Concat(nonce).ToArray();
            byte[] key = Rfc2898DeriveBytes.Pbkdf2(passphrase, salt, 1000, HashAlgorithmName.SHA256, 32);
            byte[] plain = Encoding.UTF8.GetBytes(plaintext), cipher = new byte[plain.Length], tag = new byte[16];
            using (var aes = new AesGcm(key, 16)) aes.Encrypt(nonce, plain, cipher, tag, header);
            return header.Concat(tag).Concat(cipher).ToArray();
        }

        public void SecretsFile_AKeyWithALineBreak_IsMalformed()
        {
            string v = Convert.ToBase64String(Encoding.UTF8.GetBytes("v"));
            Check.Equal("a,b", string.Join(",", SecretsFile.FromBytes(SecretsBlob($"a={v}\nb={v}\n", "pw"), "pw").Keys));
            foreach (string bad in new[] { $"a\r={v}\n", $"a\rb={v}\n", $"\r={v}\n", $"ok={v}\r\nx\r={v}\n" })
            {
                Exception? e = null;
                try { SecretsFile.FromBytes(SecretsBlob(bad, "pw"), "pw"); } catch (Exception ex) { e = ex; }
                Check.True(e is CryptographicException, $"'{bad.Replace("\r", "\\r").Replace("\n", "\\n")}': {e?.GetType().Name ?? "taken"}");
            }
        }
#endif
    }
}
