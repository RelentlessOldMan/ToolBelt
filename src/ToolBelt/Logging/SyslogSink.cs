// ToolBelt drop-in — also copy Logging/Log.cs.
using System;
using System.Globalization;
using System.Net.Sockets;
using System.Text;

namespace ToolBelt.Logging
{
    /// <summary>Syslog facilities (RFC 5424 §6.2.1).</summary>
    public enum SyslogFacility
    {
        Kernel = 0, User = 1, Mail = 2, Daemon = 3, Auth = 4, Syslog = 5, Lpr = 6, News = 7,
        Uucp = 8, Cron = 9, AuthPriv = 10, Ftp = 11,
        Local0 = 16, Local1 = 17, Local2 = 18, Local3 = 19, Local4 = 20, Local5 = 21, Local6 = 22, Local7 = 23,
    }

    /// <summary>How <see cref="SyslogSink"/> delivers messages.</summary>
    public enum SyslogTransport
    {
        /// <summary>UDP datagrams (port 514): fire-and-forget, may be lost; messages are capped at 2048 bytes.</summary>
        Udp,

        /// <summary>TCP with RFC 6587 octet-counting framing (port 601 / 514): reliable, reconnects on failure.</summary>
        Tcp,
    }

    /// <summary>
    /// Sends log events to a syslog collector (rsyslog, syslog-ng, Graylog, Splunk, a NAS) in RFC 5424 format:
    /// <c>&lt;PRI&gt;1 TIMESTAMP HOST APP PROCID MSGID - MESSAGE</c>, with the category as MSGID, the level mapped to a
    /// severity, and the exception appended to the message. Delivery failures are swallowed (and counted in
    /// <see cref="FailedSends"/>) — logging must never take the application down. <see cref="Format"/> is public and
    /// pure, so the wire format can be tested without a network.
    /// </summary>
    public sealed class SyslogSink : ILogSink
    {
        private const int UdpMaxBytes = 2048;
        private readonly string _host;
        private readonly int _port;
        private readonly SyslogTransport _transport;
        private readonly SyslogFacility _facility;
        private readonly string _hostName, _appName, _procId;
        private readonly object _gate = new object();
        private UdpClient? _udp;
        private TcpClient? _tcp;
        private NetworkStream? _tcpStream;
        private bool _disposed;
        private long _failed;

        public SyslogSink(string host, int port = 514, SyslogTransport transport = SyslogTransport.Udp,
            SyslogFacility facility = SyslogFacility.Local0, string? appName = null)
        {
            _host = string.IsNullOrWhiteSpace(host) ? throw new ArgumentException("Host is required.", nameof(host)) : host;
            if (port < 1 || port > 65535) throw new ArgumentOutOfRangeException(nameof(port), port, "Port must be 1–65535.");
            _port = port;
            _transport = transport;
            _facility = facility;
            _hostName = Token(Environment.MachineName, 255);
            _appName = Token(appName ?? AppDomain.CurrentDomain.FriendlyName, 48);
            _procId = Token(System.Diagnostics.Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture), 128);
        }

        /// <summary>Messages that could not be delivered.</summary>
        public long FailedSends => System.Threading.Interlocked.Read(ref _failed);

        public void Emit(LogEvent logEvent)
        {
            if (logEvent is null) return;
            string line = Format(logEvent, _facility, _hostName, _appName, _procId);
            byte[] payload = Encoding.UTF8.GetBytes(line);
            lock (_gate)
            {
                if (_disposed) return;
                try
                {
                    if (_transport == SyslogTransport.Udp) SendUdp(payload);
                    else SendTcp(payload);
                }
                catch (Exception ex) when (ex is SocketException || ex is System.IO.IOException || ex is ObjectDisposedException)
                {
                    System.Threading.Interlocked.Increment(ref _failed);
                    ResetTcp();
                }
            }
        }

        private void SendUdp(byte[] payload)
        {
            _udp ??= new UdpClient();
            int length = payload.Length;
            if (length > UdpMaxBytes)
            {
                length = UdpMaxBytes;
                while (length > 0 && (payload[length] & 0xC0) == 0x80) length--;      // don't split a UTF-8 sequence
            }
            _udp.Send(payload, length, _host, _port);
        }

        private void SendTcp(byte[] payload)
        {
            // Retry once only when an existing connection went stale; a fresh connect that fails won't do better
            // straight away (and a refused connect can take seconds on Windows).
            bool hadConnection = _tcpStream != null;
            for (int attempt = 0; attempt < (hadConnection ? 2 : 1); attempt++)
            {
                try
                {
                    if (_tcpStream == null)
                    {
                        _tcp = new TcpClient { NoDelay = true, SendTimeout = 5000 };
                        _tcp.Connect(_host, _port);
                        _tcpStream = _tcp.GetStream();
                    }
                    byte[] prefix = Encoding.ASCII.GetBytes(payload.Length.ToString(CultureInfo.InvariantCulture) + " ");
                    _tcpStream.Write(prefix, 0, prefix.Length);
                    _tcpStream.Write(payload, 0, payload.Length);
                    return;
                }
                catch when (hadConnection && attempt == 0)
                {
                    ResetTcp();                                                       // stale connection: reconnect once
                }
            }
        }

        private void ResetTcp()
        {
            try { _tcpStream?.Dispose(); } catch { }
            try { _tcp?.Dispose(); } catch { }
            _tcpStream = null;
            _tcp = null;
        }

        /// <summary>The RFC 5424 line for an event (no framing).</summary>
        public static string Format(LogEvent logEvent, SyslogFacility facility, string hostName, string appName, string procId)
        {
            if (logEvent is null) throw new ArgumentNullException(nameof(logEvent));
            int pri = (int)facility * 8 + Severity(logEvent.Level);
            string timestamp = logEvent.Timestamp.ToString("yyyy-MM-dd'T'HH:mm:ss.ffffffzzz", CultureInfo.InvariantCulture);
            string msgId = Token(logEvent.Category, 32);
            string message = logEvent.Exception == null ? logEvent.Message : logEvent.Message + " | " + logEvent.Exception;
            return "<" + pri.ToString(CultureInfo.InvariantCulture) + ">1 " + timestamp + " " + Token(hostName, 255) + " " + Token(appName, 48)
                   + " " + Token(procId, 128) + " " + msgId + " - " + message;
        }

        /// <summary>Syslog severity: Fatal→2 (critical), Error→3, Warning→4, Info→6, Debug/Trace→7.</summary>
        public static int Severity(LogLevel level)
        {
            switch (level)
            {
                case LogLevel.Fatal: return 2;
                case LogLevel.Error: return 3;
                case LogLevel.Warning: return 4;
                case LogLevel.Info: return 6;
                default: return 7;
            }
        }

        // RFC 5424 header fields: printable US-ASCII without spaces, length-limited; empty → "-" (NILVALUE).
        private static string Token(string? value, int maxLength)
        {
            if (string.IsNullOrEmpty(value)) return "-";
            var sb = new StringBuilder(Math.Min(value!.Length, maxLength));
            foreach (char c in value)
            {
                if (sb.Length == maxLength) break;
                sb.Append(c > 32 && c < 127 ? c : '_');
            }
            return sb.ToString();
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                _udp?.Dispose();
                ResetTcp();
            }
        }
    }
}
