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
        /// then the stream is closed. Null leaves stdin unused.
        /// </summary>
        public string? StandardInput { get; set; }

        /// <summary>Encoding for captured output/error and for <see cref="StandardInput"/>. Null uses the framework default.</summary>
        public Encoding? Encoding { get; set; }

        /// <summary>
        /// If set, the child is killed (the whole tree on .NET Core 3.0+ builds, only the direct child on the
        /// netstandard2.0 build) after this elapses and the result is flagged timed out. Reading the output counts
        /// too: if a grandchild keeps the output pipes open past the timeout, the run ends timed out with the output
        /// read so far (allowing a second's grace for output still in flight). Must be between zero and
        /// <see cref="int.MaxValue"/> milliseconds; <see cref="System.Threading.Timeout.InfiniteTimeSpan"/> means no
        /// timeout. An invalid value throws before the child starts.
        /// </summary>
        public TimeSpan? Timeout { get; set; }

        /// <summary>When true, a non-zero exit (or timeout) throws <see cref="ProcessRunException"/>.</summary>
        public bool ThrowOnNonZeroExit { get; set; }

        /// <summary>
        /// Invoked for each line of standard output as it arrives, on a background thread. If it throws, the run
        /// still completes and the first such exception is rethrown to the caller.
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
        public string StandardOutput { get; }
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
    /// separate asynchronous handlers (never block-read both from one thread — the classic deadlock once a
    /// child writes more than a pipe buffer), standard input is written in the background (so a child that
    /// never reads it cannot stall the call), and a timeout or cancellation kills the child — the whole
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

            bool timedOut = false;
            if (timeoutMs < 0)
                run.Process.WaitForExit();
            else if (!run.Process.WaitForExit(timeoutMs))
            {
                timedOut = true;
                KillTree(run.Process);
            }

            // A grandchild that inherited the output pipes can hold them open after the child is gone, so the
            // wait for the rest of the output is bounded too.
            if (!run.OutputClosed.Wait(OutputBudget(run, timeoutMs, timedOut)))
                timedOut = true;
            return Finish(run, timedOut, options);
        }

        public static async Task<ProcessResult> RunAsync(
            string fileName, string? arguments = null, ProcessRunOptions? options = null, CancellationToken cancellationToken = default)
        {
            options ??= new ProcessRunOptions();
            int timeoutMs = TimeoutMilliseconds(options.Timeout);
            var run = Start(fileName, arguments, options);

            bool exited = await CompletesWithin(run.Exited, timeoutMs, cancellationToken).ConfigureAwait(false);
            bool timedOut = false, canceled = false;
            if (!exited)
            {
                canceled = cancellationToken.IsCancellationRequested;
                timedOut = !canceled;
                KillTree(run.Process);
            }

            bool closed = await CompletesWithin(
                run.OutputClosed, OutputBudget(run, timeoutMs, !exited), exited ? cancellationToken : CancellationToken.None).ConfigureAwait(false);
            if (!closed)
            {
                if (exited && cancellationToken.IsCancellationRequested) canceled = true;
                else timedOut = true;
            }

            if (canceled)
            {
                run.Process.Dispose();
                throw new OperationCanceledException(cancellationToken);
            }
            return Finish(run, timedOut, options);
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

            public Task Exited => ExitedSource.Task;
            public Task OutputClosed => Task.WhenAll(OutputEnd.Task, ErrorEnd.Task);
        }

        private static RunState Start(string fileName, string? arguments, ProcessRunOptions options)
        {
            if (fileName is null) throw new ArgumentNullException(nameof(fileName));

            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments ?? string.Empty,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = options.StandardInput != null,
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
            process.OutputDataReceived += (_, e) => OnLine(run, e.Data, run.Output, options.OnOutputLine, run.OutputEnd);
            process.ErrorDataReceived += (_, e) => OnLine(run, e.Data, run.Error, options.OnErrorLine, run.ErrorEnd);
            process.Exited += (_, __) => run.ExitedSource.TrySetResult(true);

            run.Stopwatch = Stopwatch.StartNew();
            try { process.Start(); }
            catch { process.Dispose(); throw; }
            run.Process = process;
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            if (options.StandardInput != null)
            {
                StreamWriter stdin = process.StandardInput;
                string text = options.StandardInput;
                Encoding? encoding = options.Encoding;
                Task.Run(() => WriteInput(stdin, text, encoding));
            }
            return run;
        }

        // Runs on the reader threads. A throwing callback must not escape there (it would crash the host), so the
        // first such exception is kept and rethrown to the caller once the run finishes.
        private static void OnLine(RunState run, string? line, StringBuilder buffer, Action<string>? callback, TaskCompletionSource<bool> end)
        {
            if (line is null)
            {
                end.TrySetResult(true);
                return;
            }
            lock (buffer) buffer.AppendLine(line);
            if (callback is null) return;
            try { callback(line); }
            catch (Exception ex) { Interlocked.CompareExchange(ref run.CallbackError, ex, null); }
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

        private static ProcessResult Finish(RunState run, bool timedOut, ProcessRunOptions options)
        {
            run.Stopwatch.Stop();
            int exitCode = timedOut ? -1 : run.Process.ExitCode;
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

        private static void KillTree(DiagnosticsProcess process)
        {
            try
            {
                if (process.HasExited) return;
#if NETSTANDARD2_0
                process.Kill(); // best-effort: direct child only; the platform assembly can do a true tree kill
#else
                process.Kill(entireProcessTree: true);
#endif
            }
            catch
            {
                // The process may have exited between the check and the kill, or be inaccessible; ignore.
            }
        }
    }
}
