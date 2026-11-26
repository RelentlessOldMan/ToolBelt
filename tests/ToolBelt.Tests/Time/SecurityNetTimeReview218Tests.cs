using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ToolBelt.Net;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Time
{
    /// <summary>Round 218 review regressions (security, net, time, thirteenth pass): each failed on the code before its fix.</summary>
    public sealed class SecurityNetTimeReview218Tests
    {
        // ---- HttpDownload ----

        private sealed class RawServer : IDisposable
        {
            private readonly TcpListener _listener = new TcpListener(IPAddress.Loopback, 0);
            public string Url { get; }

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

        private static bool AsksRange(string head) => head.IndexOf("\r\nRange:", StringComparison.OrdinalIgnoreCase) >= 0;

        private static byte[] Bytes(int from, int count)
        {
            var b = new byte[count];
            for (int i = 0; i < count; i++) b[i] = (byte)('a' + (from + i) % 26);
            return b;
        }

        private static string NewDest() => Path.Combine(Path.GetTempPath(), "toolbelt-dl218-" + Guid.NewGuid().ToString("N"));

        private static void Clean(string dest)
        {
            foreach (string p in new[] { dest, dest + ".part" }) try { File.Delete(p); } catch { }
        }

        // A resume answered "bytes 10-19/*" (no total) failed but kept the partial file, so every later call asked for the
        // same range and failed the same way, though the server sends the whole file to a request without a range.
        public async Task HttpDownload_ResumeWithoutATotal_StartsOverNextTime()
        {
            using var server = new RawServer((head, ns) => AsksRange(head)
                ? Respond(ns, "206 Partial Content", Bytes(10, 10), "Content-Range: bytes 10-19/*")
                : Respond(ns, "200 OK", Bytes(0, 20)));
            string dest = NewDest();
            try
            {
                File.WriteAllBytes(dest + ".part", Bytes(0, 10));
                var options = new HttpDownloadOptions { Resume = true, MaxAttempts = 1 };
                await Check.ThrowsAsync<HttpDownloadException>(() => HttpDownload.ToFileAsync(server.Url, dest, options));
                var r = await HttpDownload.ToFileAsync(server.Url, dest, options);
                Check.Equal(20L, r.BytesDownloaded);
                Check.Equal(Convert.ToBase64String(Bytes(0, 20)), Convert.ToBase64String(File.ReadAllBytes(dest)));
            }
            finally { Clean(dest); }
        }

        // A short range from byte 0 that ended before the partial file did (bytes 0-5 of 20 for a 10-byte partial file)
        // truncated the partial file to 6 bytes and then failed saying it "adds nothing to the 10 bytes already
        // downloaded": those 4 bytes were lost. It fails with the partial file as it was.
        public async Task HttpDownload_ShortRangeFromZero_KeepsThePartialFile()
        {
            using var server = new RawServer((head, ns) => Respond(ns, "206 Partial Content", Bytes(0, 6), "Content-Range: bytes 0-5/20"));
            string dest = NewDest();
            try
            {
                File.WriteAllBytes(dest + ".part", Bytes(0, 10));
                var ex = await Check.ThrowsAsync<HttpDownloadException>(() =>
                    HttpDownload.ToFileAsync(server.Url, dest, new HttpDownloadOptions { Resume = true, MaxAttempts = 1 }));
                Check.True(ex.Message.Contains("adds nothing"), ex.Message);
                Check.Equal(Convert.ToBase64String(Bytes(0, 10)), Convert.ToBase64String(File.ReadAllBytes(dest + ".part")));
            }
            finally { Clean(dest); }
        }

        // A 204 (or 205) has no representation, but it was taken as an empty file and replaced the destination with it.
        public async Task HttpDownload_NoContent_IsNotAnEmptyFile()
        {
            foreach (string status in new[] { "204 No Content", "205 Reset Content" })
            {
                using var server = new RawServer((head, ns) => Respond(ns, status, status.StartsWith("205", StringComparison.Ordinal) ? new byte[0] : null));
                foreach (bool resume in new[] { false, true })
                {
                    string dest = NewDest();
                    try
                    {
                        File.WriteAllBytes(dest, Bytes(0, 8));
                        var ex = await Check.ThrowsAsync<HttpDownloadException>(() =>
                            HttpDownload.ToFileAsync(server.Url, dest, new HttpDownloadOptions { Resume = resume, MaxAttempts = 1 }));
                        Check.True(ex.Message.Contains(status.Substring(0, 3)), ex.Message);
                        Check.Equal(Convert.ToBase64String(Bytes(0, 8)), Convert.ToBase64String(File.ReadAllBytes(dest)), status);
                    }
                    finally { Clean(dest); }
                }
            }
        }
    }
}
