// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ToolBelt.Net
{
    /// <summary>Options for <see cref="HttpDownload.ToFileAsync"/>.</summary>
    public sealed class HttpDownloadOptions
    {
        /// <summary>Progress callback: (bytes downloaded so far, total bytes if known).</summary>
        public Action<long, long?>? OnProgress { get; set; }

        /// <summary>If a partial file already exists, resume it with a Range request.</summary>
        public bool Resume { get; set; }

        /// <summary>Expected SHA-256 (hex) of the finished file; verified on completion.</summary>
        public string? ExpectedSha256Hex { get; set; }

        /// <summary>Total attempts for transient failures (network/5xx). Default 3.</summary>
        public int MaxAttempts { get; set; } = 3;

        /// <summary>Base delay between retry attempts (multiplied by the attempt number). Default 200 ms.</summary>
        public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromMilliseconds(200);

        /// <summary>An HTTP client to use. If null, a shared process-wide client is used. Never disposed by the download.</summary>
        public HttpClient? Client { get; set; }
    }

    /// <summary>The outcome of a download.</summary>
    public sealed class HttpDownloadResult
    {
        internal HttpDownloadResult(long bytesDownloaded, long totalBytes, bool resumed, string? sha256Hex)
        {
            BytesDownloaded = bytesDownloaded;
            TotalBytes = totalBytes;
            Resumed = resumed;
            Sha256Hex = sha256Hex;
        }

        /// <summary>Total size of the file on disk after the download (including any resumed prefix).</summary>
        public long BytesDownloaded { get; }
        /// <summary>Expected total size if the server reported it, else -1.</summary>
        public long TotalBytes { get; }
        public bool Resumed { get; }
        /// <summary>The computed SHA-256 (hex) if verification was requested, else null.</summary>
        public string? Sha256Hex { get; }
    }

    /// <summary>Thrown for non-retryable download failures (client errors, checksum mismatch).</summary>
    public sealed class HttpDownloadException : Exception
    {
        public HttpDownloadException(string message) : base(message) { }
    }

    /// <summary>
    /// Downloads a URL to a file with a progress callback, optional resume via an HTTP Range request,
    /// optional SHA-256 verification, and retries for transient (network / 5xx) failures. Uses the in-box
    /// HTTP client. A 4xx response or a checksum mismatch is not retried. The caller's <see cref="HttpClient"/>
    /// (or the shared default) is never disposed here.
    /// </summary>
    public static class HttpDownload
    {
        private static readonly Lazy<HttpClient> Shared = new Lazy<HttpClient>(() => new HttpClient());

        public static async Task<HttpDownloadResult> ToFileAsync(
            string url, string destinationPath, HttpDownloadOptions? options = null, CancellationToken cancellationToken = default)
        {
            if (url is null) throw new ArgumentNullException(nameof(url));
            if (destinationPath is null) throw new ArgumentNullException(nameof(destinationPath));
            options ??= new HttpDownloadOptions();
            if (options.MaxAttempts < 1) throw new ArgumentOutOfRangeException(nameof(options), options.MaxAttempts, "MaxAttempts must be at least 1.");
            HttpClient client = options.Client ?? Shared.Value;

            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    return await AttemptAsync(client, url, destinationPath, options, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (attempt < options.MaxAttempts && IsTransient(ex))
                {
                    await Task.Delay(TimeSpan.FromTicks(options.RetryBaseDelay.Ticks * attempt), cancellationToken).ConfigureAwait(false);
                }
            }
        }

        private static async Task<HttpDownloadResult> AttemptAsync(
            HttpClient client, string url, string destinationPath, HttpDownloadOptions options, CancellationToken ct)
        {
            long existing = options.Resume && File.Exists(destinationPath) ? new FileInfo(destinationPath).Length : 0;

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (existing > 0) request.Headers.Range = new RangeHeaderValue(existing, null);

            using HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            int status = (int)response.StatusCode;

            bool resumed;
            FileMode mode;
            if (response.StatusCode == HttpStatusCode.PartialContent) { resumed = true; mode = FileMode.Append; }
            else if (response.StatusCode == HttpStatusCode.OK) { existing = 0; resumed = false; mode = FileMode.Create; } // server ignored the range
            else if (status >= 400 && status < 500) throw new HttpDownloadException($"Server returned {status} ({response.ReasonPhrase}) for {url}.");
            else { response.EnsureSuccessStatusCode(); resumed = false; mode = FileMode.Create; } // 5xx -> transient HttpRequestException

            long? contentLength = response.Content.Headers.ContentLength;
            long total = contentLength.HasValue ? existing + contentLength.Value : -1;
            long downloaded = existing;

            options.OnProgress?.Invoke(downloaded, total >= 0 ? total : (long?)null);

            string? directory = Path.GetDirectoryName(Path.GetFullPath(destinationPath));
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

#if NETSTANDARD2_0
            using (Stream input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
#else
            using (Stream input = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
#endif
            using (var output = new FileStream(destinationPath, mode, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[81920];
                int read;
                while ((read = await input.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false)) > 0)
                {
                    await output.WriteAsync(buffer, 0, read, ct).ConfigureAwait(false);
                    downloaded += read;
                    options.OnProgress?.Invoke(downloaded, total >= 0 ? total : (long?)null);
                }
            }

            string? sha = null;
            if (!string.IsNullOrEmpty(options.ExpectedSha256Hex))
            {
                sha = Sha256Hex(destinationPath);
                if (!string.Equals(sha, options.ExpectedSha256Hex, StringComparison.OrdinalIgnoreCase))
                    throw new HttpDownloadException($"Checksum mismatch for {destinationPath}: expected {options.ExpectedSha256Hex}, got {sha}.");
            }

            return new HttpDownloadResult(downloaded, total, resumed, sha);
        }

        private static string Sha256Hex(string path)
        {
            using var sha = SHA256.Create();
            using FileStream fs = File.OpenRead(path);
            byte[] hash = sha.ComputeHash(fs);
            var sb = new StringBuilder(hash.Length * 2);
            foreach (byte b in hash) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        private static bool IsTransient(Exception ex)
        {
            for (Exception? e = ex; e != null; e = e.InnerException)
            {
                if (e is OperationCanceledException) return false;
                if (e is HttpRequestException || e is IOException || e is TimeoutException) return true;
                if (e.GetType().FullName == "System.Net.Sockets.SocketException") return true;
            }
            return false;
        }
    }
}
