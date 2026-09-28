// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;
using System.Diagnostics;
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

        /// <summary>Text piped to the child's standard input, then the stream is closed. Null leaves stdin unused.</summary>
        public string? StandardInput { get; set; }

        /// <summary>Encoding for captured output/error. Null uses the framework default.</summary>
        public Encoding? Encoding { get; set; }

        /// <summary>If set, the child is killed (whole tree where supported) after this elapses and the result is flagged timed out.</summary>
        public TimeSpan? Timeout { get; set; }

        /// <summary>When true, a non-zero exit (or timeout) throws <see cref="ProcessRunException"/>.</summary>
        public bool ThrowOnNonZeroExit { get; set; }

        /// <summary>Invoked for each line of standard output as it arrives.</summary>
        public Action<string>? OnOutputLine { get; set; }

        /// <summary>Invoked for each line of standard error as it arrives.</summary>
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
    /// child writes more than a pipe buffer), a timeout triggers a real kill of the whole process tree where
    /// the runtime supports it, and cancellation does the same. Exact interleaving of the two streams is not
    /// guaranteed by the OS.
    /// </summary>
    public static class ProcessRunner
    {
        public static ProcessResult Run(string fileName, string? arguments = null, ProcessRunOptions? options = null)
        {
            options ??= new ProcessRunOptions();
            var run = Start(fileName, arguments, options);

            bool timedOut = false;
            if (options.Timeout.HasValue)
            {
                if (!run.Process.WaitForExit((int)options.Timeout.Value.TotalMilliseconds))
                {
                    timedOut = true;
                    KillTree(run.Process);
                }
            }
            run.Process.WaitForExit(); // flush async output handlers (and block until a killed child is gone)
            return Finish(run, timedOut, options);
        }

        public static async Task<ProcessResult> RunAsync(
            string fileName, string? arguments = null, ProcessRunOptions? options = null, CancellationToken cancellationToken = default)
        {
            options ??= new ProcessRunOptions();
            var run = Start(fileName, arguments, options);

            bool exited = await WaitForExitAsync(run.Process, options.Timeout, cancellationToken).ConfigureAwait(false);
            bool timedOut = false, canceled = false;
            if (!exited)
            {
                canceled = cancellationToken.IsCancellationRequested;
                timedOut = !canceled;
                KillTree(run.Process);
            }
            run.Process.WaitForExit(); // flush
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
            public StringBuilder Output = null!;
            public StringBuilder Error = null!;
            public Stopwatch Stopwatch = null!;
            public string CommandLine = "";
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

            var process = new DiagnosticsProcess { StartInfo = psi, EnableRaisingEvents = true };
            var output = new StringBuilder();
            var error = new StringBuilder();
            process.OutputDataReceived += (_, e) => { if (e.Data != null) { output.AppendLine(e.Data); options.OnOutputLine?.Invoke(e.Data); } };
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) { error.AppendLine(e.Data); options.OnErrorLine?.Invoke(e.Data); } };

            var sw = Stopwatch.StartNew();
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            if (options.StandardInput != null)
            {
                process.StandardInput.Write(options.StandardInput);
                process.StandardInput.Close();
            }

            return new RunState
            {
                Process = process,
                Output = output,
                Error = error,
                Stopwatch = sw,
                CommandLine = string.IsNullOrEmpty(arguments) ? fileName : fileName + " " + arguments,
            };
        }

        private static ProcessResult Finish(RunState run, bool timedOut, ProcessRunOptions options)
        {
            run.Stopwatch.Stop();
            int exitCode = timedOut ? -1 : run.Process.ExitCode;
            run.Process.Dispose();

            var result = new ProcessResult(exitCode, run.Output.ToString(), run.Error.ToString(), run.Stopwatch.Elapsed, timedOut, run.CommandLine);
            return options.ThrowOnNonZeroExit ? result.EnsureSuccess() : result;
        }

        private static async Task<bool> WaitForExitAsync(DiagnosticsProcess process, TimeSpan? timeout, CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            process.Exited += (_, __) => tcs.TrySetResult(true);
            if (process.HasExited) return true; // in case it exited before the handler attached

            Task delay = timeout.HasValue ? Task.Delay(timeout.Value, cancellationToken) : Task.Delay(Timeout.Infinite, cancellationToken);
            Task finished = await Task.WhenAny(tcs.Task, delay).ConfigureAwait(false);
            return finished == tcs.Task;
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
