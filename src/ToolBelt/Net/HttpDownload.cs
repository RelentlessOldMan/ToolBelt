// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Runtime.ExceptionServices;
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

        /// <summary>
        /// The largest file accepted, in bytes (counting a resumed prefix); null, the default, has no limit. A larger
        /// declared size fails before the body is read, and a body that runs past it (one with no declared size) fails
        /// once it does; neither is retried, and the partial file is deleted.
        /// </summary>
        public long? MaxBytes { get; set; }

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
    /// The body goes to <c>destination.part</c> (without <see cref="HttpDownloadOptions.Resume"/>, a name of its own,
    /// <c>destination.&lt;random&gt;.part</c>, so a kept partial file or another download is never touched), which replaces
    /// the destination only once it is complete (and verified), so a failed download never damages an existing file.
    /// With <see cref="HttpDownloadOptions.Resume"/> the partial
    /// file is kept after a failure, and the next attempt or call continues from it; if there is no partial file but the
    /// destination exists, the destination is taken as the partial download (it is copied, not moved). A resumed range
    /// is appended only if the server's Content-Range starts where the partial file ends; a 416 answer saying the file
    /// is already complete finishes the download without fetching anything. A 206 that covers only part of what is
    /// left is appended and the rest asked for (that does not count as an attempt); without Resume it fails, since
    /// nothing may be asked for by range. A 206 must give the file's total size. A 204 or 205 answer (no content) is
    /// an error, not an empty file.
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
            if (options.RetryBaseDelay < TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(options), options.RetryBaseDelay, "RetryBaseDelay must not be negative.");
            if (options.MaxBytes < 0)
                throw new ArgumentOutOfRangeException(nameof(options), options.MaxBytes, "MaxBytes must not be negative.");
            // A checksum that can never match cost a whole download each time (and under Resume discarded the progress).
            string? expected = options.ExpectedSha256Hex;
            if (!string.IsNullOrEmpty(expected) && (expected!.Length != 64 || !IsHex(expected)))
                throw new ArgumentException("ExpectedSha256Hex must be 64 hexadecimal characters.", nameof(options));
            HttpClient client = options.Client ?? Shared.Value;
            // An empty or relative URL failed in SendAsync as InvalidOperationException, after the folder was created.
            if (client.BaseAddress is null && !Uri.TryCreate(url, UriKind.Absolute, out _))
                throw new ArgumentException($"'{url}' is not an absolute URL, and the client has no base address.", nameof(url));
            // The default client speaks only HTTP: an ftp: or file: URL (a bare path parses as one) failed in SendAsync,
            // after the local files were touched. A caller's client may have a handler for other schemes.
            if (options.Client is null && Uri.TryCreate(url, UriKind.Absolute, out Uri? absolute)
                && absolute.Scheme != Uri.UriSchemeHttp && absolute.Scheme != Uri.UriSchemeHttps)
                throw new ArgumentException($"'{url}' is not an http or https URL.", nameof(url));
            if (destinationPath.Trim().Length == 0)
                throw new ArgumentException("The destination path is empty.", nameof(destinationPath));
            string destination;
            try { destination = Path.GetFullPath(destinationPath); }
            catch (ArgumentException ex) { throw new ArgumentException(ex.Message, nameof(destinationPath), ex); }   // was "path"
            // A folder for a destination downloaded the whole body, then failed to replace it (and under Resume left a
            // stray ".part" inside it).
            if (destinationPath.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                || destinationPath.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                || Directory.Exists(destination))
                throw new ArgumentException($"The destination '{destinationPath}' is a folder, not a file.", nameof(destinationPath));
            // A parent that is a file fails at once, before the request, as creating the folder there did; this only
            // looks (nothing is created until the server's answer is known).
            for (string? dir = Path.GetDirectoryName(destination); !string.IsNullOrEmpty(dir); dir = Path.GetDirectoryName(dir))
            {
                if (Directory.Exists(dir)) break;
                if (File.Exists(dir)) throw new IOException($"The destination's folder '{dir}' is a file.");
            }
            // Without Resume the shared name was deleted at a failure, though a Resume call had kept it to continue from
            // (or another download was writing it): a name of its own touches neither.
            string partial = options.Resume ? destination + ".part" : destination + "." + Guid.NewGuid().ToString("N").Substring(0, 12) + ".part";

            try
            {
                for (int attempt = 1; ; attempt++)
                {
                    try
                    {
                        return await AttemptAsync(client, url, destination, partial, options, cancellationToken).ConfigureAwait(false);
                    }
                    catch (MoreToFetch)
                    {
                        attempt--;   // progress, not a failure: each such answer grew the partial file (checked in AttemptAsync)
                    }
                    catch (Exception ex) when (attempt < options.MaxAttempts && IsTransient(ex, cancellationToken))
                    {
                        // Capped at int.MaxValue ms (Task.Delay's limit on .NET Framework); the product cannot overflow either.
                        double ms = Math.Min(options.RetryBaseDelay.TotalMilliseconds * attempt, int.MaxValue);
                        await Task.Delay(TimeSpan.FromMilliseconds(ms), cancellationToken).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex)
            {
                if (!options.Resume) TryDelete(partial);   // nothing will resume from it
                if (ex is ProgressFailure f) ExceptionDispatchInfo.Capture(f.InnerException!).Throw();
                if (ex is LocalFailure l) ExceptionDispatchInfo.Capture(l.InnerException!).Throw();
                throw;
            }
        }

        // An OnProgress exception, carried out of the attempt so it isn't taken for a network failure: an IOException,
        // TimeoutException or OperationCanceledException from the callback (a common way to abort) restarted the download.
        private sealed class ProgressFailure : Exception
        {
            public ProgressFailure(Exception inner) : base(inner.Message, inner) { }
        }

        // An IOException from the local files (a full disk, a parent path that is a file, a locked destination), carried
        // out of the attempt the same way: retrying would download the same bytes only to fail the same way.
        private sealed class LocalFailure : Exception
        {
            public LocalFailure(Exception inner) : base(inner.Message, inner) { }
        }

        // A resumed 206 covered only part of what was left: the next request continues after it.
        private sealed class MoreToFetch : Exception { }

        private static T Local<T>(Func<T> action)
        {
            try { return action(); }
            catch (IOException ex) { throw new LocalFailure(ex); }
        }

        private static void Progress(HttpDownloadOptions options, long downloaded, long? total)
        {
            Action<long, long?>? onProgress = options.OnProgress;
            if (onProgress == null) return;
            try { onProgress(downloaded, total); }
            catch (Exception ex) { throw new ProgressFailure(ex); }
        }

        private static async Task<HttpDownloadResult> AttemptAsync(
            HttpClient client, string url, string destination, string partial, HttpDownloadOptions options, CancellationToken ct)
        {
            // Nothing local is changed until the answer is known: the folder was created and a destination copied to the
            // partial file before an unusable URL or answer failed.
            bool fromDestination = false;
            long existing = Local(() =>
            {
                if (!options.Resume) return 0L;
                if (File.Exists(partial)) return new FileInfo(partial).Length;
                fromDestination = File.Exists(destination);
                return fromDestination ? new FileInfo(destination).Length : 0L;
            });
            long had = existing;   // what the partial file held before this answer
            // The destination taken as the partial download is copied only when its bytes are kept.
            void TakeDestination() => Local(() =>
            {
                if (fromDestination && !File.Exists(partial)) File.Copy(destination, partial);
                return 0;
            });

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
                // "bytes */N" with N == what we have: the partial file is already the whole file. It is held to MaxBytes
                // as the 206 path is (a partial file past the limit finished here), and progress is reported as there.
                if (options.MaxBytes is long cap && existing > cap)
                {
                    Discard(partial, destination);
                    throw new HttpDownloadException($"The file at {url} is {existing} bytes, more than MaxBytes ({cap}); the partial file was deleted.");
                }
                TakeDestination();
                Progress(options, existing, existing);
                return Local(() => Finish(destination, partial, options, existing, existing, resumed: true));
            }
            if (status == 206 && existing > 0 && range == null)
            {
                // A single-range 206 must carry Content-Range (RFC 9110 15.3.7): without it there is no telling where
                // the body starts, so appending could corrupt the file.
                Discard(partial, destination);
                throw new HttpDownloadException($"Server answered the resume of {url} with 206 but no Content-Range; the partial file was deleted, so the next download starts over.");
            }
            long? end = null;   // where this answer's bytes end (a 206's range may stop short of the file's end)
            if (status == 206 && range != null && range.From.HasValue && range.To.HasValue && range.Length is null
                && (range.From == existing || range.From == 0))
            {
                // "bytes 0-99/*" was taken as the whole file: with no total there is no telling whether more follows. A
                // kept partial file made every later call ask for the same range and fail the same way.
                if (existing > 0)
                {
                    Discard(partial, destination);
                    throw new HttpDownloadException($"Server answered {url} with the range {range} but not the file's size, so the download cannot be known to be complete. The partial file was deleted, so the next download starts over.");
                }
                throw new HttpDownloadException($"Server answered {url} with the range {range} but not the file's size, so the download cannot be known to be complete.");
            }
            if (status == 206 && existing > 0 && range!.From == existing && range.To.HasValue)
            {
                resumed = true;
                total = range.Length;
                end = range.To.Value + 1;
                TakeDestination();
            }
            else if (status == 206 && range != null && range.From == 0 && range.To.HasValue)
            {
                existing = 0; resumed = false;   // the server sent the file from its start as one range
                total = range.Length;
                end = range.To.Value + 1;
            }
            // 204 and 205 carry no representation, not an empty one: taken as the file, they replaced it with nothing.
            else if (status >= 200 && status < 300 && status != 206 && status != 204 && status != 205)
            {
                existing = 0; resumed = false;   // the server ignored the range (or there was none)
                total = contentLength;
                end = total;
            }
            else if (status == 206)
            {
                string from = range?.From?.ToString(CultureInfo.InvariantCulture) ?? "(none)";
                if (existing > 0)
                {
                    // Kept, the partial made every later call ask for the same range and fail the same way.
                    Discard(partial, destination);
                    throw new HttpDownloadException($"Server returned a range starting at byte {from} for {url}; byte {existing} was requested. The partial file was deleted, so the next download starts over.");
                }
                // Without a range asked for, the message read "starting at byte  for".
                throw new HttpDownloadException(range is null
                    ? $"Server answered {url} with 206 but no Content-Range, though no range was asked for."
                    : $"Server returned a range starting at byte {from} for {url}; byte 0 was requested.");
            }
            else if (status == 416 && existing > 0)
            {
                Discard(partial, destination);
                throw new HttpDownloadException($"Server rejected the resume range for {url} (416); the partial file was deleted, so the next download starts over.");
            }
            else if (status >= 500)
            {
                response.EnsureSuccessStatusCode();   // 5xx -> transient HttpRequestException
                throw new InvalidOperationException("unreachable");
            }
            else
                throw new HttpDownloadException($"Server returned {status} ({response.ReasonPhrase}) for {url}.");

            // A range that stops short of the end was retried whole as a truncated body (and failed every attempt the
            // same way): under Resume the rest is asked for next; without it nothing may be asked for by range.
            bool shortRange = end.HasValue && total.HasValue && end.Value < total.Value;
            if (shortRange && !options.Resume)
                throw new HttpDownloadException($"Server sent only the bytes {range!.From}-{range.To} of {total} for {url}; fetching the rest needs Resume.");
            // A short range from byte 0 rewrites the file: one that ends no further on than the file already did (bytes
            // 0-5 of 20, every time) was retried without counting as an attempt, forever, and it truncated the partial
            // file to its own length before failing, so the bytes past it were lost. It fails before anything is written.
            if (shortRange && end!.Value <= had)
                throw new HttpDownloadException($"Server sent the bytes {range!.From}-{range.To} of {total} for {url}, which adds nothing to the {had} bytes already downloaded.");
            if (options.MaxBytes is long max && (total > max || existing > max))
            {
                Discard(partial, destination);
                throw new HttpDownloadException($"The file at {url} is {total ?? existing} bytes, more than MaxBytes ({max}); the partial file was deleted.");
            }

            long downloaded = existing;
            bool overrun = false, tooBig = false;
            Progress(options, downloaded, total);

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
                    using var output = Local(() =>
                    {
                        string? directory = Path.GetDirectoryName(destination);
                        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                        return new FileStream(partial, existing > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None);
                    });
                    var buffer = new byte[81920];
                    // CancelAfter takes at most int.MaxValue ms on .NET Framework (about 24.8 days); longer waits forever.
                    // It is rounded up to whole milliseconds: CancelAfter truncates, so a sub-millisecond one timed out at once.
                    double readMs = options.ReadTimeout.TotalMilliseconds;
                    TimeSpan readTimeout = readMs > int.MaxValue || options.ReadTimeout == Timeout.InfiniteTimeSpan
                        ? Timeout.InfiniteTimeSpan : TimeSpan.FromMilliseconds(Math.Ceiling(readMs));
                    while (true)
                    {
                        idle.CancelAfter(readTimeout);
                        int read = await input.ReadAsync(buffer, 0, buffer.Length, idle.Token).ConfigureAwait(false);
                        // Only the wait for the server is timed: a slow disk or progress callback is not a stalled body.
                        idle.CancelAfter(Timeout.InfiniteTimeSpan);
                        if (read == 0) break;
                        // A body longer than its declared total was written to disk without limit, then retried (and kept
                        // under Resume); nothing past the total is written.
                        if (end.HasValue && read > end.Value - downloaded) { overrun = true; break; }
                        if (options.MaxBytes is long most && read > most - downloaded) { tooBig = true; break; }
                        try { await output.WriteAsync(buffer, 0, read, ct).ConfigureAwait(false); }
                        catch (IOException ex) { throw new LocalFailure(ex); }
                        downloaded += read;
                        Progress(options, downloaded, total);
                    }
                }
                catch (Exception ex) when (idle.IsCancellationRequested && !ct.IsCancellationRequested)
                {
                    throw new TimeoutException($"No data from {url} for {options.ReadTimeout}.", ex);
                }
                // The disposed response fails the pending read with an IOException where the stream ignores the token
                // (.NET Framework): the caller cancelled, so say so. A read cancelled through the idle token carries that
                // token, not the caller's, so it is re-raised with theirs too.
                catch (Exception ex) when (ct.IsCancellationRequested && !(ex is ProgressFailure) && !(ex is LocalFailure)
                                           && !(ex is OperationCanceledException oce && oce.CancellationToken == ct))
                {
                    throw new OperationCanceledException("The download was canceled.", ex, ct);
                }
            }

            if (overrun)
            {
                Discard(partial, destination);   // what is there may not be the start of this file either
                throw new HttpDownloadException($"The response from {url} was longer than its declared {end!.Value - existing} bytes; the partial file was deleted.");
            }
            if (tooBig)
            {
                Discard(partial, destination);
                throw new HttpDownloadException($"The response from {url} ran past MaxBytes ({options.MaxBytes}); the partial file was deleted.");
            }
            if (end.HasValue && downloaded != end.Value)
                throw new IOException($"The response from {url} ended after {downloaded} of {end.Value} bytes.");
            if (shortRange) throw new MoreToFetch();
            return Local(() => Finish(destination, partial, options, downloaded, total ?? -1, resumed));
        }

        private static bool IsHex(string s)
        {
            foreach (char c in s)
                if (!(c >= '0' && c <= '9' || c >= 'a' && c <= 'f' || c >= 'A' && c <= 'F')) return false;
            return true;
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
                    Discard(partial, destination);   // resuming would only extend the wrong bytes
                    throw new HttpDownloadException($"Checksum mismatch for {destination}: expected {options.ExpectedSha256Hex}, got {sha}.");
                }
            }
            if (File.Exists(destination))
            {
                // One step that keeps the old file's ACL and attributes; deleting first could lose both files if the
                // move then failed, and a move gives the file the partial's inherited permissions.
                try { File.Replace(partial, destination, null); }
                catch (FileNotFoundException) when (!File.Exists(destination)) { File.Move(partial, destination); }   // deleted meanwhile
            }
            else File.Move(partial, destination);
            return new HttpDownloadResult(downloaded, total, resumed, sha);
        }

        // Drops a partial file that cannot be resumed. Under Resume a missing partial file is copied again from an existing
        // destination, so deleting it there made every later call resume the same stale destination and fail the same
        // way, though the message said the next download starts over: an empty partial file starts it over instead.
        private static void Discard(string partial, string destination)
        {
            if (!File.Exists(destination)) { TryDelete(partial); return; }
            try { using (new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None)) { } }
            catch (IOException) { TryDelete(partial); }
            catch (UnauthorizedAccessException) { TryDelete(partial); }
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
                if (e is HttpDownloadException || e is ProgressFailure || e is LocalFailure) return false;
                // With the caller's token untouched, a cancellation is the HttpClient's own Timeout firing.
                if (e is OperationCanceledException) return true;
                if (e is HttpRequestException || e is IOException || e is TimeoutException) return true;
                if (e.GetType().FullName == "System.Net.Sockets.SocketException") return true;
            }
            return false;
        }
    }
}
