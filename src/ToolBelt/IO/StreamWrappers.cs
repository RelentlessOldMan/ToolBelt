// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ToolBelt.IO
{
    /// <summary>
    /// A pass-through stream that counts the bytes read and written — for progress, throughput and "how big was that
    /// upload" without buffering. Seeking is passed through and doesn't affect the counts. Disposing disposes the inner
    /// stream unless <c>leaveOpen</c>.
    /// </summary>
    public sealed class CountingStream : Stream
    {
        private readonly Stream _inner;
        private readonly bool _leaveOpen;
        private long _read, _written;

        public CountingStream(Stream inner, bool leaveOpen = false)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _leaveOpen = leaveOpen;
        }

        public long BytesRead => Interlocked.Read(ref _read);
        public long BytesWritten => Interlocked.Read(ref _written);

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => _inner.CanSeek;
        public override bool CanWrite => _inner.CanWrite;
        public override long Length => _inner.Length;
        public override long Position { get => _inner.Position; set => _inner.Position = value; }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int n = _inner.Read(buffer, offset, count);
            Interlocked.Add(ref _read, n);
            return n;
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            int n = await _inner.ReadAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
            Interlocked.Add(ref _read, n);
            return n;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            _inner.Write(buffer, offset, count);
            Interlocked.Add(ref _written, count);
        }

        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            await _inner.WriteAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
            Interlocked.Add(ref _written, count);
        }

        public override void Flush() => _inner.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken) => _inner.FlushAsync(cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
        public override void SetLength(long value) => _inner.SetLength(value);

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_leaveOpen) _inner.Dispose();
            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// Duplicates a stream: everything read from (or written to) the primary stream is also written to a secondary one —
    /// hash or archive a download while saving it, log a protocol exchange, keep a raw copy of parsed data. Read-only
    /// wrapping copies what was actually read; write wrapping writes to both. Not seekable. Disposing disposes both
    /// streams unless told to leave them open.
    /// </summary>
    public sealed class TeeStream : Stream
    {
        private readonly Stream _primary, _copy;
        private readonly bool _leavePrimaryOpen, _leaveCopyOpen;

        public TeeStream(Stream primary, Stream copy, bool leavePrimaryOpen = false, bool leaveCopyOpen = false)
        {
            _primary = primary ?? throw new ArgumentNullException(nameof(primary));
            _copy = copy ?? throw new ArgumentNullException(nameof(copy));
            if (!copy.CanWrite) throw new ArgumentException("The copy stream must be writable.", nameof(copy));
            _leavePrimaryOpen = leavePrimaryOpen;
            _leaveCopyOpen = leaveCopyOpen;
        }

        public override bool CanRead => _primary.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => _primary.CanWrite;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int n = _primary.Read(buffer, offset, count);
            if (n > 0) _copy.Write(buffer, offset, n);
            return n;
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            int n = await _primary.ReadAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
            if (n > 0) await _copy.WriteAsync(buffer, offset, n, cancellationToken).ConfigureAwait(false);
            return n;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            _primary.Write(buffer, offset, count);
            _copy.Write(buffer, offset, count);
        }

        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            await _primary.WriteAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
            await _copy.WriteAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
        }

        public override void Flush()
        {
            _primary.Flush();
            _copy.Flush();
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try { _copy.Flush(); } catch (ObjectDisposedException) { }
                if (!_leavePrimaryOpen) _primary.Dispose();
                if (!_leaveCopyOpen) _copy.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
