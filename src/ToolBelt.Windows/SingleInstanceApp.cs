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
    /// At most 100 launches wait at a time (before anyone subscribes, or behind a slow handler); a launch past that is
    /// refused, so the secondary sees <see cref="ForwardedToPrimary"/> false. A launch's arguments may total up to 1 MB
    /// of text (64 KB each), far past what a command line can hold, and any number of them a command line can hold.
    /// The instance name and the pipe are per user, so another account cannot receive this user's launches (though, since
    /// pipe names are machine-wide, one that creates the pipe first can keep them from being forwarded). The pipe accepts
    /// only clients owned by the same account; an elevated and a non-elevated launch of one user can count as
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
        /// waits without limit). Each attempt still gets at least 1 s, so a zero timeout reaches a primary that is running
        /// (it serves one launch at a time, and a 1 ms attempt between two of them lost the launch). If the forward fails because the primary exited meanwhile, this launch becomes the
        /// primary instead (<see cref="IsPrimary"/> true), so it handles its own arguments. App ids compare without
        /// regard to case ("Contoso.Viewer" and "contoso.viewer" are one instance).
        /// </summary>
        public static SingleInstanceApp Start(string appId, IReadOnlyList<string> args, TimeSpan? forwardTimeout = null)
        {
            if (appId is null) throw new ArgumentNullException(nameof(appId));
            if (string.IsNullOrWhiteSpace(appId) || appId.IndexOfAny(new[] { '\\', '/' }) >= 0)
                throw new ArgumentException("App id is required and may not contain slashes.", nameof(appId));
            // A NUL ends the kernel object names early (dropping the per-user part) and breaks the pipe name.
            foreach (char c in appId)
                if (char.IsControl(c)) throw new ArgumentException("App id may not contain control characters.", nameof(appId));
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
            // ".2": the wire format (UTF-16 strings) changed, so a launch never talks to an older primary's listener.
            // Pipe names ignore case but mutex names don't: "App" and "app" shared a pipe yet both became primary, and the
            // second's launches went to the first. Both names use one folded id: the kernel's case table is not
            // ToUpperInvariant's (it leaves U+017F and Georgian alone), so "...s" and "...ſ" shared a gate but not a pipe
            // and the second launch was lost.
            string folded = appId.ToUpperInvariant();
            string pipe = "ToolBelt.SingleInstance.2." + folded + "." + session + "." + user;
            string gateName = @"Local\ToolBelt.SingleInstance." + folded + "." + user;
            var gate = SingleInstance.TryAcquire(gateName);
            if (gate.IsOwned) return new SingleInstanceApp(gate, pipe, primary: true, forwarded: false);
            var clock = System.Diagnostics.Stopwatch.StartNew();
            bool forwarded = Forward(pipe, args, timeout, out bool sent);
            if (!forwarded)
            {
                // A primary that exited (or crashed) while this launch waited for it leaves nobody to forward to: take over
                // rather than lose the launch. A primary that is alive still holds the gate, so this changes nothing then.
                gate.Dispose();
                gate = SingleInstance.TryAcquire(gateName);
                if (gate.IsOwned) return new SingleInstanceApp(gate, pipe, primary: true, forwarded: false);
                // A primary that got the message (and may have delivered it) but whose answer was lost, or that refused it,
                // is alive: resending to it handled the launch twice.
                if (sent) return new SingleInstanceApp(gate, pipe, primary: false, forwarded: false);
                // Another launch took over first: it is the primary now, so forward to it rather than lose this launch. With
                // what is left of the timeout: a second full wait took a launch to twice the forward timeout.
                TimeSpan left = timeout == Timeout.InfiniteTimeSpan || timeout == TimeSpan.MaxValue ? timeout
                    : clock.Elapsed >= timeout ? TimeSpan.Zero : timeout - clock.Elapsed;
                forwarded = Forward(pipe, args, left, out _);
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

        // A primary that is up gets this long to answer even under a zero timeout: a busy machine delays its listener by
        // hundreds of milliseconds (a 100 ms floor still lost launches under load).
        private const int MinAttemptMs = 1000;

        // sent: the message reached a primary (which may have delivered it), so it must not go out again.
        private static bool Forward(string pipeName, IReadOnlyList<string> args, TimeSpan timeout, out bool sent)
        {
            sent = false;
            // Build (and size-check) the message once: one the primary would reject fails now, not after the timeout. The
            // budget is the primary's: string bytes only. Counting the length headers too refused launches it would take.
            if (args.Count > MaxArgs) return false;
            var message = new MemoryStream();
            var budget = new Budget(MaxMessageBytes);
            WriteInt32(message, Environment.ProcessId);
            if (!TryWriteString(message, Environment.CurrentDirectory, budget)) return false;
            WriteInt32(message, args.Count);
            foreach (string a in args)
                if (!TryWriteString(message, a ?? "", budget)) return false;

            // Infinite (or MaxValue, which overflowed a DateTime deadline) means no deadline; a Stopwatch ignores clock steps.
            bool unlimited = timeout == Timeout.InfiniteTimeSpan || timeout == TimeSpan.MaxValue;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            long RemainingMs() => unlimited ? int.MaxValue : Math.Max(0, (long)(timeout - clock.Elapsed).TotalMilliseconds);
            while (true)
            {
                try
                {
                    using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.CurrentUserOnly | PipeOptions.Asynchronous);
                    // At least MinAttemptMs: the listener builds its next pipe after each client, and a 1 ms connect (a zero
                    // timeout) that fell in that gap lost the launch to a primary that was up and ready.
                    client.Connect((int)Math.Max(MinAttemptMs, Math.Min(RemainingMs(), 1000)));
                    // Pipes don't support ReadTimeout. Past the deadline (5 s, or what is left of the forward timeout, which a
                    // stalled primary otherwise stretched) a timer cancels the pipe's I/O, so a primary that took the
                    // connection and then stopped reading or writing can't hang this launch. It repeats every 50 ms: a cancel
                    // that lands between two calls has nothing to cancel. (Blocking on the async calls was sync-over-async.)
                    using var cancelIo = new Timer(CancelPipeIo, client, Math.Max(MinAttemptMs, Math.Min(RemainingMs(), 5000)), 50);
                    var pid = new byte[4];
                    client.ReadExactly(pid);
                    AllowSetForegroundWindow(BitConverter.ToInt32(pid, 0));           // let the primary come to the front
                    sent = true;
                    client.Write(message.GetBuffer(), 0, (int)message.Length);
                    var ack = new byte[1];
                    client.ReadExactly(ack);
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
                    // The primary is still starting its listener. A bounded retry inside a synchronous call that takes no
                    // token, the one kind of Thread.Sleep the house rules allow; never past the forward timeout.
                    Thread.Sleep((int)Math.Min(50, RemainingMs()));
                }
            }
        }

        // A synchronous read or write on the pipe then fails with ERROR_OPERATION_ABORTED (an IOException or
        // OperationCanceledException). Once the pipe is closed there is nothing to cancel.
        private static void CancelPipeIo(object? state)
        {
            try { CancelIoEx(((NamedPipeClientStream)state!).SafePipeHandle, IntPtr.Zero); }
            catch (Exception) { /* closed meanwhile */ }
        }

        // UTF-16 on the wire: the byte limits are twice the UTF-8 ones, so the same text fits as before. A command line
        // (32,767 characters) holds at most 16,384 arguments; a cap of 1000 dropped a launch of 1001 short file names.
        private const int MaxArgs = 32768, MaxStringBytes = 64 * 1024, MaxMessageBytes = 2 * 1024 * 1024, MaxAppIdLength = 100;

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
                catch (Exception) when (!ct.IsCancellationRequested)
                {
                    // Name busy or squatted, or any other failure: back off and retry rather than stop listening for good,
                    // which lost every later launch without a trace.
                    try { await Task.Delay(500, ct).ConfigureAwait(false); } catch (OperationCanceledException) { return; }
                    continue;
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
            // Behind a slow handler the delivery queue had no cap: 300 launches of up to 2 MB each held 515 MB.
            lock (_deliveryGate) return Volatile.Read(ref _disposed) == 0 && (_handlers != null ? _pending.Count < MaxQueued : _queued.Count < MaxQueued);
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

        // Wire format: little-endian int32; strings as an int32 byte count + raw UTF-16 code units, bounded per string
        // and per message so a client can't make the primary allocate gigabytes.
        private sealed class Budget
        {
            public Budget(int bytes) => Remaining = bytes;
            public int Remaining;
        }

        private static void WriteInt32(Stream s, int value) => s.Write(BitConverter.GetBytes(value), 0, 4);

        private static bool TryWriteString(Stream s, string value, Budget budget)
        {
            // The raw UTF-16 code units: a file name may hold an unpaired surrogate, which any encoder turns into U+FFFD.
            byte[] bytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(value.AsSpan()).ToArray();
            if (bytes.Length > MaxStringBytes || bytes.Length > budget.Remaining) return false;
            budget.Remaining -= bytes.Length;
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
            if (length % 2 != 0) throw new InvalidDataException("String is not UTF-16.");
            budget.Remaining -= length;
            var bytes = new byte[length];
            await s.ReadExactlyAsync(bytes, ct).ConfigureAwait(false);
            return new string(System.Runtime.InteropServices.MemoryMarshal.Cast<byte, char>(bytes));
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
        [DllImport("kernel32.dll")] private static extern bool CancelIoEx(Microsoft.Win32.SafeHandles.SafePipeHandle handle, IntPtr overlapped);
    }
}
