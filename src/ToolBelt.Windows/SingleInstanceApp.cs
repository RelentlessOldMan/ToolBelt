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

        /// <summary>Raised in the primary for each later launch.</summary>
        public event EventHandler<SecondInstanceEventArgs>? SecondInstanceStarted;

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
            string pipe = "ToolBelt.SingleInstance." + appId + "." + System.Diagnostics.Process.GetCurrentProcess().SessionId;
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
                    using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.CurrentUserOnly);
                    int remaining = (int)Math.Max(1, (deadline - DateTime.UtcNow).TotalMilliseconds);
                    client.Connect(Math.Min(remaining, 1000));
                    using var reader = new BinaryReader(client, Encoding.UTF8, leaveOpen: true);
                    using var writer = new BinaryWriter(client, Encoding.UTF8, leaveOpen: true);
                    int primaryPid = reader.ReadInt32();
                    AllowSetForegroundWindow(primaryPid);                             // let the primary come to the front
                    writer.Write(Environment.ProcessId);
                    writer.Write(Environment.CurrentDirectory);
                    writer.Write(args.Count);
                    foreach (string a in args) writer.Write(a ?? "");
                    writer.Flush();
                    return reader.ReadByte() == 1;
                }
                catch (Exception ex) when (ex is TimeoutException || ex is IOException)
                {
                    if (DateTime.UtcNow >= deadline) return false;
                    Thread.Sleep(50);                                                 // primary still starting its listener
                }
            }
        }

        private async Task ListenAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    using var server = new NamedPipeServerStream(_pipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await server.WaitForConnectionAsync(ct).ConfigureAwait(false);
                    using var reader = new BinaryReader(server, Encoding.UTF8, leaveOpen: true);
                    using var writer = new BinaryWriter(server, Encoding.UTF8, leaveOpen: true);
                    writer.Write(Environment.ProcessId);
                    writer.Flush();
                    int pid = reader.ReadInt32();
                    string cwd = reader.ReadString();
                    int count = reader.ReadInt32();
                    if (count < 0 || count > 10_000) throw new InvalidDataException("Implausible argument count.");
                    var received = new string[count];
                    for (int i = 0; i < count; i++) received[i] = reader.ReadString();
                    writer.Write((byte)1);
                    writer.Flush();
                    var handler = SecondInstanceStarted;
                    if (handler != null)
                    {
                        var e = new SecondInstanceEventArgs(received, cwd, pid);
                        _ = Task.Run(() => handler(this, e));                          // never let a slow handler block the next launch
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
                catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is EndOfStreamException)
                {
                    // A client that disconnected mid-message: ignore it and keep listening.
                }
            }
        }

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
