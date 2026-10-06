// ToolBelt.Windows drop-in — Windows-only (net8.0-windows); also copy SingleInstance.cs.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ToolBelt.Windows
{
    /// <summary>What a second launch sent to the primary instance.</summary>
    public sealed class SecondInstanceEventArgs : EventArgs
    {
        internal SecondInstanceEventArgs(IReadOnlyList<string> args, string workingDirectory, int processId)
        {
            Args = args;
            WorkingDirectory = workingDirectory;
            ProcessId = processId;
        }

        /// <summary>The second process's command-line arguments (resolve relative paths against <see cref="WorkingDirectory"/>).</summary>
        public IReadOnlyList<string> Args { get; }

        public string WorkingDirectory { get; }
        public int ProcessId { get; }
    }

    /// <summary>
    /// Single-instance application plumbing: the first launch becomes the primary; a later launch hands its command
    /// line (and working directory) to the primary over a per-user named pipe and should then exit — the "double-click a
    /// second file and it opens in the window that's already running" behaviour. The secondary also grants the primary
    /// permission to take the foreground (<c>AllowSetForegroundWindow</c>), so the primary can call
    /// <see cref="BringToFront"/> from its handler; Windows otherwise blocks a background process from stealing focus.
    /// <code>
    /// using var app = SingleInstanceApp.Start("Contoso.Viewer", args);
    /// if (!app.IsPrimary) return;                 // args were delivered to the running instance
    /// app.SecondInstanceStarted += (s, e) => ui.Invoke(() => { Open(e.Args); SingleInstanceApp.BringToFront(mainHwnd); });
    /// </code>
    /// <see cref="SecondInstanceStarted"/> is raised on a thread-pool thread; marshal to the UI thread. The pipe accepts
    /// connections from the same user only.
    /// </summary>
    public sealed class SingleInstanceApp : IDisposable
    {
        private readonly SingleInstance _gate;
        private readonly string _pipeName;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly Task? _listener;

        private SingleInstanceApp(SingleInstance gate, string pipeName, bool primary, bool forwarded)
        {
            _gate = gate;
            _pipeName = pipeName;
            IsPrimary = primary;
            ForwardedToPrimary = forwarded;
            if (primary) _listener = Task.Run(() => ListenAsync(_cts.Token));
        }

        /// <summary>True in the first instance (keep running); false in a later one (exit).</summary>
        public bool IsPrimary { get; }

        /// <summary>In a secondary instance: whether the primary acknowledged the arguments.</summary>
        public bool ForwardedToPrimary { get; }

        private readonly object _deliveryGate = new object();
        private readonly List<SecondInstanceEventArgs> _queued = new List<SecondInstanceEventArgs>();
        private EventHandler<SecondInstanceEventArgs>? _handlers;

        /// <summary>
        /// Raised in the primary for each later launch. Launches that arrive before the first handler is attached (while the
        /// primary is still building its window) are kept and delivered as soon as one subscribes, so none are lost.
        /// </summary>
        public event EventHandler<SecondInstanceEventArgs>? SecondInstanceStarted
        {
            add
            {
                if (value is null) return;
                List<SecondInstanceEventArgs> backlog;
                lock (_deliveryGate)
                {
                    _handlers += value;
                    backlog = new List<SecondInstanceEventArgs>(_queued);
                    _queued.Clear();
                }
                if (backlog.Count > 0) _ = Task.Run(() => { foreach (var e in backlog) value(this, e); });
            }
            remove { lock (_deliveryGate) _handlers -= value; }
        }

        /// <summary>
        /// Claims the instance named <paramref name="appId"/> for this user session, or forwards <paramref name="args"/>
        /// to the instance that already holds it (waiting up to <paramref name="forwardTimeout"/>, default 5 s, for a
        /// primary that is still starting up).
        /// </summary>
        public static SingleInstanceApp Start(string appId, IReadOnlyList<string> args, TimeSpan? forwardTimeout = null)
        {
            if (string.IsNullOrWhiteSpace(appId) || appId.IndexOfAny(new[] { '\\', '/' }) >= 0)
                throw new ArgumentException("App id is required and may not contain slashes.", nameof(appId));
            if (args is null) throw new ArgumentNullException(nameof(args));
            // Per session and per user (SID), so another account cannot sit on the name.
            string user = System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
            string pipe = "ToolBelt.SingleInstance." + appId + "." + System.Diagnostics.Process.GetCurrentProcess().SessionId + "." + user;
            var gate = SingleInstance.TryAcquire(@"Local\ToolBelt.SingleInstance." + appId);
            if (gate.IsOwned) return new SingleInstanceApp(gate, pipe, primary: true, forwarded: false);
            bool forwarded = Forward(pipe, args, forwardTimeout ?? TimeSpan.FromSeconds(5));
            return new SingleInstanceApp(gate, pipe, primary: false, forwarded);
        }

        /// <summary>Restores (if minimized) and activates a top-level window.</summary>
        public static bool BringToFront(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) throw new ArgumentException("Window handle is required.", nameof(hwnd));
            if (IsIconic(hwnd)) ShowWindow(hwnd, SW_RESTORE);
            return SetForegroundWindow(hwnd);
        }

        private static bool Forward(string pipeName, IReadOnlyList<string> args, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (true)
            {
                try
                {
                    using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.CurrentUserOnly | PipeOptions.Asynchronous);
                    int remaining = (int)Math.Max(1, (deadline - DateTime.UtcNow).TotalMilliseconds);
                    client.Connect(Math.Min(remaining, 1000));
                    // Pipes don't support ReadTimeout; bound the reads with a token so a hung primary can't hang this launch.
                    using var replyTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    int primaryPid = ReadInt32Async(client, replyTimeout.Token).GetAwaiter().GetResult();
                    AllowSetForegroundWindow(primaryPid);                             // let the primary come to the front
                    var message = new MemoryStream();
                    WriteInt32(message, Environment.ProcessId);
                    WriteString(message, Environment.CurrentDirectory);
                    WriteInt32(message, args.Count);
                    foreach (string a in args) WriteString(message, a ?? "");
                    if (message.Length > MaxMessageBytes) return false;               // the primary would reject it anyway
                    message.Position = 0;
                    message.CopyTo(client);
                    client.Flush();
                    var ack = new byte[1];
                    client.ReadExactlyAsync(ack, replyTimeout.Token).AsTask().GetAwaiter().GetResult();
                    return ack[0] == 1;
                }
                catch (UnauthorizedAccessException)
                {
                    return false;                                                     // the pipe belongs to someone else
                }
                catch (Exception ex) when (ex is TimeoutException || ex is IOException || ex is OperationCanceledException)
                {
                    if (DateTime.UtcNow >= deadline) return false;
                    Thread.Sleep(50);                                                 // primary still starting its listener
                }
            }
        }

        private const int MaxArgs = 1000, MaxStringBytes = 32 * 1024, MaxMessageBytes = 1024 * 1024;

        private async Task ListenAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                NamedPipeServerStream server;
                try
                {
                    // FirstPipeInstance: if anyone else already holds this name, fail (and retry later) rather than share it.
                    server = new NamedPipeServerStream(_pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly | PipeOptions.FirstPipeInstance);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    try { await Task.Delay(500, ct).ConfigureAwait(false); } catch (OperationCanceledException) { return; }
                    continue;                                                          // name busy or squatted: back off, don't spin
                }

                using (server)
                {
                    try
                    {
                        await server.WaitForConnectionAsync(ct).ConfigureAwait(false);
                        // One client at a time, so a client that connects and never speaks must not hold everyone else up.
                        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        timeout.CancelAfter(TimeSpan.FromSeconds(5));
                        var t = timeout.Token;
                        await server.WriteAsync(BitConverter.GetBytes(Environment.ProcessId), t).ConfigureAwait(false);
                        await server.FlushAsync(t).ConfigureAwait(false);
                        var budget = new Budget(MaxMessageBytes);
                        int pid = await ReadInt32Async(server, t).ConfigureAwait(false);
                        string cwd = await ReadStringAsync(server, budget, t).ConfigureAwait(false);
                        int count = await ReadInt32Async(server, t).ConfigureAwait(false);
                        if (count < 0 || count > MaxArgs) throw new InvalidDataException("Implausible argument count.");
                        var received = new string[count];
                        for (int i = 0; i < count; i++) received[i] = await ReadStringAsync(server, budget, t).ConfigureAwait(false);
                        await server.WriteAsync(new byte[] { 1 }, t).ConfigureAwait(false);
                        await server.FlushAsync(t).ConfigureAwait(false);
                        Deliver(new SecondInstanceEventArgs(received, cwd, pid));
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
                    catch (Exception) when (!ct.IsCancellationRequested)
                    {
                        // A broken, malformed, oversized or silent client: drop it and keep listening for real launches.
                    }
                }
            }
        }

        private void Deliver(SecondInstanceEventArgs e)
        {
            EventHandler<SecondInstanceEventArgs>? handlers;
            lock (_deliveryGate)
            {
                handlers = _handlers;
                if (handlers is null)
                {
                    if (_queued.Count < 100) _queued.Add(e);                           // nobody subscribed yet: keep it
                    return;
                }
            }
            _ = Task.Run(() => handlers(this, e));                                     // never let a slow handler block the next launch
        }

        // Wire format: little-endian int32; strings as an int32 byte count + UTF-8, bounded per string and per message so a
        // client can't make the primary allocate gigabytes.
        private sealed class Budget
        {
            public Budget(int bytes) => Remaining = bytes;
            public int Remaining;
        }

        private static void WriteInt32(Stream s, int value) => s.Write(BitConverter.GetBytes(value), 0, 4);

        private static void WriteString(Stream s, string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            WriteInt32(s, bytes.Length);
            s.Write(bytes, 0, bytes.Length);
        }

        private static async Task<int> ReadInt32Async(Stream s, CancellationToken ct)
        {
            var b = new byte[4];
            await s.ReadExactlyAsync(b, ct).ConfigureAwait(false);
            return BitConverter.ToInt32(b, 0);
        }

        private static async Task<string> ReadStringAsync(Stream s, Budget budget, CancellationToken ct)
        {
            int length = await ReadInt32Async(s, ct).ConfigureAwait(false);
            if (length < 0 || length > MaxStringBytes || length > budget.Remaining) throw new InvalidDataException("String too long.");
            budget.Remaining -= length;
            var bytes = new byte[length];
            await s.ReadExactlyAsync(bytes, ct).ConfigureAwait(false);
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
        }

        /// <summary>
        /// Stops listening and releases the instance name. Call it on the thread that called <see cref="Start"/>: the name is
        /// a thread-owned mutex, and releasing it from another thread fails (it is then freed only when that thread exits).
        /// </summary>
        public void Dispose()
        {
            _cts.Cancel();
            try { _listener?.Wait(TimeSpan.FromSeconds(5)); } catch (AggregateException) { }
            _cts.Dispose();
            _gate.Dispose();
        }

        private const int SW_RESTORE = 9;

        [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(int processId);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int cmd);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
    }
}
