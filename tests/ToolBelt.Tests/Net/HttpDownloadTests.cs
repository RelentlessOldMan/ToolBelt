using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ToolBelt.Net;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Net
{
    public sealed class HttpDownloadTests
    {
        private static readonly byte[] Body = Encoding.ASCII.GetBytes("hello world");
        // SHA-256 of "hello world".
        private const string BodySha256 = "b94d27b9934d3e08a52e52d7da7dabfac484efe37a5380ee9088f7ace2efcde9";

        private sealed class TestServer : IDisposable
        {
            private readonly HttpListener _listener = new HttpListener();
            public string Url { get; }

            public TestServer(Action<HttpListenerContext> handler)
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

        private static void RespondFull(HttpListenerContext ctx, int status, byte[]? body)
        {
            ctx.Response.StatusCode = status;
            if (body != null) { ctx.Response.ContentLength64 = body.Length; ctx.Response.OutputStream.Write(body, 0, body.Length); }
            ctx.Response.Close();
        }

        private static string TempFile() => Path.Combine(Path.GetTempPath(), "toolbelt-dl-" + Guid.NewGuid().ToString("N"));

        public async Task DownloadsToFile()
        {
            using var server = new TestServer(ctx => RespondFull(ctx, 200, Body));
            string dest = TempFile();
            try
            {
                long? lastTotal = null; long lastProgress = 0;
                var result = await HttpDownload.ToFileAsync(server.Url, dest,
                    new HttpDownloadOptions { OnProgress = (d, t) => { lastProgress = d; lastTotal = t; } });

                Check.True(File.ReadAllBytes(dest).SequenceEqual(Body));
                Check.Equal(11L, result.BytesDownloaded);
                Check.Equal(11L, lastProgress);
                Check.Equal(11L, lastTotal!.Value);
            }
            finally { File.Delete(dest); }
        }

        public async Task VerifiesChecksum()
        {
            using var server = new TestServer(ctx => RespondFull(ctx, 200, Body));
            string dest = TempFile();
            try
            {
                var ok = await HttpDownload.ToFileAsync(server.Url, dest, new HttpDownloadOptions { ExpectedSha256Hex = BodySha256 });
                Check.Equal(BodySha256, ok.Sha256Hex);

                await Check.ThrowsAsync<HttpDownloadException>(() =>
                    HttpDownload.ToFileAsync(server.Url, dest, new HttpDownloadOptions { ExpectedSha256Hex = new string('0', 64) }));
            }
            finally { File.Delete(dest); }
        }

        public async Task ResumesViaRange()
        {
            using var server = new TestServer(ctx =>
            {
                string? range = ctx.Request.Headers["Range"];
                if (range != null && range.StartsWith("bytes=", StringComparison.Ordinal))
                {
                    int start = int.Parse(range.Substring(6).TrimEnd('-'));
                    byte[] slice = Body.Skip(start).ToArray();
                    ctx.Response.StatusCode = 206;
                    ctx.Response.AddHeader("Content-Range", $"bytes {start}-{Body.Length - 1}/{Body.Length}");
                    ctx.Response.ContentLength64 = slice.Length;
                    ctx.Response.OutputStream.Write(slice, 0, slice.Length);
                    ctx.Response.Close();
                }
                else RespondFull(ctx, 200, Body);
            });
            string dest = TempFile();
            try
            {
                File.WriteAllBytes(dest, Body.Take(6).ToArray()); // "hello " already present
                var result = await HttpDownload.ToFileAsync(server.Url, dest, new HttpDownloadOptions { Resume = true });
                Check.True(result.Resumed);
                Check.True(File.ReadAllBytes(dest).SequenceEqual(Body));
            }
            finally { File.Delete(dest); }
        }

        public async Task RetriesTransientFailure()
        {
            int requests = 0;
            using var server = new TestServer(ctx =>
            {
                if (Interlocked.Increment(ref requests) == 1) { RespondFull(ctx, 500, null); }
                else RespondFull(ctx, 200, Body);
            });
            string dest = TempFile();
            try
            {
                var result = await HttpDownload.ToFileAsync(server.Url, dest,
                    new HttpDownloadOptions { MaxAttempts = 3, RetryBaseDelay = TimeSpan.FromMilliseconds(10) });
                Check.True(File.ReadAllBytes(dest).SequenceEqual(Body));
                Check.True(requests >= 2, $"expected a retry, saw {requests} request(s)");
            }
            finally { File.Delete(dest); }
        }

        public async Task DoesNotRetryClientError()
        {
            int requests = 0;
            using var server = new TestServer(ctx => { Interlocked.Increment(ref requests); RespondFull(ctx, 404, null); });
            string dest = TempFile();
            try
            {
                await Check.ThrowsAsync<HttpDownloadException>(() =>
                    HttpDownload.ToFileAsync(server.Url, dest, new HttpDownloadOptions { MaxAttempts = 3, RetryBaseDelay = TimeSpan.FromMilliseconds(10) }));
                Check.Equal(1, requests); // 4xx is not retried
            }
            finally { if (File.Exists(dest)) File.Delete(dest); }
        }

        public async Task NullArguments_Throw()
        {
            await Check.ThrowsAsync<ArgumentNullException>(() => HttpDownload.ToFileAsync(null!, "x"));
            await Check.ThrowsAsync<ArgumentNullException>(() => HttpDownload.ToFileAsync("http://x/", null!));
        }
    }
}
