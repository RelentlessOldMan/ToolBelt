// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
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
        /// On .NET Framework (the netstandard2.0 build), when the console's input code page is UTF-8 (65001), the
        /// framework writes a UTF-8 byte order mark to the child's stdin as it starts, before this text and even when
        /// this is null; netstandard2.0 offers no way to choose the stdin encoding, so a child that minds it should skip
        /// a leading EF BB BF.
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
        /// server); any text counts as arriving, a line not yet finished too, and is kept. The result is then not
        /// flagged timed out. Time the line callbacks spend on text already read when the timeout runs out is the
        /// caller's, not the child's: the run waits while they get through it (each second must finish a call), so a slow
        /// callback does not turn a child that exited on time into a timeout. Must be between zero and <see cref="int.MaxValue"/> milliseconds; <see cref="System.Threading.Timeout.InfiniteTimeSpan"/> means no
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
            string reason = TimedOut ? "timed out" : "exited with code " + ExitCode.ToString(CultureInfo.InvariantCulture);
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
    /// interleaving of the two streams is not guaranteed by the OS. Once a run has returned, a grandchild still
    /// holding the inherited output pipes does not keep the run's reader threads: on Windows their blocked reads are
    /// cancelled. Elsewhere each reader thread stays blocked until the grandchild closes its end of the pipe.
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
                while (!run.ReadersLeft.Wait(OutputGraceMilliseconds) && !Quiet(run)) { }
            }
            else if (!ReadersEndWithin(run, OutputBudget(run, timeoutMs, killed)))
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
                closed = await OutputClosesWithin(
                    run, OutputBudget(run, timeoutMs, !exited), exited ? cancellationToken : CancellationToken.None).ConfigureAwait(false);
            if (!closed)
            {
                if (exited && cancellationToken.IsCancellationRequested) canceled = true;
                else timedOut = true;
            }

            if (canceled)
            {
                StopCallbacks(run);
                ReleaseReaders(run);
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
            public long Lines;           // reads that returned text and callbacks finished, so a wait can tell flowing from quiet
            public long LinesSeen;
            public long CallbacksDone;   // callbacks finished, so a wait can tell a backlog moving from one stuck
            public readonly Reader Out = new Reader(), Err = new Reader();

            public Task Exited => ExitedSource.Task;
            public Task OutputClosed => Task.WhenAll(OutputEnd.Task, ErrorEnd.Task);
            // For the synchronous Run: the WhenAll above completes on a pool thread (the sources run continuations
            // asynchronously), so with the pool busy Run waited out its timeout and reported TimedOut for a child that met it.
            public readonly CountdownEvent ReadersLeft = new CountdownEvent(2);
        }

        // One reader thread's progress: chunks it has read from the pipe, and chunks whose lines it has handed to the
        // callback. A chunk read but not yet done is backlog the callback is still working through.
        private sealed class Reader
        {
            public long Read, Done;
            public SafeHandle? Handle;   // the pipe, so a read still blocked once the run has returned can be cancelled
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

            // Encoded before the child starts: an encoder that throws (a strict one meeting a lone surrogate) threw on the
            // writer thread, where nothing caught it, and took the host down.
            byte[]? inputBytes = options.StandardInput != null && options.Encoding != null
                ? options.Encoding.GetBytes(options.StandardInput) // no preamble: a child reading stdin doesn't expect one
                : null;

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
            run.Out.Handle = (stdout.BaseStream as FileStream)?.SafeFileHandle;
            run.Err.Handle = (stderr.BaseStream as FileStream)?.SafeFileHandle;
            StartThread(() => ReadLines(run, run.Out, stdout, run.Output, options.OnOutputLine, run.OutputEnd), "ProcessRunner stdout");
            StartThread(() => ReadLines(run, run.Err, stderr, run.Error, options.OnErrorLine, run.ErrorEnd), "ProcessRunner stderr");

            if (options.StandardInput != null)
            {
                StreamWriter stdin = process.StandardInput;
                string text = options.StandardInput;
                StartThread(() => WriteInput(run, stdin, text, inputBytes), "ProcessRunner stdin");
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

        private static void ReadLines(RunState run, Reader progress, StreamReader reader, StringBuilder buffer, Action<string>? callback, TaskCompletionSource<bool> end)
        {
            try
            {
                // In chunks, not ReadLine: a grandchild streaming text with no line break looked quiet (nothing counted until
                // a whole line arrived), so a run with no timeout ended early and the unfinished line was lost.
                var chars = new char[4096];
                var text = new StringBuilder();
                StringBuilder? line = callback is null ? null : new StringBuilder();
                var lines = new List<string>();
                bool afterCr = false;
                int n;
                // Done: the run has returned and nothing reads what follows (see ReleaseReaders).
                while (Volatile.Read(ref run.Done) == 0 && (n = reader.Read(chars, 0, chars.Length)) > 0)
                {
                    Interlocked.Increment(ref progress.Read);
                    Interlocked.Increment(ref run.Lines);
                    text.Clear();
                    lines.Clear();
                    int start = 0;
                    for (int i = 0; i < n; i++)
                    {
                        char c = chars[i];
                        bool lf = afterCr && c == '\n'; // the second half of a \r\n, perhaps split across reads
                        afterCr = c == '\r';
                        if (c != '\r' && c != '\n') continue;
                        text.Append(chars, start, i - start);
                        line?.Append(chars, start, i - start);
                        start = i + 1;
                        if (lf) continue;
                        text.Append(Environment.NewLine);
                        if (line != null) { lines.Add(line.ToString()); line.Clear(); }
                    }
                    text.Append(chars, start, n - start);
                    line?.Append(chars, start, n - start);
                    Capture(run, buffer, text.ToString());
                    foreach (string l in lines) OnLine(run, l, callback);
                    Interlocked.Increment(ref progress.Done);
                }
                // As ReadLine: an unfinished last line is still a line.
                if (line != null && line.Length > 0)
                {
                    Interlocked.Increment(ref progress.Read);
                    OnLine(run, line.ToString(), callback);
                    Interlocked.Increment(ref progress.Done);
                }
            }
            catch (OperationCanceledException) when (Volatile.Read(ref run.Done) != 0) { /* ReleaseReaders cancelled the read */ }
            catch (IOException) { /* the pipe broke */ }
            catch (ObjectDisposedException) { /* the run already finished */ }
            catch (Exception ex)
            {
                // A decoding failure, say. The pipe is still drained: left unread, a child writing more than a pipe buffer
                // blocked for good, so the run lasted its whole timeout, or forever with none.
                Interlocked.CompareExchange(ref run.CallbackError, ex, null);
                Drain(reader.BaseStream);
            }
            finally
            {
                // Closed here, not left to the finalizer: the pipe handle stayed open as long as the process object lived.
                try { reader.Dispose(); } catch { }
                run.ReadersLeft.Signal();
                end.TrySetResult(true);
            }
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
            catch (OperationCanceledException) { } // ReleaseReaders cancelled the read
        }

        // Nothing read and no callback finished for a whole grace period: what is holding the pipes open has gone quiet.
        // A callback that finishes counts as progress, so a slow one working through a backlog keeps the run going; one
        // stuck on a single call does not (it required no callback running, so a call that took 6 s held the run 6 s,
        // and one that never returned hung it, though the docs bound that wait to a second).
        private static bool Quiet(RunState run)
        {
            long lines = Interlocked.Read(ref run.Lines);
            bool quiet = lines == run.LinesSeen;
            run.LinesSeen = lines;
            return quiet;
        }

        // Runs on the reader threads, as text arrives (an unfinished line too, so a run that ends while a grandchild is
        // mid-line keeps what it wrote).
        private static void Capture(RunState run, StringBuilder buffer, string text)
        {
            // After the run has returned nobody can read the buffer: a grandchild writing to an inherited pipe after a
            // timeout grew it without bound.
            if (Volatile.Read(ref run.Done) != 0) return;
            // Capture can fail (OutOfMemoryException past the StringBuilder's 2 GB limit); escaping here would crash the
            // host, so it is reported as a callback's exception is.
            try { lock (buffer) buffer.Append(text); }
            catch (Exception ex) { Interlocked.CompareExchange(ref run.CallbackError, ex, null); }
        }

        // Runs on the reader threads. A throwing callback must not escape there (it would crash the host), so the
        // first such exception is kept and rethrown to the caller once the run finishes.
        private static void OnLine(RunState run, string line, Action<string>? callback)
        {
            // After a timeout or a cancel the reader threads kept calling back after Run had returned. Counting the call
            // before reading Done (both full fences) means StopCallbacks either sees it running or it sees Done.
            if (callback is null) return;
            Interlocked.Increment(ref run.CallbacksRunning);
            try
            {
                if (Volatile.Read(ref run.Done) != 0) return;
                try { callback(line); }
                catch (Exception ex) { Interlocked.CompareExchange(ref run.CallbackError, ex, null); }
                Interlocked.Increment(ref run.Lines); // progress, for Quiet
                Interlocked.Increment(ref run.CallbacksDone);
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

        private static void WriteInput(RunState run, StreamWriter stdin, string text, byte[]? bytes)
        {
            try
            {
                if (bytes is null) stdin.Write(text);
                else stdin.BaseStream.Write(bytes, 0, bytes.Length);
                stdin.Close();
            }
            catch (IOException) { /* the child exited (or was killed) without reading all of it */ }
            catch (ObjectDisposedException) { /* the run already finished */ }
            catch (Exception ex)
            {
                // Escaping here would crash the host: kept and rethrown to the caller, like a callback's exception.
                Interlocked.CompareExchange(ref run.CallbackError, ex, null);
                try { stdin.Dispose(); } catch { }
            }
        }

        private static ProcessResult Finish(RunState run, bool timedOut, bool killed, ProcessRunOptions options)
        {
            StopCallbacks(run);
            ReleaseReaders(run);
            run.Stopwatch.Stop();
            // A child that exited on time but left a grandchild holding the pipes had its real exit code replaced by -1.
            int exitCode = killed ? -1 : run.Process.ExitCode;
            run.Process.Dispose();

            Exception? callbackError = Volatile.Read(ref run.CallbackError);
            if (callbackError != null) ExceptionDispatchInfo.Capture(callbackError).Throw();

            string output, error;
            lock (run.Output) output = Terminated(run.Output);
            lock (run.Error) error = Terminated(run.Error);
            var result = new ProcessResult(exitCode, output, error, run.Stopwatch.Elapsed, timedOut, run.CommandLine);
            return options.ThrowOnNonZeroExit ? result.EnsureSuccess() : result;
        }

        // Every line ends in a newline, the last one too (see ProcessResult.StandardOutput).
        private static string Terminated(StringBuilder buffer)
        {
            string text = buffer.ToString();
            return text.Length == 0 || text.EndsWith(Environment.NewLine, StringComparison.Ordinal) ? text : text + Environment.NewLine;
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

        // The output budget ran out with text already read still waiting for the callback: that time is the caller's, not
        // the child's. A child that exited in milliseconds, with a callback taking 100 ms a line for 30 lines, was
        // reported TimedOut (exit code 0) and the callback missed 12 of the lines StandardOutput held. The wait goes on
        // while the callbacks get through what was read before the budget ran out (each grace period must finish one),
        // then allows the grace period for the pipes to close. Text read after that is the child's (or a grandchild's)
        // and still counts against the timeout.
        private static bool ReadersEndWithin(RunState run, int milliseconds)
        {
            if (run.ReadersLeft.Wait(milliseconds)) return true;
            long outRead = Interlocked.Read(ref run.Out.Read), errRead = Interlocked.Read(ref run.Err.Read);
            if (!Backlog(run, outRead, errRead)) return false;
            do
            {
                long done = Interlocked.Read(ref run.CallbacksDone);
                if (run.ReadersLeft.Wait(OutputGraceMilliseconds)) return true;
                if (Interlocked.Read(ref run.CallbacksDone) == done) return false; // a callback is stuck
            } while (Backlog(run, outRead, errRead));
            return run.ReadersLeft.Wait(OutputGraceMilliseconds);
        }

        // RunAsync's ReadersEndWithin.
        private static async Task<bool> OutputClosesWithin(RunState run, int milliseconds, CancellationToken cancellationToken)
        {
            if (await CompletesWithin(run.OutputClosed, milliseconds, cancellationToken).ConfigureAwait(false)) return true;
            long outRead = Interlocked.Read(ref run.Out.Read), errRead = Interlocked.Read(ref run.Err.Read);
            if (!Backlog(run, outRead, errRead)) return false;
            do
            {
                if (cancellationToken.IsCancellationRequested) return false;
                long done = Interlocked.Read(ref run.CallbacksDone);
                if (await CompletesWithin(run.OutputClosed, OutputGraceMilliseconds, cancellationToken).ConfigureAwait(false)) return true;
                if (Interlocked.Read(ref run.CallbacksDone) == done) return false;
            } while (Backlog(run, outRead, errRead));
            return !cancellationToken.IsCancellationRequested
                && await CompletesWithin(run.OutputClosed, OutputGraceMilliseconds, cancellationToken).ConfigureAwait(false);
        }

        // Whether a reader still has lines to hand to its callback from the chunks it had read by the snapshot.
        private static bool Backlog(RunState run, long outRead, long errRead) =>
            Interlocked.Read(ref run.Out.Done) < outRead || Interlocked.Read(ref run.Err.Done) < errRead;

        // Once the run has returned nothing reads the pipes, but a grandchild holding them open kept both reader threads
        // blocked in Read, with the pipe handles, for its whole life: ten runs that each left a daemon behind took a
        // process from 7 threads to 31 (R81-2). On Windows the blocked reads are cancelled (the reader stops at Done
        // when it gets back to its loop); elsewhere a reader ends when the grandchild closes its end of the pipe.
        // Cancelling is retried for a grace period, off the caller's thread, since a reader between two reads has nothing
        // pending to cancel; one still inside a callback stops when it returns.
        private static void ReleaseReaders(RunState run)
        {
            if (run.ReadersLeft.IsSet || !RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
            Task.Run(async () =>
            {
                var wait = Stopwatch.StartNew();
                while (true)
                {
                    if (!run.OutputEnd.Task.IsCompleted) CancelRead(run.Out.Handle);
                    if (!run.ErrorEnd.Task.IsCompleted) CancelRead(run.Err.Handle);
                    if (run.ReadersLeft.IsSet || wait.ElapsedMilliseconds >= OutputGraceMilliseconds) return;
                    await Task.Delay(10).ConfigureAwait(false);
                }
            });
        }

        private static void CancelRead(SafeHandle? handle)
        {
            if (handle is null) return;
            try { CancelIoEx(handle, IntPtr.Zero); }
            catch (ObjectDisposedException) { /* the reader closed it as it ended */ }
        }

        [DllImport("kernel32.dll")]
        private static extern bool CancelIoEx(SafeHandle handle, IntPtr overlapped);

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
