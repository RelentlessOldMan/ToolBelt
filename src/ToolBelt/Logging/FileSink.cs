// ToolBelt drop-in — also copy Logging/Log.cs and Logging/LogFormatters.cs.
using System;
using System.IO;
using System.Text;

namespace ToolBelt.Logging
{
    /// <summary>
    /// Writes formatted log lines to a file, with optional size-based rotation and retention. When the file
    /// reaches <c>maxBytes</c> it rolls to <c>path.1</c>, <c>path.2</c>, … keeping at most <c>maxFiles</c>
    /// backups; older ones are pruned. Writes are serialized and flushed per line so events are not lost on a
    /// crash. If a rotation fails (another process holds a backup or the file without delete sharing), the sink keeps
    /// appending to the current file and tries again on the next write. Thread-safe.
    /// </summary>
    public sealed class FileSink : ILogSink
    {
        private readonly string _path;
        private readonly Func<LogEvent, string> _formatter;
        private readonly long _maxBytes;
        private readonly int _maxFiles;
        private readonly Encoding _encoding;
        private readonly object _gate = new object();
        private FileStream? _stream;
        private StreamWriter? _writer;
        private bool _disposed;

        /// <summary>
        /// Creates a file sink. <paramref name="maxBytes"/> of 0 disables rotation; otherwise the file rolls
        /// at that size keeping <paramref name="maxFiles"/> backups.
        /// </summary>
        public FileSink(string path, Func<LogEvent, string>? formatter = null, long maxBytes = 0, int maxFiles = 5, Encoding? encoding = null)
        {
            if (path is null) throw new ArgumentNullException(nameof(path));
            // Resolved once: a relative path re-resolved on every reopen moved the log (and lost its rotation) when the
            // current directory changed.
            _path = Path.GetFullPath(path);
            if (maxBytes < 0) throw new ArgumentOutOfRangeException(nameof(maxBytes), maxBytes, "maxBytes must be non-negative.");
            if (maxBytes > 0 && maxFiles < 1) throw new ArgumentOutOfRangeException(nameof(maxFiles), maxFiles, "maxFiles must be at least 1 when rotation is enabled.");
            _formatter = formatter ?? LogFormatters.Plain;
            _maxBytes = maxBytes;
            _maxFiles = maxFiles;
            _encoding = encoding ?? new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            Open();
        }

        public void Emit(LogEvent logEvent)
        {
            string line = _formatter(logEvent);
            lock (_gate)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(FileSink));
                if (_writer == null) Open(); // an earlier reopen failed; try again
                _writer!.WriteLine(line);
                _writer.Flush();
                if (_maxBytes > 0 && _stream!.Length >= _maxBytes) Rotate();
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                if (_writer == null) return;
                try { _writer.Flush(); } catch { /* best-effort */ }
                _writer.Dispose(); // also disposes the underlying stream
                _writer = null;
                _stream = null;
            }
        }

        private void Open()
        {
            string? dir = Path.GetDirectoryName(Path.GetFullPath(_path));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            _stream = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.Read);
            _writer = new StreamWriter(_stream, _encoding);
        }

        private void Rotate()
        {
            _writer!.Flush();
            _writer.Dispose();
            _writer = null;
            _stream = null;

            string Rotated(int n) => _path + "." + n;
            try
            {
                // Shift only the run of backups up to the first gap: probing every index up to maxFiles hung a sink
                // created with a large maxFiles.
                int last = 0;
                while (last < _maxFiles && File.Exists(Rotated(last + 1))) last++;
                if (last == _maxFiles) File.Delete(Rotated(last--));
                for (int k = last; k >= 1; k--) File.Move(Rotated(k), Rotated(k + 1));
                if (File.Exists(_path)) File.Move(_path, Rotated(1));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // Someone holds a file open without delete sharing (a log viewer, a backup tool). Keep appending to
                // the current file; the next write past the limit tries again.
            }

            Open(); // a fresh primary file, or the current one again if the rotation failed
        }
    }
}
