// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ToolBelt.Net
{
    /// <summary>
    /// A thin, line-oriented TCP client with connect/read/write timeouts — the shape most instrument and
    /// service control interfaces take. Connects on construction; send a line and read the response line.
    /// This is a small tested wrapper, not a protocol framework. Not thread-safe. Dispose closes the socket.
    /// <para>
    /// A read that times out throws <see cref="IOException"/> but loses nothing: the part of a line already received is
    /// kept, and the next <see cref="ReadLine"/> continues it. Lines end at LF, CR or CR LF, as with
    /// <see cref="StreamReader.ReadLine"/>. No byte order mark is sent, and one at the start of the peer's data is dropped.
    /// </para>
    /// </summary>
    public sealed class TcpLineClient : IDisposable
    {
        private readonly TcpClient _client;
        private readonly NetworkStream _stream;
        private readonly Decoder _decoder;
        private readonly Encoding _encoding;
        private readonly byte[] _bytes = new byte[4096];
        private readonly char[] _chars;
        private readonly StringBuilder _line = new StringBuilder();
        private int _charPos, _charLen;   // decoded but not yet consumed: _chars[_charPos.._charLen)
        private bool _skipLf;             // the last line ended at CR; drop an LF that follows it
        private bool _eof;
        private bool _discarding;         // skipping the rest of a line that exceeded MaxLineLength
        private bool _atStart = true;     // nothing decoded yet: a byte order mark from the peer is dropped
        private int _bytePos, _byteLen;   // read but not yet decoded, after a decoding error: _bytes[_bytePos.._byteLen)
        private Exception? _decodeError;  // raised once the text decoded before the bad bytes has been read

        /// <param name="host">The host to connect to.</param>
        /// <param name="port">The TCP port, 0-65535.</param>
        /// <param name="connectTimeout">How long to wait for the connection; default 30 s.</param>
        /// <param name="readTimeout">How long a read may wait for data; null waits indefinitely.</param>
        /// <param name="writeTimeout">How long a write may block; null waits indefinitely.</param>
        /// <param name="encoding">The text encoding; default UTF-8 without a byte order mark.</param>
        public TcpLineClient(string host, int port,
            TimeSpan? connectTimeout = null, TimeSpan? readTimeout = null, TimeSpan? writeTimeout = null, Encoding? encoding = null)
        {
            if (host is null) throw new ArgumentNullException(nameof(host));
            if (host.Length == 0) throw new ArgumentException("Host must not be empty.", nameof(host));
            if (port < IPEndPoint.MinPort || port > IPEndPoint.MaxPort)
                throw new ArgumentOutOfRangeException(nameof(port), port, "Port must be in [0, 65535].");
            int connectMs = ToMilliseconds(connectTimeout ?? TimeSpan.FromSeconds(30), nameof(connectTimeout));
            int readMs = readTimeout.HasValue ? ToMilliseconds(readTimeout.Value, nameof(readTimeout)) : Timeout.Infinite;
            int writeMs = writeTimeout.HasValue ? ToMilliseconds(writeTimeout.Value, nameof(writeTimeout)) : Timeout.Infinite;
            encoding ??= new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

            _client = NewClient();
            Task connect;
            try { connect = _client.ConnectAsync(host, port); }   // may throw before it returns a task
            catch
            {
                _client.Dispose();
                throw;
            }
            // If we give up on the connect, its eventual failure must not surface as an unobserved task exception.
            connect.ContinueWith(t => _ = t.Exception, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            try
            {
                if (!connect.Wait(connectMs))
                {
                    _client.Dispose();
                    throw new TimeoutException($"Connection to {host}:{port} timed out.");
                }
            }
            catch (AggregateException ex)
            {
                _client.Dispose();
                throw ex.GetBaseException();
            }

            _stream = _client.GetStream();
            _stream.ReadTimeout = readMs;
            _stream.WriteTimeout = writeMs;
            _decoder = encoding.GetDecoder();
            _chars = new char[encoding.GetMaxCharCount(_bytes.Length)];
            _encoding = encoding;
        }

        /// <summary>
        /// The longest line (in characters, excluding the terminator) <see cref="ReadLine"/> accepts before throwing
        /// <see cref="InvalidDataException"/>, so a peer that never sends a newline cannot exhaust memory. Default 1 Mi.
        /// </summary>
        public int MaxLineLength
        {
            get => _maxLineLength;
            set
            {
                if (value < 1) throw new ArgumentOutOfRangeException(nameof(value), value, "MaxLineLength must be positive.");
                _maxLineLength = value;
            }
        }
        private int _maxLineLength = 1024 * 1024;

        /// <summary>Sends a line (terminated with LF) and returns the next response line, or null at end of stream.</summary>
        public string? SendReceive(string line)
        {
            Send(line);
            return ReadLine();
        }

        /// <summary>Sends a line terminated with LF. The line itself must not contain CR or LF.</summary>
        public void Send(string line)
        {
            if (line is null) throw new ArgumentNullException(nameof(line));
            if (line.IndexOfAny(new[] { '\r', '\n' }) >= 0)
                throw new ArgumentException("The line must not contain CR or LF; it would be sent as more than one line.", nameof(line));
            // Encoded directly: a StreamWriter would send the encoding's byte order mark before the first line.
            byte[] bytes = _encoding.GetBytes(line + "\n");
            _stream.Write(bytes, 0, bytes.Length);
        }

        /// <summary>
        /// The next line (without its terminator), or null at end of stream. Throws <see cref="IOException"/> if the read
        /// timeout expires (the partial line is kept for the next call) and <see cref="InvalidDataException"/> if the
        /// line exceeds <see cref="MaxLineLength"/> (that line is dropped up to its terminator; the next call returns the
        /// line after it). With an encoding that throws on invalid bytes, the line holding them throws
        /// <see cref="DecoderFallbackException"/> and is dropped the same way; the lines around it are kept.
        /// </summary>
        public string? ReadLine()
        {
            while (true)
            {
                while (_charPos < _charLen)
                {
                    char c = _chars[_charPos++];
                    if (_atStart)
                    {
                        _atStart = false;
                        if (c == '\uFEFF') continue;
                    }
                    if (_skipLf)
                    {
                        _skipLf = false;
                        if (c == '\n') continue;
                    }
                    if (c == '\n' || c == '\r')
                    {
                        _skipLf = c == '\r';
                        if (_discarding)
                        {
                            _discarding = false;   // the end of the overlong line: carry on with the next one
                            continue;
                        }
                        string result = _line.ToString();
                        _line.Clear();
                        return result;
                    }
                    if (_discarding) continue;
                    if (_line.Length >= _maxLineLength)
                    {
                        // Drop only this line: the rest of it is skipped up to its terminator, and later lines are kept.
                        _line.Clear();
                        _discarding = true;
                        throw new InvalidDataException($"A line longer than {_maxLineLength} characters was received.");
                    }
                    _line.Append(c);
                }

                if (_decodeError != null)
                {
                    Exception error = _decodeError;
                    _decodeError = null;
                    _line.Clear();
                    _discarding = !_eof;   // skip the rest of the bad line, as for an overlong one
                    throw error;
                }
                if (_eof) break;
                int start = 0, count;
                if (_bytePos < _byteLen)
                {
                    start = _bytePos;
                    count = _byteLen - _bytePos;
                    _bytePos = _byteLen = 0;
                }
                else count = _stream.Read(_bytes, 0, _bytes.Length);   // throws IOException on timeout; state is kept
                Decode(start, count);
            }

            if (_line.Length == 0) return null;
            string last = _line.ToString();
            _line.Clear();
            return last;
        }

        // Decodes _bytes[start, start + count) into _chars; a count of 0 is the end of the stream. When the encoding throws,
        // the bytes are decoded again one at a time to find the bad ones: the text before them is kept for ReadLine, the
        // error waits until that text is used, and the bytes after them are decoded on the next pass.
        private void Decode(int start, int count)
        {
            _charPos = 0;
            _charLen = 0;
            try
            {
                _charLen = _decoder.GetChars(_bytes, start, count, _chars, 0, flush: count == 0);
                if (count == 0) _eof = true;
                return;
            }
            catch (DecoderFallbackException ex)
            {
                _decoder.Reset();
                _decodeError = ex;
                if (count == 0) { _eof = true; return; }   // the stream ended inside a character
            }
            for (int i = start; i < start + count; i++)
            {
                try { _charLen += _decoder.GetChars(_bytes, i, 1, _chars, _charLen, flush: false); }
                catch (DecoderFallbackException ex)
                {
                    _decoder.Reset();
                    _decodeError = ex;
                    // A byte that exposed an unfinished character can itself be text (an ASCII letter, a line break), so
                    // it is decoded again; the first byte tried, with a fresh decoder, is bad on its own and is skipped.
                    _bytePos = i == start || _bytes[i] >= 0x80 ? i + 1 : i;
                    _byteLen = start + count;
                    return;
                }
            }
            // Each byte decoded alone: the bad bytes were the end of the previous read, so the error comes first.
            _charLen = 0;
            _bytePos = start;
            _byteLen = start + count;
        }

        public void Dispose()
        {
            _client.Dispose();
        }

        // Positive timeouts only; anything past int.MaxValue ms waits indefinitely.
        // On .NET Framework a plain TcpClient is IPv4-only (connecting to "::1" throws); a dual-mode IPv6 socket reaches
        // both families, as the default one does on .NET Core.
        private static TcpClient NewClient()
            => Socket.OSSupportsIPv6 ? new TcpClient(AddressFamily.InterNetworkV6) { Client = { DualMode = true } } : new TcpClient();

        private static int ToMilliseconds(TimeSpan timeout, string paramName)
        {
            if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(paramName, timeout, "Timeout must be positive.");
            double ms = Math.Ceiling(timeout.TotalMilliseconds);
            return ms >= int.MaxValue ? Timeout.Infinite : (int)ms;
        }
    }
}
