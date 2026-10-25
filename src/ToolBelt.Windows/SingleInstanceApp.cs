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
    /// <see cref="SecondInstanceStarted"/> is raised on a thread-pool thread, one launch at a time, in arrival order;
    /// marshal to the UI thread. An exception thrown by a handler is dropped (it cannot reach the launch that caused it).
    /// The instance name and the pipe are per user, so another account cannot block or receive this user's launches. The
    /// pipe accepts only clients owned by the same account; an elevated and a non-elevated launch of one user can count as
    /// different owners, so such a launch may not reach the primary (it then reports <see cref="ForwardedToPrimary"/> false).
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
        private readonly Queue<(EventHandler<SecondInstanceEventArgs> Handlers, SecondInstanceEventArgs Args)> _pending =
            new Queue<(EventHandler<SecondInstanceEventArgs>, SecondInstanceEventArgs)>();
        private bool _draining;
        private EventHandler<SecondInstanceEventArgs>? _handlers;
        private int _disposed;

        /// <summary>
        /// Raised in the primary for each later launch. Launches that arrive before the first handler is attached (while the
        /// primary is still building its window) are kept and delivered as soon as one subscribes, so none are lost.
        /// </summary>
        public event EventHandler<SecondInstanceEventArgs>? SecondInstanceStarted
        {
            add
            {
                if (value is null) return;
                lock (_deliveryGate)
                {
                    _handlers += value;
                    foreach (var e in _queued) EnqueueLocked(value, e);
                    _queued.Clear();
                }
            }
            remove { lock (_deliveryGate) _handlers -= value; }
        }

        /// <summary>
        /// Claims the instance named <paramref name="appId"/> for this user session, or forwards <paramref name="args"/>
        /// to the instance that already holds it (waiting up to <paramref name="forwardTimeout"/>, default 5 s, for a
        /// primary that is still starting up; <see cref="Timeout.InfiniteTimeSpan"/> or <see cref="TimeSpan.MaxValue"/>
        /// waits without limit). If the forward fails because the primary exited meanwhile, this launch becomes the
        /// primary instead (<see cref="IsPrimary"/> true), so it handles its own arguments.
        /// </summary>
        public static SingleInstanceApp Start(string appId, IReadOnlyList<string> args, TimeSpan? forwardTimeout = null)
        {
            if (string.IsNullOrWhiteSpace(appId) || appId.IndexOfAny(new[] { '\\', '/' }) >= 0)
                throw new ArgumentException("App id is required and may not contain slashes.", nameof(appId));
            // The pipe name (id + session + SID) must stay under Windows' 256-character limit.
            if (appId.Length > MaxAppIdLength)
                throw new ArgumentException($"App id may be at most {MaxAppIdLength} characters.", nameof(appId));
            if (args is null) throw new ArgumentNullException(nameof(args));
            TimeSpan timeout = forwardTimeout ?? TimeSpan.FromSeconds(5);
            if (timeout < TimeSpan.Zero && timeout != Timeout.InfiniteTimeSpan)
                throw new ArgumentOutOfRangeException(nameof(forwardTimeout), forwardTimeout, "Timeout must be non-negative or Timeout.InfiniteTimeSpan.");
            // Per session and per user (SID), so another account cannot sit on the name.
            string user;
            using (var identity = System.Security.Principal.WindowsIdentity.GetCurrent())
                user = identity.User?.Value ?? Environment.UserName;
            int session;
            using (var self = System.Diagnostics.Process.GetCurrentProcess())
                session = self.SessionId;
            string pipe = "ToolBelt.SingleInstance." + appId + "." + session + "." + user;
            string gateName = @"Local\ToolBelt.SingleInstance." + appId + "." + user;
            var gate = SingleInstance.TryAcquire(gateName);
            if (gate.IsOwned) return new SingleInstanceApp(gate, pipe, primary: true, forwarded: false);
            bool forwarded = Forward(pipe, args, timeout);
            if (!forwarded)
            {
                // A primary that exited (or crashed) while this launch waited for it leaves nobody to forward to: take over
                // rather than lose the launch. A primary that is alive still holds the gate, so this changes nothing then.
                gate.Dispose();
                gate = SingleInstance.TryAcquire(gateName);
                if (gate.IsOwned) return new SingleInstanceApp(gate, pipe, primary: true, forwarded: false);
            }
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
            // Build (and size-check) the message once: one the primary would reject fails now, not after the timeout.
            if (args.Count > MaxArgs) return false;
            var message = new MemoryStream();
            WriteInt32(message, Environment.ProcessId);
            if (!TryWriteString(message, Environment.CurrentDirectory)) return false;
            WriteInt32(message, args.Count);
            foreach (string a in args)
                if (!TryWriteString(message, a ?? "")) return false;
            if (message.Length > MaxMessageBytes) return false;

            // Infinite (or MaxValue, which overflowed a DateTime deadline) means no deadline; a Stopwatch ignores clock steps.
            bool unlimited = timeout == Timeout.InfiniteTimeSpan || timeout == TimeSpan.MaxValue;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            long RemainingMs() => unlimited ? int.MaxValue : Math.Max(0, (long)(timeout - clock.Elapsed).TotalMilliseconds);
            while (true)
            {
                bool sent = false;
                try
                {
                    using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.CurrentUserOnly | PipeOptions.Asynchronous);
                    client.Connect((int)Math.Max(1, Math.Min(RemainingMs(), 1000)));
                    // Pipes don't support ReadTimeout; bound the reads with a token so a hung primary can't hang this launch,
                    // and by what is left of the forward timeout, which a stalled primary otherwise stretched to 5 s more.
                    using var replyTimeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(Math.Max(1, Math.Min(RemainingMs(), 5000))));
                    int primaryPid = ReadInt32Async(client, replyTimeout.Token).GetAwaiter().GetResult();
                    AllowSetForegroundWindow(primaryPid);                             // let the primary come to the front
                    message.Position = 0;
                    message.CopyTo(client);
                    client.Flush();
                    sent = true;
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
                    // Once the message is out, the primary may have delivered it: resending could open the file twice.
                    if (sent || RemainingMs() == 0) return false;
                    Thread.Sleep(50);                                                 // primary still starting its listener
                }
            }
        }

        private const int MaxArgs = 1000, MaxStringBytes = 32 * 1024, MaxMessageBytes = 1024 * 1024, MaxAppIdLength = 100;

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
                        // Acknowledge only a launch that will be kept: one past the pre-subscription cap was acknowledged and
                        // then dropped. (Only this loop adds to the queue, so room found here is still there below.)
                        bool accept = CanAccept();
                        await server.WriteAsync(new byte[] { accept ? (byte)1 : (byte)0 }, t).ConfigureAwait(false);
                        await server.FlushAsync(t).ConfigureAwait(false);
                        if (accept) Deliver(new SecondInstanceEventArgs(received, cwd, pid));
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
                    catch (Exception) when (!ct.IsCancellationRequested)
                    {
                        // A broken, malformed, oversized or silent client: drop it and keep listening for real launches.
                    }
                }
            }
        }

        private const int MaxQueued = 100;

        private bool CanAccept()
        {
            lock (_deliveryGate) return Volatile.Read(ref _disposed) == 0 && (_handlers != null || _queued.Count < MaxQueued);
        }

        private void Deliver(SecondInstanceEventArgs e)
        {
            lock (_deliveryGate)
            {
                if (_handlers is null)
                {
                    if (_queued.Count < MaxQueued) _queued.Add(e);                     // nobody subscribed yet: keep it
                    return;
                }
                EnqueueLocked(_handlers, e);
            }
        }

        // One drain loop at a time on the pool: a slow handler never blocks the listener, and launches stay in order.
        private void EnqueueLocked(EventHandler<SecondInstanceEventArgs> handlers, SecondInstanceEventArgs e)
        {
            _pending.Enqueue((handlers, e));
            if (_draining) return;
            _draining = true;
            _ = Task.Run(Drain);
        }

        private void Drain()
        {
            while (true)
            {
                (EventHandler<SecondInstanceEventArgs> Handlers, SecondInstanceEventArgs Args) next;
                lock (_deliveryGate)
                {
                    // Once disposed, the app is shutting down: handlers no longer run.
                    if (_pending.Count == 0 || Volatile.Read(ref _disposed) != 0) { _pending.Clear(); _draining = false; return; }
                    next = _pending.Dequeue();
                }
                try { next.Handlers(this, next.Args); }
                catch (Exception) { /* the launch that caused it is gone; keep delivering the rest */ }
            }
        }

        // Wire format: little-endian int32; strings as an int32 byte count + UTF-8, bounded per string and per message so a
        // client can't make the primary allocate gigabytes.
        private sealed class Budget
        {
            public Budget(int bytes) => Remaining = bytes;
            public int Remaining;
        }

        private static void WriteInt32(Stream s, int value) => s.Write(BitConverter.GetBytes(value), 0, 4);

        private static bool TryWriteString(Stream s, string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            if (bytes.Length > MaxStringBytes) return false;
            WriteInt32(s, bytes.Length);
            s.Write(bytes, 0, bytes.Length);
            return true;
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
        /// Stops listening, stops delivering launches not yet handed to a handler, and releases the instance name. Safe from
        /// any thread.
        /// </summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _cts.Cancel();
            bool stopped = true;
            try { stopped = _listener?.Wait(TimeSpan.FromSeconds(5)) ?? true; } catch (AggregateException) { }
            if (stopped) _cts.Dispose();                                               // a still-running listener holds its token
            _gate.Dispose();
        }

        private const int SW_RESTORE = 9;

        [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(int processId);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int cmd);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
    }
}
