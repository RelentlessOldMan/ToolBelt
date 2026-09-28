// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.IO;
using System.Text;

namespace ToolBelt.Logging
{
    /// <summary>
    /// Writes formatted log lines to a file, with optional size-based rotation and retention. When the file
    /// reaches <c>maxBytes</c> it rolls to <c>path.1</c>, <c>path.2</c>, … keeping at most <c>maxFiles</c>
    /// backups; older ones are pruned. Writes are serialized and flushed per line so events are not lost on a
    /// crash. Thread-safe.
    /// </summary>
    public sealed class FileSink : ILogSink
    {
        private readonly string _path;
        private readonly Func<LogEvent, string> _formatter;
        private readonly long _maxBytes;
        private readonly int _maxFiles;
        private readonly Encoding _encoding;
        private readonly object _gate = new object();
        private FileStream _stream = null!;
        private StreamWriter _writer = null!;

        /// <summary>
        /// Creates a file sink. <paramref name="maxBytes"/> of 0 disables rotation; otherwise the file rolls
        /// at that size keeping <paramref name="maxFiles"/> backups.
        /// </summary>
        public FileSink(string path, Func<LogEvent, string>? formatter = null, long maxBytes = 0, int maxFiles = 5, Encoding? encoding = null)
        {
            _path = path ?? throw new ArgumentNullException(nameof(path));
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
                _writer.WriteLine(line);
                _writer.Flush();
                if (_maxBytes > 0 && _stream.Length >= _maxBytes) Rotate();
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                try { _writer.Flush(); } catch { /* best-effort */ }
                _writer.Dispose(); // also disposes the underlying stream
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
            _writer.Flush();
            _writer.Dispose();

            string Rotated(int n) => _path + "." + n;
            if (File.Exists(Rotated(_maxFiles))) File.Delete(Rotated(_maxFiles));
            for (int k = _maxFiles - 1; k >= 1; k--)
                if (File.Exists(Rotated(k))) File.Move(Rotated(k), Rotated(k + 1));
            if (File.Exists(_path)) File.Move(_path, Rotated(1));

            Open(); // fresh, empty primary file
        }
    }
}
