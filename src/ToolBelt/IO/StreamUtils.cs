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
            int read;
            while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                destination.Write(buffer, 0, read);
                total += read;
                progress?.Invoke(total);
            }
            return total;
        }

        /// <summary>Reads exactly <paramref name="count"/> bytes or throws <see cref="EndOfStreamException"/>.</summary>
        public static byte[] ReadExactly(Stream source, int count)
        {
            if (source is null) throw new ArgumentNullException(nameof(source));
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count), count, "Count must be non-negative.");

            var buffer = new byte[count];
            int offset = 0;
            while (offset < count)
            {
                int read = source.Read(buffer, offset, count - offset);
                if (read == 0)
                    throw new EndOfStreamException($"Expected {count} bytes but the stream ended after {offset}.");
                offset += read;
            }
            return buffer;
        }
    }
}
