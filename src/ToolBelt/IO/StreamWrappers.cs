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
    /// stream unless <c>leaveOpen</c>; either way the wrapper itself is then closed (its operations throw
    /// <see cref="ObjectDisposedException"/>, its <c>Can*</c> properties are false), and the counts stay readable.
    /// </summary>
    public sealed class CountingStream : Stream
    {
        private readonly Stream _inner;
        private readonly bool _leaveOpen;
        private long _read, _written;
        private volatile bool _disposed;   // with leaveOpen, the wrapper kept reading, writing and counting after Dispose

        public CountingStream(Stream inner, bool leaveOpen = false)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _leaveOpen = leaveOpen;
        }

        public long BytesRead => Interlocked.Read(ref _read);
        public long BytesWritten => Interlocked.Read(ref _written);

        public override bool CanRead => !_disposed && _inner.CanRead;
        public override bool CanSeek => !_disposed && _inner.CanSeek;
        public override bool CanWrite => !_disposed && _inner.CanWrite;
        public override long Length => Inner.Length;
        public override long Position { get => Inner.Position; set => Inner.Position = value; }

        private Stream Inner => _disposed ? throw new ObjectDisposedException(nameof(CountingStream)) : _inner;

        public override int Read(byte[] buffer, int offset, int count)
        {
            int n = Inner.Read(buffer, offset, count);
            Interlocked.Add(ref _read, n);
            return n;
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            int n = await Inner.ReadAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
            Interlocked.Add(ref _read, n);
            return n;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            Inner.Write(buffer, offset, count);
            Interlocked.Add(ref _written, count);
        }

        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            await Inner.WriteAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
            Interlocked.Add(ref _written, count);
        }

        public override void Flush() => Inner.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken) => Inner.FlushAsync(cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => Inner.Seek(offset, origin);
        public override void SetLength(long value) => Inner.SetLength(value);

        protected override void Dispose(bool disposing)
        {
            _disposed = true;
            if (disposing && !_leaveOpen) _inner.Dispose();
            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// Duplicates a stream: everything read from (or written to) the primary stream is also written to a secondary one —
    /// hash or archive a download while saving it, log a protocol exchange, keep a raw copy of parsed data. Read-only
    /// wrapping copies what was actually read; write wrapping writes to both. Not seekable. Disposing disposes both
    /// streams unless told to leave them open; either way the wrapper itself is then closed (its operations throw
    /// <see cref="ObjectDisposedException"/>, its <c>Can*</c> properties are false).
    /// </summary>
    public sealed class TeeStream : Stream
    {
        private readonly Stream _primary, _copy;
        private readonly bool _leavePrimaryOpen, _leaveCopyOpen;
        private volatile bool _disposed;   // with both left open, the wrapper kept teeing after Dispose

        public TeeStream(Stream primary, Stream copy, bool leavePrimaryOpen = false, bool leaveCopyOpen = false)
        {
            _primary = primary ?? throw new ArgumentNullException(nameof(primary));
            _copy = copy ?? throw new ArgumentNullException(nameof(copy));
            if (!copy.CanWrite) throw new ArgumentException("The copy stream must be writable.", nameof(copy));
            _leavePrimaryOpen = leavePrimaryOpen;
            _leaveCopyOpen = leaveCopyOpen;
        }

        public override bool CanRead => !_disposed && _primary.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => !_disposed && _primary.CanWrite;

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(TeeStream));
        }
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            ThrowIfDisposed();
            int n = _primary.Read(buffer, offset, count);
            if (n > 0) _copy.Write(buffer, offset, n);
            return n;
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            int n = await _primary.ReadAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
            if (n > 0) await _copy.WriteAsync(buffer, offset, n, cancellationToken).ConfigureAwait(false);
            return n;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            ThrowIfDisposed();
            _primary.Write(buffer, offset, count);
            _copy.Write(buffer, offset, count);
        }

        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            await _primary.WriteAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
            await _copy.WriteAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
        }

        public override void Flush()
        {
            ThrowIfDisposed();
            _primary.Flush();
            _copy.Flush();
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_disposed) return;   // a second Dispose flushed (and disposed) again
                _disposed = true;
                try
                {
                    try { try { _copy.Flush(); } catch (ObjectDisposedException) { } }
                    finally { if (!_leavePrimaryOpen) _primary.Dispose(); }   // even if the copy's flush threw
                }
                finally
                {
                    try { if (!_leaveCopyOpen) _copy.Dispose(); }   // even if the primary threw
                    finally { base.Dispose(disposing); }
                }
                return;
            }
            base.Dispose(disposing);
        }
    }
}
