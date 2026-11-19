using System;
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

namespace ToolBelt.Tests.Time
{
    // Round 149 review regressions (Security, net, time, ninth pass): each failed on the code before its fix.
    public sealed class SecurityNetTimeReview9Tests
    {
        // ---- TcpLineClient ----

        // A peer that sends `data` once a client connects, then holds the connection open until disposed.
        private sealed class Peer : IDisposable
        {
            private readonly TcpListener _listener = new TcpListener(IPAddress.Loopback, 0);
            private readonly ManualResetEventSlim _done = new ManualResetEventSlim();
            public int Port { get; }
            public string Received = "";

            public Peer(byte[] data)
            {
                _listener.Start();
                Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        using TcpClient c = await _listener.AcceptTcpClientAsync();
                        NetworkStream s = c.GetStream();
                        await s.WriteAsync(data, 0, data.Length);
                        var buf = new byte[256];
                        s.ReadTimeout = 10000;
                        int n;
                        try { while ((n = s.Read(buf, 0, buf.Length)) > 0) Received += Encoding.ASCII.GetString(buf, 0, n); }
                        catch (IOException) { }
                        _done.Wait(10000);
                    }
                    catch { }
                });
            }

            public void Dispose() { _done.Set(); _listener.Stop(); }
        }

        private static TcpLineClient Connect(Peer peer, Encoding? encoding = null)
            => new TcpLineClient("127.0.0.1", peer.Port, connectTimeout: TimeSpan.FromSeconds(5), readTimeout: TimeSpan.FromSeconds(3), encoding: encoding);

        // Bad bytes made no characters, so a line of them was read in full however long (then DecoderFallbackException).
        public void TcpLineClient_BadBytesCountTowardMaxLineLength()
        {
            byte[] data = Enumerable.Repeat((byte)0xFF, 200_000).Concat(Encoding.ASCII.GetBytes("\nok\n")).ToArray();
            using var peer = new Peer(data);
            using var client = Connect(peer, new UTF8Encoding(false, true));
            client.MaxLineLength = 1000;
            Check.Throws<InvalidDataException>(() => client.ReadLine());
            string? next = null;
            for (int i = 0; i < 1000 && next == null; i++)
            {
                try { next = client.ReadLine(); }
                catch (InvalidDataException) { }
            }
            Check.Equal("ok", next);
            // Guard: a short bad line still throws DecoderFallbackException.
            using var peer2 = new Peer(new byte[] { 0x61, 0xFF, 0x62, 0x0A, 0x63, 0x0A });
            using var client2 = Connect(peer2, new UTF8Encoding(false, true));
            client2.MaxLineLength = 3;
            Check.Throws<DecoderFallbackException>(() => client2.ReadLine());
            Check.Equal("c", client2.ReadLine());
        }

        // The rest of an overlong line was skipped without limit: a peer that kept sending held ReadLine for good.
        public void TcpLineClient_SkippingAnOverlongLineThrowsAgainAfterEachMaxLineLength()
        {
            byte[] data = Encoding.ASCII.GetBytes(new string('a', 10_000) + "\nnext\n");
            using var peer = new Peer(data);
            using var client = Connect(peer);
            client.MaxLineLength = 100;
            Check.Throws<InvalidDataException>(() => client.ReadLine());
            Check.Throws<InvalidDataException>(() => client.ReadLine());   // another 100 skipped, no terminator yet
            int throws = 2;
            string? next = null;
            while (next == null && throws < 1000)
            {
                try { next = client.ReadLine(); }
                catch (InvalidDataException) { throws++; }
            }
            Check.Equal("next", next);
            Check.True(throws >= 90 && throws <= 101, $"{throws} throws");
        }

        // A character the encoding cannot represent went out as '?', and the send reported success.
        public void TcpLineClient_SendRefusesACharacterTheEncodingCannotEncode()
        {
            using var peer = new Peer(new byte[0]);
            using (var client = Connect(peer, Encoding.ASCII))
            {
                var ex = Check.Throws<ArgumentException>(() => client.Send("café"));
                Check.Equal("line", ex.ParamName);
                Check.True(ex.Message.Contains("index 3"), ex.Message);
                client.Send("cafe");   // guard
            }
            Check.True(SpinWait.SpinUntil(() => peer.Received.Contains("cafe\n"), 5000), peer.Received);
            Check.False(peer.Received.Contains("caf?"), peer.Received);
        }

        // ---- The DNS API's parameter name ----

        public void Host_ALongNameIsReportedAsHost()
        {
            string host = new string('a', 300);
            var e1 = Check.Throws<ArgumentException>(() => HostInfo.Resolve(host, TimeSpan.FromSeconds(5)));
            Check.Equal("host", e1.ParamName);
            var e2 = Check.Throws<ArgumentException>(() => new TcpLineClient(host, 80, connectTimeout: TimeSpan.FromSeconds(5)));
            Check.Equal("host", e2.ParamName);
            // It returned false, a caller's mistake read as "unreachable".
            var e3 = Check.Throws<ArgumentException>(() => PortCheck.IsReachable(host, 80, TimeSpan.FromSeconds(5)));
            Check.Equal("host", e3.ParamName);
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

        private static void Respond(HttpListenerContext ctx, int status, string? contentRange, byte[] body, bool chunked = false)
        {
            ctx.Response.StatusCode = status;
            if (contentRange != null) ctx.Response.AddHeader("Content-Range", contentRange);
            if (chunked) ctx.Response.SendChunked = true;
            else ctx.Response.ContentLength64 = body.Length;
            ctx.Response.OutputStream.Write(body, 0, body.Length);
            ctx.Response.Close();
        }

        private static readonly byte[] Body = Encoding.ASCII.GetBytes("ABCDEFGHIJKLMNOPQRST");   // 20 bytes

        private static string TempPath() => Path.Combine(Path.GetTempPath(), "toolbelt-r149-" + Guid.NewGuid().ToString("N"));

        private static void Cleanup(string dest)
        {
            string dir = Path.GetDirectoryName(dest)!;
            foreach (string f in Directory.GetFiles(dir, Path.GetFileName(dest) + "*")) try { File.Delete(f); } catch { }
        }

        // "bytes 0-9/*" (no total) was taken as the whole file.
        public async Task HttpDownload_ARangeWithNoTotalIsNotTakenAsComplete()
        {
            using var server = new Server(ctx => Respond(ctx, 206, "bytes 0-9/*", Body.Take(10).ToArray()));
            string dest = TempPath();
            try
            {
                await Check.ThrowsAsync<HttpDownloadException>(() => HttpDownload.ToFileAsync(server.Url, dest, new HttpDownloadOptions { MaxAttempts = 3 }));
                Check.False(File.Exists(dest));
                Check.Equal(1, server.Requests);
            }
            finally { Cleanup(dest); }
        }

        // A 206 covering only part of the file was retried whole as a truncated body, failing every attempt alike.
        public async Task HttpDownload_AShortRangeIsContinuedUnderResumeAndFailsAtOnceWithout()
        {
            using var server = new Server(ctx =>
            {
                string? range = ctx.Request.Headers["Range"];
                int from = range == null ? 0 : int.Parse(range.Substring(6).TrimEnd('-'), System.Globalization.CultureInfo.InvariantCulture);
                int to = Math.Min(from + 6, Body.Length) - 1;
                Respond(ctx, 206, $"bytes {from}-{to}/{Body.Length}", Body.Skip(from).Take(to - from + 1).ToArray());
            });
            string dest = TempPath();
            try
            {
                var result = await HttpDownload.ToFileAsync(server.Url, dest, new HttpDownloadOptions { Resume = true, MaxAttempts = 1 });
                Check.True(File.ReadAllBytes(dest).SequenceEqual(Body), Encoding.ASCII.GetString(File.ReadAllBytes(dest)));
                Check.Equal(20L, result.BytesDownloaded);
                Check.Equal(4, server.Requests);   // 0-5, 6-11, 12-17, 18-19
                File.Delete(dest);

                var ex = await Check.ThrowsAsync<HttpDownloadException>(() =>
                    HttpDownload.ToFileAsync(server.Url, dest, new HttpDownloadOptions { MaxAttempts = 3 }));
                Check.True(ex.Message.Contains("Resume"), ex.Message);
                Check.Equal(5, server.Requests);   // not retried
                Check.False(File.Exists(dest));
            }
            finally { Cleanup(dest); }
        }

        // A non-Resume download deleted the ".part" a Resume download had kept to continue from.
        public async Task HttpDownload_WithoutResumeAKeptPartialIsLeftAlone()
        {
            using var server = new Server(ctx => Respond(ctx, 500, null, new byte[0]));
            string dest = TempPath();
            try
            {
                File.WriteAllText(dest + ".part", "HELLO");
                await Check.ThrowsAsync<Exception>(() => HttpDownload.ToFileAsync(server.Url, dest, new HttpDownloadOptions { MaxAttempts = 1 }));
                Check.Equal("HELLO", File.ReadAllText(dest + ".part"));
                string dir = Path.GetDirectoryName(dest)!;
                Check.Equal(1, Directory.GetFiles(dir, Path.GetFileName(dest) + "*").Length);   // no stray partial of its own
            }
            finally { Cleanup(dest); }
        }

        // An ftp: URL failed only in SendAsync: under Resume the destination had been copied to ".part" by then, and
        // without it a new folder had been created.
        public async Task HttpDownload_AnUnusableUrlTouchesNothing()
        {
            string dest = TempPath();
            string folder = TempPath();
            try
            {
                File.WriteAllBytes(dest, new byte[1000]);
                var e1 = await Check.ThrowsAsync<ArgumentException>(() =>
                    HttpDownload.ToFileAsync("ftp://127.0.0.1/x", dest, new HttpDownloadOptions { Resume = true }));
                Check.Equal("url", e1.ParamName);
                Check.False(File.Exists(dest + ".part"));
                // A server's refusal does not leave a folder behind either.
                using var server = new Server(ctx => Respond(ctx, 404, null, new byte[0]));
                await Check.ThrowsAsync<HttpDownloadException>(() =>
                    HttpDownload.ToFileAsync(server.Url, Path.Combine(folder, "f.bin"), new HttpDownloadOptions { MaxAttempts = 1 }));
                Check.False(Directory.Exists(folder));
            }
            finally { Cleanup(dest); try { Directory.Delete(folder, true); } catch { } }
        }

        // A folder for a destination downloaded the whole body, then failed to replace it.
        public async Task HttpDownload_AFolderDestinationIsRefusedUpFront()
        {
            using var server = new Server(ctx => Respond(ctx, 200, null, Body));
            string dir = TempPath();
            Directory.CreateDirectory(dir);
            try
            {
                foreach (string d in new[] { dir, dir + Path.DirectorySeparatorChar, Path.Combine(dir, "new") + Path.DirectorySeparatorChar })
                {
                    var ex = await Check.ThrowsAsync<ArgumentException>(() =>
                        HttpDownload.ToFileAsync(server.Url, d, new HttpDownloadOptions { Resume = true, MaxAttempts = 1 }));
                    Check.Equal("destinationPath", ex.ParamName);
                }
                Check.Equal(0, server.Requests);
                Check.Equal(0, Directory.GetFileSystemEntries(dir).Length);
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        // ReadTimeout below a millisecond was truncated to 0 ms, so a body already received timed out.
        public async Task HttpDownload_ASubMillisecondReadTimeoutIsRoundedUp()
        {
            using var server = new Server(ctx => Respond(ctx, 200, null, Body));
            string dest = TempPath();
            try
            {
                for (int i = 0; i < 10; i++)
                {
                    await HttpDownload.ToFileAsync(server.Url, dest, new HttpDownloadOptions { MaxAttempts = 1, ReadTimeout = TimeSpan.FromTicks(1) });
                    Check.True(File.ReadAllBytes(dest).SequenceEqual(Body));
                }
            }
            finally { Cleanup(dest); }
        }

        // New: MaxBytes caps a declared size and a body with none.
        public async Task HttpDownload_MaxBytesCapsTheFile()
        {
            byte[] big = new byte[65536];
            bool chunked = false;
            using var server = new Server(ctx => Respond(ctx, 200, null, big, chunked));
            string dest = TempPath();
            try
            {
                var opts = new HttpDownloadOptions { MaxBytes = 1000, MaxAttempts = 3 };
                await Check.ThrowsAsync<HttpDownloadException>(() => HttpDownload.ToFileAsync(server.Url, dest, opts));
                chunked = true;
                await Check.ThrowsAsync<HttpDownloadException>(() => HttpDownload.ToFileAsync(server.Url, dest, opts));
                Check.Equal(2, server.Requests);   // neither retried
                Check.Equal(0, Directory.GetFiles(Path.GetDirectoryName(dest)!, Path.GetFileName(dest) + "*").Length);
                opts.MaxBytes = big.Length;   // guard: exactly the limit is fine
                await HttpDownload.ToFileAsync(server.Url, dest, opts);
                Check.Equal((long)big.Length, new FileInfo(dest).Length);
                Check.Throws<ArgumentOutOfRangeException>(() => HttpDownload.ToFileAsync(server.Url, dest, new HttpDownloadOptions { MaxBytes = -1 }).GetAwaiter().GetResult());
            }
            finally { Cleanup(dest); }
        }

        // ---- Secrets: hardening, behaviour unchanged ----

        public void Pbkdf2_StillMatchesTheRfc6070Vector()
        {
            byte[] key = KeyDerivation.Pbkdf2("password", Encoding.ASCII.GetBytes("salt"), 2, 32);
            Check.Equal("ae4d0c95af6b46d32d0adff928f06dd02a303f8ef3c251dfd6e2d85a95474c43", string.Concat(key.Select(b => b.ToString("x2"))));
            string h = PasswordHasher.Hash("pw", 10);
            Check.True(PasswordHasher.Verify("pw", h));
            Check.False(PasswordHasher.Verify("pW", h));
        }
    }
}
