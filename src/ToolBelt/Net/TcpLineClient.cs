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
        private readonly Encoding _sending;   // the encoding, throwing where a character cannot be encoded
        private readonly byte[] _bytes = new byte[4096];
        private readonly char[] _chars;
        private readonly StringBuilder _line = new StringBuilder();
        private int _charPos, _charLen;   // decoded but not yet consumed: _chars[_charPos.._charLen)
        private bool _skipLf;             // the last line ended at CR; drop an LF that follows it
        private bool _eof;
        private bool _discarding;         // skipping the rest of a line that exceeded MaxLineLength
        private bool _atStart = true;     // nothing decoded yet: a byte order mark from the peer is dropped
        private readonly CountingFallback? _bad;   // the encoding throws on bad bytes: a copy counts them instead
        private int[] _badBefore = new int[0];    // bad byte runs just before _chars[i] (or at the end, i == _charLen)
        private bool _lineBad;                    // the line so far held bad bytes: it throws, once, at its end
        private long _lineBadRuns;                // those runs, each counted as one character toward MaxLineLength
        private long _skipped;                    // characters skipped of an overlong line since it last threw

        /// <param name="host">The host to connect to.</param>
        /// <param name="port">The TCP port, 1-65535.</param>
        /// <param name="connectTimeout">How long to wait for the connection; default 30 s.</param>
        /// <param name="readTimeout">How long a read may wait for data; null waits indefinitely.</param>
        /// <param name="writeTimeout">How long a write may block; null waits indefinitely.</param>
        /// <param name="encoding">The text encoding; default UTF-8 without a byte order mark.</param>
        public TcpLineClient(string host, int port,
            TimeSpan? connectTimeout = null, TimeSpan? readTimeout = null, TimeSpan? writeTimeout = null, Encoding? encoding = null)
        {
            if (host is null) throw new ArgumentNullException(nameof(host));
            // A blank host went to DNS and failed as SocketException "No such host is known" (PortCheck and HostInfo
            // refuse it).
            if (host.Trim().Length == 0) throw new ArgumentException("Host must not be empty or blank.", nameof(host));
            // Port 0 was accepted, and failed later as a SocketException: no connection can be made to it.
            if (port < 1 || port > IPEndPoint.MaxPort)
                throw new ArgumentOutOfRangeException(nameof(port), port, "Port must be in [1, 65535].");
            int connectMs = ToMilliseconds(connectTimeout ?? TimeSpan.FromSeconds(30), nameof(connectTimeout));
            int readMs = readTimeout.HasValue ? ToMilliseconds(readTimeout.Value, nameof(readTimeout)) : Timeout.Infinite;
            int writeMs = writeTimeout.HasValue ? ToMilliseconds(writeTimeout.Value, nameof(writeTimeout)) : Timeout.Infinite;
            encoding ??= new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

            _client = NewClient();
            Task connect;
            try { connect = _client.ConnectAsync(host, port); }   // may throw before it returns a task
            catch (Exception ex)
            {
                _client.Dispose();
                if (BadHost(ex) is Exception bad) throw bad;
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
                throw BadHost(ex) ?? ex.GetBaseException();
            }

            _stream = _client.GetStream();
            _stream.ReadTimeout = readMs;
            _stream.WriteTimeout = writeMs;
            // A throwing decoder was reset after a bad byte and the bytes were decoded again one at a time, which dropped a
            // character split across reads, assumed UTF-8 when skipping (a lone UTF-16 surrogate misaligned every later
            // line) and threw once per bad byte. A copy of the encoding counts bad bytes instead of throwing and keeps its
            // own carry and alignment; the line holding them throws once, at its end.
            Encoding decoding = encoding;
            if (encoding.DecoderFallback is DecoderExceptionFallback)
            {
                decoding = (Encoding)encoding.Clone();
                decoding.DecoderFallback = _bad = new CountingFallback();
            }
            _decoder = decoding.GetDecoder();
            _chars = new char[decoding.GetMaxCharCount(_bytes.Length)];
            if (_bad != null) _badBefore = new int[_chars.Length + 1];
            _encoding = encoding;
            // A character the encoding cannot represent went out as '?' (ASCII "café" as "caf?"), and the send succeeded.
            _sending = encoding;
            if (!(encoding.EncoderFallback is EncoderExceptionFallback))
            {
                _sending = (Encoding)encoding.Clone();
                _sending.EncoderFallback = EncoderFallback.ExceptionFallback;
            }
        }

        /// <summary>
        /// The longest line (in characters, excluding the terminator) <see cref="ReadLine"/> accepts before throwing
        /// <see cref="InvalidDataException"/>, so a peer that never sends a newline cannot exhaust memory. Default 1 Mi.
        /// With an encoding that throws on invalid bytes, each invalid sequence the decoder reports (for UTF-8 often a single
        /// byte) counts as one character, so a line of nothing but bad bytes is held to the limit too. While the rest of an
        /// overlong line is skipped, each call throws again after another MaxLineLength characters with no terminator,
        /// so a peer that keeps sending cannot hold a call for good.
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
            // A lone surrogate went out as U+FFFD (or '?'), so the peer got other text and the send reported success.
            for (int i = 0; i < line.Length; i++)
            {
                if (char.IsHighSurrogate(line[i]) && i + 1 < line.Length && char.IsLowSurrogate(line[i + 1])) { i++; continue; }
                if (char.IsSurrogate(line[i]))
                    throw new ArgumentException($"The line has a lone surrogate at index {i}; it cannot be encoded.", nameof(line));
            }
            // Encoded directly: a StreamWriter would send the encoding's byte order mark before the first line.
            byte[] bytes;
            try { bytes = _sending.GetBytes(line + "\n"); }
            catch (EncoderFallbackException ex)
            {
                throw new ArgumentException($"The line has a character at index {ex.Index} that {_encoding.WebName} cannot encode.", nameof(line), ex);
            }
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
                    TakeBad();
                    char c = _chars[_charPos++];
                    if (_atStart)
                    {
                        _atStart = false;
                        if (c == '\uFEFF') continue;
                    }
                    if (_skipLf)
                    {
                        _skipLf = false;
                        // Bad bytes between the CR and the LF: the LF ends that bad line (skipped, it marked the next one).
                        if (c == '\n' && !_lineBad) continue;
                    }
                    if (c == '\n' || c == '\r')
                    {
                        _skipLf = c == '\r';
                        if (_discarding)
                        {
                            _discarding = false;   // the end of the overlong line: carry on with the next one
                            _skipped = 0;
                            continue;
                        }
                        if (_lineBad) ThrowBadLine();
                        string result = _line.ToString();
                        _line.Clear();
                        return result;
                    }
                    if (_discarding)
                    {
                        if (++_skipped >= _maxLineLength) StillTooLong();
                        continue;
                    }
                    if (_line.Length + _lineBadRuns >= _maxLineLength) TooLong();
                    _line.Append(c);
                }

                TakeBad();   // bad bytes at the end of what was read
                if (_eof) break;
                int count = _stream.Read(_bytes, 0, _bytes.Length);   // throws IOException on timeout; state is kept
                Decode(count);
            }

            if (_lineBad) ThrowBadLine();
            if (_line.Length == 0) return null;
            string last = _line.ToString();
            _line.Clear();
            return last;
        }

        // Decodes _bytes[0, count) into _chars; a count of 0 is the end of the stream (an unfinished character there is
        // bad). With a counting fallback the bytes go in one at a time, so each bad run is placed before the characters
        // decoded after it.
        private void Decode(int count)
        {
            _charPos = 0;
            _charLen = 0;
            if (_bad is null)
            {
                _charLen = _decoder.GetChars(_bytes, 0, count, _chars, 0, flush: count == 0);
            }
            else if (count == 0)
            {
                int before = _bad.Count;
                _charLen = _decoder.GetChars(_bytes, 0, 0, _chars, 0, flush: true);
                _badBefore[0] += unchecked(_bad.Count - before);
            }
            else
            {
                for (int i = 0; i < count; i++)
                {
                    int before = _bad.Count;
                    int n = _decoder.GetChars(_bytes, i, 1, _chars, _charLen, flush: false);
                    _badBefore[_charLen] += unchecked(_bad.Count - before);
                    _charLen += n;
                }
            }
            if (count == 0) _eof = true;
        }

        // Drops only this line: the rest of it is skipped up to its terminator, and later lines are kept.
        private void TooLong()
        {
            _line.Clear();
            _lineBad = false;   // reported once, as too long
            _lineBadRuns = 0;
            _discarding = true;
            _skipped = 0;
            throw new InvalidDataException($"A line longer than {_maxLineLength} characters was received.");
        }

        // A peer that never ended the overlong line held ReadLine for good: data kept coming, so the read timeout never
        // fired. Each call skips at most another MaxLineLength characters.
        private void StillTooLong()
        {
            _skipped = 0;
            throw new InvalidDataException($"The line longer than {_maxLineLength} characters is still being received; another {_maxLineLength} were skipped.");
        }

        // Marks the line as bad when bad bytes came before the next character (or the end of what was decoded). Each
        // invalid sequence counts as a character: bad bytes made no characters, so a line of them was never held to
        // MaxLineLength. (A whole run counted once would let a peer stream bad bytes forever without a terminator.)
        private void TakeBad()
        {
            if (_bad is null) return;
            int runs = _badBefore[_charPos];
            if (runs == 0) return;
            _badBefore[_charPos] = 0;
            if (_discarding)
            {
                _skipped += runs;
                if (_skipped >= _maxLineLength) StillTooLong();
                return;
            }
            _lineBad = true;
            _lineBadRuns += runs;
            if (_line.Length + _lineBadRuns > _maxLineLength) TooLong();
        }

        private void ThrowBadLine()
        {
            _lineBad = false;
            _lineBadRuns = 0;
            _line.Clear();
            throw new DecoderFallbackException($"A line held bytes that are not valid {_encoding.WebName}; it was dropped.");
        }

        // Counts bad byte runs and yields no characters for them.
        private sealed class CountingFallback : DecoderFallback
        {
            public int Count;

            public override int MaxCharCount => 0;
            public override DecoderFallbackBuffer CreateFallbackBuffer() => new Buffer(this);

            private sealed class Buffer : DecoderFallbackBuffer
            {
                private readonly CountingFallback _owner;
                public Buffer(CountingFallback owner) => _owner = owner;

                public override bool Fallback(byte[] bytesUnknown, int index)
                {
                    unchecked { _owner.Count++; }   // only ever compared for change, so wrapping is harmless
                    return false;   // nothing to insert
                }

                public override char GetNextChar() => '\0';
                public override bool MovePrevious() => false;
                public override int Remaining => 0;
            }
        }

        public void Dispose()
        {
            _client.Dispose();
        }

        // The DNS API names its parameter hostName (a name over 255 characters, say); the caller's is host.
        private static Exception? BadHost(Exception ex)
        {
            Exception e = ex is AggregateException a ? a.GetBaseException() : ex;
            if (!(e is ArgumentException arg) || arg.ParamName != "hostName") return null;
            return e is ArgumentOutOfRangeException
                ? new ArgumentOutOfRangeException("host", arg.Message)
                : new ArgumentException(arg.Message, "host", arg);
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
