// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace ToolBelt.Net
{
    /// <summary>
    /// A thin, line-oriented TCP client with connect/read/write timeouts — the shape most instrument and
    /// service control interfaces take. Connects on construction; send a line and read the response line.
    /// This is a small tested wrapper, not a protocol framework. Not thread-safe. Dispose closes the socket.
    /// </summary>
    public sealed class TcpLineClient : IDisposable
    {
        private readonly TcpClient _client;
        private readonly StreamReader _reader;
        private readonly StreamWriter _writer;

        public TcpLineClient(string host, int port,
            TimeSpan? connectTimeout = null, TimeSpan? readTimeout = null, TimeSpan? writeTimeout = null, Encoding? encoding = null)
        {
            if (host is null) throw new ArgumentNullException(nameof(host));
            encoding ??= new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

            _client = new TcpClient();
            Task connect = _client.ConnectAsync(host, port);
            int connectMs = (int)(connectTimeout ?? TimeSpan.FromSeconds(30)).TotalMilliseconds;
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

            NetworkStream stream = _client.GetStream();
            if (readTimeout.HasValue) stream.ReadTimeout = (int)readTimeout.Value.TotalMilliseconds;
            if (writeTimeout.HasValue) stream.WriteTimeout = (int)writeTimeout.Value.TotalMilliseconds;

            _reader = new StreamReader(stream, encoding);
            _writer = new StreamWriter(stream, encoding) { AutoFlush = true, NewLine = "\n" };
        }

        /// <summary>Sends a line (terminated with LF) and returns the next response line, or null at end of stream.</summary>
        public string? SendReceive(string line)
        {
            Send(line);
            return ReadLine();
        }

        public void Send(string line)
        {
            if (line is null) throw new ArgumentNullException(nameof(line));
            _writer.WriteLine(line);
        }

        public string? ReadLine() => _reader.ReadLine();

        public void Dispose()
        {
            try { _writer.Dispose(); } catch { }
            try { _reader.Dispose(); } catch { }
            _client.Dispose();
        }
    }
}
