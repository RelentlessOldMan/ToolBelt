// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.IO;
using System.Threading;

namespace ToolBelt.IO
{
    /// <summary>
    /// Stream helpers: copy with a progress callback and cancellation, and a read-exactly that fills a
    /// buffer or throws (the older target lacks a built-in equivalent).
    /// </summary>
    public static class StreamUtils
    {
        /// <summary>Copies <paramref name="source"/> to <paramref name="destination"/>, reporting cumulative bytes copied.</summary>
        public static long CopyWithProgress(
            Stream source, Stream destination,
            Action<long>? progress = null, int bufferSize = 81920, CancellationToken cancellationToken = default)
        {
            if (source is null) throw new ArgumentNullException(nameof(source));
            if (destination is null) throw new ArgumentNullException(nameof(destination));
            if (bufferSize < 1) throw new ArgumentOutOfRangeException(nameof(bufferSize), bufferSize, "Buffer size must be positive.");

            var buffer = new byte[bufferSize];
            long total = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();   // before the read, so a cancelled copy consumes nothing more
                int read = source.Read(buffer, 0, buffer.Length);
                if (read <= 0) break;
                destination.Write(buffer, 0, read);
                total += read;
                progress?.Invoke(total);
            }
            return total;
        }

        /// <summary>
        /// Reads exactly <paramref name="count"/> bytes (0 to 0x7FFFFFC7, the largest byte array) or throws
        /// <see cref="EndOfStreamException"/>. Memory grows with what the stream holds, not with the count asked for.
        /// </summary>
        public static byte[] ReadExactly(Stream source, int count)
        {
            if (source is null) throw new ArgumentNullException(nameof(source));
            if (count < 0 || count > MaxByteArray) throw new ArgumentOutOfRangeException(nameof(count), count, "Count must be from 0 to 0x7FFFFFC7.");

            // A count read from a header (a length prefix, say) was allocated in full before reading: a 4-byte stream
            // saying "2 GB" cost 2 GB, or OutOfMemoryException, before the EndOfStreamException it deserved.
            var buffer = new byte[Math.Min(count, InitialChunk)];
            int offset = 0;
            while (offset < count)
            {
                if (offset == buffer.Length)
                    Array.Resize(ref buffer, (int)Math.Min(count, Math.Max(2L * buffer.Length, InitialChunk)));
                int read = source.Read(buffer, offset, buffer.Length - offset);
                if (read == 0)
                    throw new EndOfStreamException($"Expected {count} bytes but the stream ended after {offset}.");
                offset += read;
            }
            return buffer;
        }

        private const int MaxByteArray = 0x7FFFFFC7, InitialChunk = 1 << 20;
    }
}
