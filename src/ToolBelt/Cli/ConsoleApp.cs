// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ToolBelt.Cli
{
    /// <summary>
    /// Conventional process exit codes: 0/1/2 as everyone uses them, the BSD <c>sysexits.h</c> range for specific
    /// failures, and 130 for "interrupted by Ctrl+C" (128 + SIGINT) so shells and CI recognise a cancel.
    /// </summary>
    public static class ExitCodes
    {
        public const int Success = 0;
        public const int Failure = 1;

        /// <summary>Command-line usage error (bad arguments).</summary>
        public const int Usage = 2;

        public const int DataError = 65;
        public const int NoInput = 66;
        public const int Unavailable = 69;
        public const int Software = 70;
        public const int IOError = 74;
        public const int TempFailure = 75;
        public const int NoPermission = 77;
        public const int Config = 78;

        /// <summary>Interrupted (Ctrl+C).</summary>
        public const int Interrupted = 130;
    }

    /// <summary>Throw from a command to exit with a specific code and a message for the user (no stack trace).</summary>
    public sealed class ExitException : Exception
    {
        public ExitException(int exitCode, string message) : base(message) => ExitCode = exitCode;

        public int ExitCode { get; }
    }

    /// <summary>
    /// The <c>Main</c> wrapper every console tool rewrites: runs the body with a cancellation token that Ctrl+C trips
    /// (the first Ctrl+C requests a graceful stop; a second one lets the process die as usual), and turns the outcome into
    /// an exit code — the body's own return, <see cref="ExitCodes.Interrupted"/> for a cancel, an <see cref="ExitException"/>'s
    /// code and message, <see cref="ExitCodes.Usage"/> for an <see cref="ArgumentException"/>, and
    /// <see cref="ExitCodes.Software"/> for anything unexpected, written to stderr as one line (full details with
    /// <c>verbose</c>). Usage: <c>static Task&lt;int&gt; Main(string[] a) =&gt; ConsoleApp.RunAsync(ct =&gt; Run(a, ct));</c>
    /// </summary>
    public static class ConsoleApp
    {
        public static Task<int> RunAsync(Func<CancellationToken, Task<int>> body, TextWriter? error = null, bool verbose = false)
            => RunAsync(body, error, verbose, hookConsole: true, externalToken: default);

        /// <summary>Synchronous body.</summary>
        public static int Run(Func<CancellationToken, int> body, TextWriter? error = null, bool verbose = false)
        {
            if (body is null) throw new ArgumentNullException(nameof(body));
            return RunAsync(ct => Task.FromResult(body(ct)), error, verbose).GetAwaiter().GetResult();
        }

        /// <summary>Testable core: no console hook; <paramref name="externalToken"/> stands in for Ctrl+C.</summary>
        public static async Task<int> RunAsync(Func<CancellationToken, Task<int>> body, TextWriter? error, bool verbose, bool hookConsole, CancellationToken externalToken)
        {
            if (body is null) throw new ArgumentNullException(nameof(body));
            TextWriter err = error ?? Console.Error;
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
            ConsoleCancelEventHandler? handler = null;
            if (hookConsole)
            {
                handler = (_, e) =>
                {
                    if (cts.IsCancellationRequested) return;       // second Ctrl+C: let the runtime terminate
                    e.Cancel = true;
                    cts.Cancel();
                };
                Console.CancelKeyPress += handler;
            }
            try
            {
                return await body(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested)
            {
                err.WriteLine("Cancelled.");
                return ExitCodes.Interrupted;
            }
            catch (ExitException ex)
            {
                if (!string.IsNullOrEmpty(ex.Message)) err.WriteLine(ex.Message);
                return ex.ExitCode;
            }
            catch (ArgumentException ex)
            {
                err.WriteLine("error: " + ex.Message);
                return ExitCodes.Usage;
            }
            catch (Exception ex)
            {
                err.WriteLine(verbose ? "error: " + ex : $"error: {ex.Message} ({ex.GetType().Name})");
                return ExitCodes.Software;
            }
            finally
            {
                if (handler != null) Console.CancelKeyPress -= handler;
            }
        }

        /// <summary>A token that trips on the first Ctrl+C (which is then suppressed); dispose to unhook.</summary>
        public static CancellationTokenSource CreateInterruptSource()
        {
            var cts = new InterruptSource();
            return cts;
        }

        private sealed class InterruptSource : CancellationTokenSource
        {
            private readonly ConsoleCancelEventHandler _handler;

            public InterruptSource()
            {
                _handler = (_, e) =>
                {
                    if (IsCancellationRequested) return;
                    e.Cancel = true;
                    try { Cancel(); } catch (ObjectDisposedException) { }
                };
                Console.CancelKeyPress += _handler;
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing) Console.CancelKeyPress -= _handler;
                base.Dispose(disposing);
            }
        }
    }
}
