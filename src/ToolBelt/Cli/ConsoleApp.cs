// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.IO;
using System.Text;
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
        /// <param name="exitCode">The process exit code.</param>
        /// <param name="message">What to tell the user; null or empty prints nothing (null printed the BCL's default
        /// "Exception of type ... was thrown.").</param>
        public ExitException(int exitCode, string? message) : base(message ?? string.Empty) => ExitCode = exitCode;

        public int ExitCode { get; }
    }

    /// <summary>
    /// The <c>Main</c> wrapper every console tool rewrites: runs the body with a cancellation token that Ctrl+C trips
    /// (the first Ctrl+C requests a graceful stop; a second one lets the process die as usual), and turns the outcome into
    /// an exit code — the body's own return, <see cref="ExitCodes.Interrupted"/> for a cancel, an <see cref="ExitException"/>'s
    /// code and message, <see cref="ExitCodes.Usage"/> for an <see cref="ArgumentException"/>, and
    /// <see cref="ExitCodes.Software"/> for anything unexpected, written to stderr as one line (full details with
    /// <c>verbose</c>). A cancellation that surfaces wrapped in an <see cref="AggregateException"/> (from
    /// <c>Task.Wait</c> or <c>.Result</c>) also counts as an interrupt. Only Ctrl+C / Ctrl+Break is hooked: a SIGTERM
    /// or a closing console window ends the process without tripping the token.
    /// Usage: <c>static Task&lt;int&gt; Main(string[] a) =&gt; ConsoleApp.RunAsync(ct =&gt; Run(a, ct));</c>
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
                    try { cts.Cancel(); }
                    catch (ObjectDisposedException) { }   // Ctrl+C racing with RunAsync unwinding
                    // A token callback the body registered threw: out of the CancelKeyPress handler, that ended the process.
                    catch (AggregateException ex) { TryWrite(err, "A cancellation callback failed: " + Text(ex, () => Unwrap(ex).Message)); }
                };
                Console.CancelKeyPress += handler;
            }
            try
            {
                return await body(cts.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (cts.IsCancellationRequested && IsCancellation(ex))
            {
                TryWrite(err, "Cancelled.");
                return ExitCodes.Interrupted;
            }
            catch (Exception thrown)
            {
                // Classified by what the body threw: one wrapped by Task.Wait or .Result (an AggregateException with a
                // single inner) was reported as 70 even for an ExitException or ArgumentException.
                Exception ex = Unwrap(thrown);
                if (ex is ExitException exit)
                {
                    string message = Text(exit, () => exit.Message);
                    if (message.Length > 0) TryWrite(err, message);
                    return exit.ExitCode;
                }
                if (ex is ArgumentException)
                {
                    TryWrite(err, "error: " + Text(ex, () => ex.Message));
                    return ExitCodes.Usage;
                }
                TryWrite(err, verbose ? "error: " + Text(ex, () => Describe(ex)) : $"error: {Text(ex, () => ex.Message)} ({ex.GetType().Name})");
                return ExitCodes.Software;
            }
            finally
            {
                if (handler != null) Console.CancelKeyPress -= handler;
            }
        }

        // A closed or broken stderr (a pipe whose reader exited) threw out of RunAsync and the exit code was lost.
        private static void TryWrite(TextWriter err, string line)
        {
            try { err.WriteLine(line); }
            catch (Exception) { /* any failure: the exit code still has to come back */ }
        }

        private static Exception Unwrap(Exception ex)
        {
            if (ex is AggregateException agg)
            {
                var inner = agg.Flatten().InnerExceptions;
                if (inner.Count == 1) return inner[0];
            }
            return ex;
        }

        // An exception whose ToString throws (a broken override) escaped RunAsync. ToString recurses once per
        // level of inner exceptions and copies the inner text at each: a chain 20,000 deep overflowed the stack (which
        // no catch stops) and one 10,000 deep took 12 s, and a cycle made by reflection overflowed too. Up to 64 levels
        // (and 10,000 exceptions) keep the runtime's own text; past that, each of the first 256 exceptions gets its type,
        // message and stack, walked without recursion.
        private static string Describe(Exception ex)
        {
            var walk = new System.Collections.Generic.Stack<(Exception Exception, int Depth)>();
            walk.Push((ex, 0));
            bool deep = false;
            for (int visits = 0; walk.Count > 0 && !deep; visits++)
            {
                var (e, depth) = walk.Pop();
                if (depth > 64 || visits == 10_000) deep = true;
                else if (e is AggregateException agg) foreach (Exception inner in agg.InnerExceptions) walk.Push((inner, depth + 1));
                else if (e.InnerException != null) walk.Push((e.InnerException, depth + 1));
            }
            if (!deep)
            {
                try { return ex.ToString(); }
                catch (Exception inner) { return ex.GetType().FullName + " (its ToString threw " + inner.GetType().FullName + ")"; }
            }
            var seen = new System.Collections.Generic.List<Exception>();
            var todo = new System.Collections.Generic.Stack<Exception>();
            todo.Push(ex);
            bool again = false, more = false;
            while (todo.Count > 0)
            {
                Exception e = todo.Pop();
                if (seen.Exists(s => ReferenceEquals(s, e))) { again = true; continue; }
                if (seen.Count == 256) { more = true; break; }
                seen.Add(e);
                if (e is AggregateException agg)
                {
                    var inner = agg.InnerExceptions;
                    for (int i = inner.Count - 1; i >= 0; i--) todo.Push(inner[i]);
                }
                else if (e.InnerException != null) todo.Push(e.InnerException);
            }
            var sb = new StringBuilder();
            for (int i = 0; i < seen.Count; i++)
            {
                Exception e = seen[i];
                if (i > 0) sb.Append(System.Environment.NewLine).Append(" ---> ");
                sb.Append(e.GetType().FullName).Append(": ");
                try { sb.Append(e.Message); }
                catch (Exception inner) { sb.Append("(its Message threw ").Append(inner.GetType().FullName).Append(')'); }
                string? trace;
                try { trace = e.StackTrace; }
                catch (Exception) { trace = null; }
                if (!string.IsNullOrEmpty(trace)) sb.Append(System.Environment.NewLine).Append(trace);
            }
            if (again) sb.Append(System.Environment.NewLine).Append(" ---> (an exception shown above appears again)");
            if (more) sb.Append(System.Environment.NewLine).Append(" ---> (more inner exceptions not shown)");
            return sb.ToString();
        }

        // An exception whose Message or ToString throws escaped RunAsync, and the exit code was lost.
        private static string Text(Exception ex, Func<string?> read)
        {
            try { return read() ?? string.Empty; }
            catch (Exception) { return "<" + ex.GetType().Name + ": the message could not be read>"; }
        }

                private static bool IsCancellation(Exception ex)
        {
            if (ex is OperationCanceledException) return true;
            if (ex is AggregateException agg)
            {
                var inner = agg.Flatten().InnerExceptions;
                if (inner.Count == 0) return false;
                foreach (Exception e in inner)
                    if (!(e is OperationCanceledException)) return false;
                return true;
            }
            return false;
        }

        /// <summary>
        /// A token that trips on the first Ctrl+C (which is then suppressed); dispose to unhook. An exception thrown by a
        /// callback registered on the token is swallowed: it would otherwise end the process from the Ctrl+C handler.
        /// </summary>
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
                    try { Cancel(); }
                    catch (ObjectDisposedException) { }
                    catch (AggregateException) { /* a registered callback threw: out of this handler it ended the process */ }
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
