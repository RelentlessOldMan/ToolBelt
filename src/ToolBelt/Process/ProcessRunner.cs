// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DiagnosticsProcess = System.Diagnostics.Process;

namespace ToolBelt.Process
{
    /// <summary>Options controlling how a child process is run.</summary>
    public sealed class ProcessRunOptions
    {
        /// <summary>Working directory for the child. Null uses the current directory.</summary>
        public string? WorkingDirectory { get; set; }

        /// <summary>Environment overrides applied on top of the inherited block. A null value removes the variable.</summary>
        public IReadOnlyDictionary<string, string?>? Environment { get; set; }

        /// <summary>
        /// Text piped to the child's standard input (in the background, encoded with <see cref="Encoding"/> when set),
        /// then the stream is closed. Null gives the child an empty, closed stdin (it does not inherit the caller's).
        /// </summary>
        public string? StandardInput { get; set; }

        /// <summary>Encoding for captured output/error and for <see cref="StandardInput"/>. Null uses the framework default.</summary>
        public Encoding? Encoding { get; set; }

        /// <summary>
        /// If set, the child is killed (the whole tree on .NET Core 3.0+ builds, only the direct child on the
        /// netstandard2.0 build) after this elapses and the result is flagged timed out. Reading the output counts
        /// too: if a grandchild keeps the output pipes open past the timeout, the run ends timed out with the output
        /// read so far (allowing a second's grace for output still in flight). The exit code is then the child's own if
        /// it had exited, and -1 if it was killed. A grandchild still running once the child has exited is not killed
        /// (its parent is gone, so it is no longer in the tree); put the child in a job object for that. With no timeout,
        /// once the child exits the run reads its output until the pipes close or nothing has arrived for a second, so a
        /// grandchild holding them open can't keep the run waiting (it waited for the grandchild, or forever for a
        /// server); the result is then not flagged timed out. Must be between zero and
        /// <see cref="int.MaxValue"/> milliseconds; <see cref="System.Threading.Timeout.InfiniteTimeSpan"/> means no
        /// timeout. An invalid value throws before the child starts.
        /// </summary>
        public TimeSpan? Timeout { get; set; }

        /// <summary>When true, a non-zero exit (or timeout) throws <see cref="ProcessRunException"/>.</summary>
        public bool ThrowOnNonZeroExit { get; set; }

        /// <summary>
        /// Invoked for each line of standard output as it arrives, on a background thread. If it throws, the run
        /// still completes and the first such exception is rethrown to the caller. No call starts after the run returns
        /// (a grandchild may still be writing to an inherited pipe after a timeout), and the run waits up to a second
        /// for a call already under way; one that runs longer than that carries on after the run returns, and anything
        /// it throws then is dropped.
        /// </summary>
        public Action<string>? OnOutputLine { get; set; }

        /// <summary>Invoked for each line of standard error as it arrives; see <see cref="OnOutputLine"/>.</summary>
        public Action<string>? OnErrorLine { get; set; }
    }

    /// <summary>The immutable outcome of running a process.</summary>
    public sealed class ProcessResult
    {
        internal ProcessResult(int exitCode, string standardOutput, string standardError, TimeSpan elapsed, bool timedOut, string commandLine)
        {
            ExitCode = exitCode;
            StandardOutput = standardOutput;
            StandardError = standardError;
            Elapsed = elapsed;
            TimedOut = timedOut;
            CommandLine = commandLine;
        }

        public int ExitCode { get; }
        /// <summary>
        /// Standard output, captured line by line: each line ends in <see cref="Environment.NewLine"/>, whatever the
        /// child wrote (\n, \r\n or none at the end), so this is text, not a byte-faithful copy.
        /// </summary>
        public string StandardOutput { get; }
        /// <summary>Standard error, captured line by line as for <see cref="StandardOutput"/>.</summary>
        public string StandardError { get; }
        public TimeSpan Elapsed { get; }
        public bool TimedOut { get; }
        /// <summary>The resolved "file args" string, for diagnostics.</summary>
        public string CommandLine { get; }

        public bool Success => !TimedOut && ExitCode == 0;

        /// <summary>Returns this result if it succeeded; otherwise throws <see cref="ProcessRunException"/>.</summary>
        public ProcessResult EnsureSuccess()
        {
            if (Success) return this;
            string reason = TimedOut ? "timed out" : $"exited with code {ExitCode}";
            throw new ProcessRunException($"Process {reason}: {CommandLine}", ExitCode, TimedOut, StandardError);
        }
    }

    /// <summary>Thrown by <see cref="ProcessResult.EnsureSuccess"/> (and the throw-on-error option) when a process fails.</summary>
    public sealed class ProcessRunException : Exception
    {
        public ProcessRunException(string message, int exitCode, bool timedOut, string standardError) : base(message)
        {
            ExitCode = exitCode;
            TimedOut = timedOut;
            StandardError = standardError;
        }

        public int ExitCode { get; }
        public bool TimedOut { get; }
        public string StandardError { get; }
    }

    /// <summary>
    /// Runs child processes and captures their output correctly: standard output and error are read on
    /// separate threads (never block-read both from one thread — the classic deadlock once a child writes
    /// more than a pipe buffer), standard input is written on another (so a child that never reads it cannot
    /// stall the call); the three are dedicated threads, not the thread pool, so callers that block pool
    /// threads cannot starve them, and a timeout or cancellation kills the child — the whole
    /// process tree on .NET Core 3.0+ builds, only the direct child on the netstandard2.0 build. Exact
    /// interleaving of the two streams is not guaranteed by the OS.
    /// </summary>
    public static class ProcessRunner
    {
        // How long to wait for the output pipes to close after a kill, or after a child exits at its deadline.
        private const int OutputGraceMilliseconds = 1000;

        public static ProcessResult Run(string fileName, string? arguments = null, ProcessRunOptions? options = null)
        {
            options ??= new ProcessRunOptions();
            int timeoutMs = TimeoutMilliseconds(options.Timeout);
            var run = Start(fileName, arguments, options);

            bool timedOut = false, killed = false;
            if (timeoutMs < 0)
                run.Process.WaitForExit();
            else if (!run.Process.WaitForExit(timeoutMs))
            {
                timedOut = true;
                killed = KillTree(run.Process);
            }

            // A grandchild that inherited the output pipes can hold them open after the child is gone, so the
            // wait for the rest of the output is bounded too.
            if (timeoutMs < 0)
            {
                while (!run.OutputClosed.Wait(OutputGraceMilliseconds) && !Quiet(run)) { }
            }
            else if (!run.OutputClosed.Wait(OutputBudget(run, timeoutMs, killed)))
                timedOut = true;
            return Finish(run, timedOut, killed, options);
        }

        public static async Task<ProcessResult> RunAsync(
            string fileName, string? arguments = null, ProcessRunOptions? options = null, CancellationToken cancellationToken = default)
        {
            options ??= new ProcessRunOptions();
            int timeoutMs = TimeoutMilliseconds(options.Timeout);
            // Before starting: an already-cancelled token still launched the child, which then ran unobserved.
            cancellationToken.ThrowIfCancellationRequested();
            var run = Start(fileName, arguments, options);

            bool exited = await CompletesWithin(run.Exited, timeoutMs, cancellationToken).ConfigureAwait(false);
            bool timedOut = false, canceled = false, killed = false;
            if (!exited)
            {
                canceled = cancellationToken.IsCancellationRequested;
                timedOut = !canceled;
                killed = KillTree(run.Process);
            }

            bool closed;
            if (exited && timeoutMs < 0)
            {
                // No timeout: read until the pipes close or go quiet (see ProcessRunOptions.Timeout).
                while (!(closed = await CompletesWithin(run.OutputClosed, OutputGraceMilliseconds, cancellationToken).ConfigureAwait(false))
                       && !cancellationToken.IsCancellationRequested && !Quiet(run)) { }
                if (!closed && !cancellationToken.IsCancellationRequested) closed = true;   // quiet: done, not timed out
            }
            else
                closed = await CompletesWithin(
                    run.OutputClosed, OutputBudget(run, timeoutMs, !exited), exited ? cancellationToken : CancellationToken.None).ConfigureAwait(false);
            if (!closed)
            {
                if (exited && cancellationToken.IsCancellationRequested) canceled = true;
                else timedOut = true;
            }

            if (canceled)
            {
                StopCallbacks(run);
                run.Process.Dispose();
                throw new OperationCanceledException(cancellationToken);
            }
            return Finish(run, timedOut, killed, options);
        }

        private sealed class RunState
        {
            public DiagnosticsProcess Process = null!;
            public readonly StringBuilder Output = new StringBuilder();
            public readonly StringBuilder Error = new StringBuilder();
            public readonly TaskCompletionSource<bool> ExitedSource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            public readonly TaskCompletionSource<bool> OutputEnd = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            public readonly TaskCompletionSource<bool> ErrorEnd = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            public Stopwatch Stopwatch = null!;
            public string CommandLine = "";
            public Exception? CallbackError;
            public int Done;             // the run has returned (or thrown): no more callbacks
            public int CallbacksRunning; // raised before Done is checked, so StopCallbacks can wait for the ones under way
            public long Lines;           // lines read so far, so a wait for the output can tell flowing from quiet
            public long LinesSeen;

            public Task Exited => ExitedSource.Task;
            public Task OutputClosed => Task.WhenAll(OutputEnd.Task, ErrorEnd.Task);
        }

        private static RunState Start(string fileName, string? arguments, ProcessRunOptions options)
        {
            if (fileName is null) throw new ArgumentNullException(nameof(fileName));
            // Process.Start threw InvalidOperationException with no parameter name.
            if (fileName.Length == 0) throw new ArgumentException("The file name must not be empty.", nameof(fileName));
            // A key holding '=' became another variable ("A=B" set to "C" reached the child as A = "B=C"), and a NUL cut
            // the block short.
            if (options.Environment != null)
                foreach (var kv in options.Environment)
                    if (string.IsNullOrEmpty(kv.Key) || kv.Key.IndexOf('=') >= 0 || kv.Key.IndexOf('\0') >= 0 || (kv.Value != null && kv.Value.IndexOf('\0') >= 0))
                        throw new ArgumentException($"Environment variable '{kv.Key}' is not valid: a name must be non-empty with no '=' or NUL, a value no NUL.",
                            nameof(ProcessRunOptions.Environment));

            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments ?? string.Empty,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                // Always redirected: unredirected, the child inherited the caller's stdin, read its data, and a child that
                // reads stdin (sort, a prompt) blocked on an interactive console until the timeout, or for good.
                RedirectStandardInput = true,
            };
            if (options.WorkingDirectory != null) psi.WorkingDirectory = options.WorkingDirectory;
            if (options.Encoding != null) { psi.StandardOutputEncoding = options.Encoding; psi.StandardErrorEncoding = options.Encoding; }
            if (options.Environment != null)
                foreach (var kv in options.Environment)
                {
                    if (kv.Value is null) psi.Environment.Remove(kv.Key);
                    else psi.Environment[kv.Key] = kv.Value;
                }

            var run = new RunState { CommandLine = string.IsNullOrEmpty(arguments) ? fileName : fileName + " " + arguments };
            var process = new DiagnosticsProcess { StartInfo = psi, EnableRaisingEvents = true };
            process.Exited += (_, __) => run.ExitedSource.TrySetResult(true);

            run.Stopwatch = Stopwatch.StartNew();
            try { process.Start(); }
            catch { process.Dispose(); throw; }
            run.Process = process;
            // Dedicated threads, not BeginOutputReadLine or Task.Run: those read and write on the thread pool, and with
            // the pool's threads blocked (in Run, say: two Runs on a pool capped at two threads) the child filled its
            // pipe and stalled, timing out with nothing captured.
            StreamReader stdout = process.StandardOutput, stderr = process.StandardError;
            StartThread(() => ReadLines(run, stdout, run.Output, options.OnOutputLine, run.OutputEnd), "ProcessRunner stdout");
            StartThread(() => ReadLines(run, stderr, run.Error, options.OnErrorLine, run.ErrorEnd), "ProcessRunner stderr");

            if (options.StandardInput != null)
            {
                StreamWriter stdin = process.StandardInput;
                string text = options.StandardInput;
                Encoding? encoding = options.Encoding;
                StartThread(() => WriteInput(stdin, text, encoding), "ProcessRunner stdin");
            }
            else
            {
                try { process.StandardInput.Close(); }
                catch (IOException) { /* the child already exited */ }
            }
            return run;
        }

        // Background, so a grandchild holding a pipe open cannot keep the host alive through a blocked reader.
        private static void StartThread(Action body, string name) =>
            new Thread(() => body()) { IsBackground = true, Name = name }.Start();

        private static void ReadLines(RunState run, StreamReader reader, StringBuilder buffer, Action<string>? callback, TaskCompletionSource<bool> end)
        {
            try
            {
                string? line;
                while ((line = reader.ReadLine()) != null) OnLine(run, line, buffer, callback);
            }
            catch (IOException) { /* the pipe broke */ }
            catch (ObjectDisposedException) { /* the run already finished */ }
            catch (Exception ex)
            {
                // A decoding failure, say. The pipe is still drained: left unread, a child writing more than a pipe buffer
                // blocked for good, so the run lasted its whole timeout, or forever with none.
                Interlocked.CompareExchange(ref run.CallbackError, ex, null);
                Drain(reader.BaseStream);
            }
            finally { end.TrySetResult(true); }
        }

        private static void Drain(Stream stream)
        {
            try
            {
                var discard = new byte[4096];
                while (stream.Read(discard, 0, discard.Length) > 0) { }
            }
            catch (IOException) { }
            catch (ObjectDisposedException) { }
        }

        // No line read and no callback running for a whole grace period: what is holding the pipes open has gone quiet.
        private static bool Quiet(RunState run)
        {
            long lines = Interlocked.Read(ref run.Lines);
            bool quiet = lines == run.LinesSeen && Volatile.Read(ref run.CallbacksRunning) == 0;
            run.LinesSeen = lines;
            return quiet;
        }

        // Runs on the reader threads. A throwing callback must not escape there (it would crash the host), so the
        // first such exception is kept and rethrown to the caller once the run finishes.
        private static void OnLine(RunState run, string line, StringBuilder buffer, Action<string>? callback)
        {
            Interlocked.Increment(ref run.Lines);
            // After the run has returned nobody can read the buffer: a grandchild writing to an inherited pipe after a
            // timeout grew it without bound.
            if (Volatile.Read(ref run.Done) != 0) return;
            // Capture can fail too (OutOfMemoryException past the StringBuilder's 2 GB limit); escaping here would
            // crash the host, so it is reported the same way.
            try { lock (buffer) buffer.AppendLine(line); }
            catch (Exception ex) { Interlocked.CompareExchange(ref run.CallbackError, ex, null); }
            // After a timeout or a cancel the reader threads kept calling back after Run had returned. Counting the call
            // before reading Done (both full fences) means StopCallbacks either sees it running or it sees Done.
            if (callback is null) return;
            Interlocked.Increment(ref run.CallbacksRunning);
            try
            {
                if (Volatile.Read(ref run.Done) != 0) return;
                try { callback(line); }
                catch (Exception ex) { Interlocked.CompareExchange(ref run.CallbackError, ex, null); }
            }
            finally { Interlocked.Decrement(ref run.CallbacksRunning); }
        }

        // A callback already running when Done was checked kept going after the run returned, and its exception, raised
        // after Finish had read CallbackError, was lost. Waits a bounded time: a callback that never returns can't hang Run.
        private static void StopCallbacks(RunState run)
        {
            Interlocked.Exchange(ref run.Done, 1);
            if (Volatile.Read(ref run.CallbacksRunning) == 0) return;
            var wait = Stopwatch.StartNew();
            var spin = new SpinWait();
            while (Volatile.Read(ref run.CallbacksRunning) != 0 && wait.ElapsedMilliseconds < OutputGraceMilliseconds)
                spin.SpinOnce();
        }

        private static void WriteInput(StreamWriter stdin, string text, Encoding? encoding)
        {
            try
            {
                if (encoding is null)
                    stdin.Write(text);
                else
                {
                    byte[] bytes = encoding.GetBytes(text); // no preamble: a child reading stdin doesn't expect one
                    stdin.BaseStream.Write(bytes, 0, bytes.Length);
                }
                stdin.Close();
            }
            catch (IOException) { /* the child exited (or was killed) without reading all of it */ }
            catch (ObjectDisposedException) { /* the run already finished */ }
        }

        private static ProcessResult Finish(RunState run, bool timedOut, bool killed, ProcessRunOptions options)
        {
            StopCallbacks(run);
            run.Stopwatch.Stop();
            // A child that exited on time but left a grandchild holding the pipes had its real exit code replaced by -1.
            int exitCode = killed ? -1 : run.Process.ExitCode;
            run.Process.Dispose();

            Exception? callbackError = Volatile.Read(ref run.CallbackError);
            if (callbackError != null) ExceptionDispatchInfo.Capture(callbackError).Throw();

            string output, error;
            lock (run.Output) output = run.Output.ToString();
            lock (run.Error) error = run.Error.ToString();
            var result = new ProcessResult(exitCode, output, error, run.Stopwatch.Elapsed, timedOut, run.CommandLine);
            return options.ThrowOnNonZeroExit ? result.EnsureSuccess() : result;
        }

        // Validated before the child starts, so a bad value can't leave a process running.
        private static int TimeoutMilliseconds(TimeSpan? timeout)
        {
            if (!timeout.HasValue || timeout.Value == Timeout.InfiniteTimeSpan) return Timeout.Infinite;
            TimeSpan t = timeout.Value;
            if (t < TimeSpan.Zero || t.TotalMilliseconds > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(ProcessRunOptions.Timeout), t,
                    "Timeout must be between zero and int.MaxValue milliseconds, or Timeout.InfiniteTimeSpan.");
            return (int)Math.Ceiling(t.TotalMilliseconds);
        }

        private static int OutputBudget(RunState run, int timeoutMs, bool killed)
        {
            if (killed) return OutputGraceMilliseconds;
            if (timeoutMs < 0) return Timeout.Infinite;
            long left = timeoutMs - run.Stopwatch.ElapsedMilliseconds;
            return left > OutputGraceMilliseconds ? (int)left : OutputGraceMilliseconds;
        }

        private static async Task<bool> CompletesWithin(Task task, int milliseconds, CancellationToken cancellationToken)
        {
            if (task.IsCompleted) return true;
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            Task first = await Task.WhenAny(task, Task.Delay(milliseconds, stop.Token)).ConfigureAwait(false);
            stop.Cancel(); // release the timer and the registration on the caller's token
            return first == task;
        }

        // Whether the child was killed: one that exited by itself as the timeout fired keeps its exit code (it was -1).
        private static bool KillTree(DiagnosticsProcess process)
        {
            try
            {
                if (process.HasExited) return false;
#if NETSTANDARD2_0
                process.Kill(); // best-effort: direct child only; the platform assembly can do a true tree kill
#else
                process.Kill(entireProcessTree: true);
#endif
                return true;
            }
            catch
            {
                // The process may have exited between the check and the kill, or be inaccessible.
                try { return !process.HasExited; }
                catch { return true; }
            }
        }
    }
}
