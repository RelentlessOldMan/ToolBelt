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

        /// <summary>
        /// Resume a partial download (<c>destination.part</c>, or else an existing destination) with a Range request, and
        /// keep the partial file after a failure so a later call can resume it.
        /// </summary>
        public bool Resume { get; set; }

        /// <summary>Expected SHA-256 (hex) of the finished file; verified on completion.</summary>
        public string? ExpectedSha256Hex { get; set; }

        /// <summary>Total attempts for transient failures (network/5xx). Default 3.</summary>
        public int MaxAttempts { get; set; } = 3;

        /// <summary>Base delay between retry attempts (multiplied by the attempt number). Default 200 ms.</summary>
        public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromMilliseconds(200);

        /// <summary>
        /// The longest the response body may go without delivering a byte before the attempt fails with a
        /// <see cref="TimeoutException"/> (which is retried). Default 60 s; <see cref="Timeout.InfiniteTimeSpan"/> waits forever.
        /// The client's own <see cref="HttpClient.Timeout"/> covers only the wait for the response headers.
        /// </summary>
        public TimeSpan ReadTimeout { get; set; } = TimeSpan.FromSeconds(60);

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
        /// <summary>True if the file was completed from a partial download rather than fetched whole.</summary>
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
    /// optional SHA-256 verification, and retries for transient failures (network errors, 5xx, a stalled body, the
    /// client's own timeout). Uses the in-box HTTP client. Other responses (4xx, an unfollowed redirect) and a checksum
    /// mismatch are not retried. The caller's <see cref="HttpClient"/> (or the shared default) is never disposed here.
    /// <para>
    /// The body goes to <c>destination.part</c>, which replaces the destination only once it is complete (and verified),
    /// so a failed download never damages an existing file. With <see cref="HttpDownloadOptions.Resume"/> the partial
    /// file is kept after a failure, and the next attempt or call continues from it; if there is no partial file but the
    /// destination exists, the destination is taken as the partial download (it is copied, not moved). A resumed range
    /// is appended only if the server's Content-Range starts where the partial file ends; a 416 answer saying the file
    /// is already complete finishes the download without fetching anything.
    /// </para>
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
            if (options.ReadTimeout <= TimeSpan.Zero && options.ReadTimeout != Timeout.InfiniteTimeSpan)
                throw new ArgumentOutOfRangeException(nameof(options), options.ReadTimeout, "ReadTimeout must be positive or Timeout.InfiniteTimeSpan.");
            HttpClient client = options.Client ?? Shared.Value;
            string destination = Path.GetFullPath(destinationPath);
            string partial = destination + ".part";

            try
            {
                for (int attempt = 1; ; attempt++)
                {
                    try
                    {
                        return await AttemptAsync(client, url, destination, partial, options, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (attempt < options.MaxAttempts && IsTransient(ex, cancellationToken))
                    {
                        await Task.Delay(TimeSpan.FromTicks(options.RetryBaseDelay.Ticks * attempt), cancellationToken).ConfigureAwait(false);
                    }
                }
            }
            catch
            {
                if (!options.Resume) TryDelete(partial);   // nothing will resume from it
                throw;
            }
        }

        private static async Task<HttpDownloadResult> AttemptAsync(
            HttpClient client, string url, string destination, string partial, HttpDownloadOptions options, CancellationToken ct)
        {
            string? directory = Path.GetDirectoryName(destination);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            long existing = 0;
            if (options.Resume)
            {
                if (!File.Exists(partial) && File.Exists(destination)) File.Copy(destination, partial);
                if (File.Exists(partial)) existing = new FileInfo(partial).Length;
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (existing > 0) request.Headers.Range = new RangeHeaderValue(existing, null);

            using HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            int status = (int)response.StatusCode;
            ContentRangeHeaderValue? range = response.Content.Headers.ContentRange;
            long? contentLength = response.Content.Headers.ContentLength;

            bool resumed;
            long? total;
            if (status == 416 && existing > 0 && range != null && range.Length == existing)
            {
                // "bytes */N" with N == what we have: the partial file is already the whole file.
                return Finish(destination, partial, options, existing, existing, resumed: true);
            }
            if (status == 206 && existing > 0 && (range == null || range.From == existing))
            {
                resumed = true;
                total = range?.Length ?? (contentLength.HasValue ? existing + contentLength.Value : (long?)null);
            }
            else if (status == 206 && range != null && range.From == 0)
            {
                existing = 0; resumed = false;   // the server sent the whole file as one range
                total = range.Length ?? contentLength;
            }
            else if (status >= 200 && status < 300 && status != 206)
            {
                existing = 0; resumed = false;   // the server ignored the range (or there was none)
                total = contentLength;
            }
            else if (status == 206)
                throw new HttpDownloadException($"Server returned a range starting at byte {range?.From} for {url}; byte {existing} was requested.");
            else if (status == 416 && existing > 0)
            {
                TryDelete(partial);
                throw new HttpDownloadException($"Server rejected the resume range for {url} (416); the partial file was deleted, so the next download starts over.");
            }
            else if (status >= 500)
            {
                response.EnsureSuccessStatusCode();   // 5xx -> transient HttpRequestException
                throw new InvalidOperationException("unreachable");
            }
            else
                throw new HttpDownloadException($"Server returned {status} ({response.ReasonPhrase}) for {url}.");

            long downloaded = existing;
            options.OnProgress?.Invoke(downloaded, total);

            using (var idle = CancellationTokenSource.CreateLinkedTokenSource(ct))
            // Where a response stream ignores cancellation, disposing the response unblocks the read.
            using (idle.Token.Register(() => response.Dispose()))
            {
                try
                {
#if NETSTANDARD2_0
                    using Stream input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
#else
                    using Stream input = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
#endif
                    using var output = new FileStream(partial, existing > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None);
                    var buffer = new byte[81920];
                    while (true)
                    {
                        idle.CancelAfter(options.ReadTimeout);
                        int read = await input.ReadAsync(buffer, 0, buffer.Length, idle.Token).ConfigureAwait(false);
                        if (read == 0) break;
                        await output.WriteAsync(buffer, 0, read, ct).ConfigureAwait(false);
                        downloaded += read;
                        options.OnProgress?.Invoke(downloaded, total);
                    }
                }
                catch (Exception ex) when (idle.IsCancellationRequested && !ct.IsCancellationRequested)
                {
                    throw new TimeoutException($"No data from {url} for {options.ReadTimeout}.", ex);
                }
            }

            if (total.HasValue && downloaded != total.Value)
                throw new IOException($"The response from {url} ended after {downloaded} of {total.Value} bytes.");
            return Finish(destination, partial, options, downloaded, total ?? -1, resumed);
        }

        // Verifies the partial file if asked, then moves it over the destination.
        private static HttpDownloadResult Finish(string destination, string partial, HttpDownloadOptions options, long downloaded, long total, bool resumed)
        {
            string? sha = null;
            if (!string.IsNullOrEmpty(options.ExpectedSha256Hex))
            {
                sha = Sha256Hex(partial);
                if (!string.Equals(sha, options.ExpectedSha256Hex, StringComparison.OrdinalIgnoreCase))
                {
                    TryDelete(partial);   // resuming would only extend the wrong bytes
                    throw new HttpDownloadException($"Checksum mismatch for {destination}: expected {options.ExpectedSha256Hex}, got {sha}.");
                }
            }
#if NETSTANDARD2_0
            if (File.Exists(destination)) File.Delete(destination);
            File.Move(partial, destination);
#else
            File.Move(partial, destination, overwrite: true);
#endif
            return new HttpDownloadResult(downloaded, total, resumed, sha);
        }

        private static void TryDelete(string path)
        {
            try { File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
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

        private static bool IsTransient(Exception ex, CancellationToken ct)
        {
            if (ct.IsCancellationRequested) return false;   // the caller cancelled
            for (Exception? e = ex; e != null; e = e.InnerException)
            {
                if (e is HttpDownloadException) return false;
                // With the caller's token untouched, a cancellation is the HttpClient's own Timeout firing.
                if (e is OperationCanceledException) return true;
                if (e is HttpRequestException || e is IOException || e is TimeoutException) return true;
                if (e.GetType().FullName == "System.Net.Sockets.SocketException") return true;
            }
            return false;
        }
    }
}
