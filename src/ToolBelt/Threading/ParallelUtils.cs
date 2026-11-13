// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ToolBelt.Threading
{
    /// <summary>
    /// Bounded-concurrency asynchronous fan-out with cancellation and a choice of fail-fast or
    /// collect-all-exceptions. Modern .NET has an equivalent; the older target does not, so this fills a
    /// genuine framework gap. At most <c>maxConcurrency</c> bodies run at once, and no body outlives the
    /// call: on cancellation, a failure, or an exception from enumerating the source, the bodies already
    /// started are awaited first. Cancellation by the caller surfaces as <see cref="OperationCanceledException"/>
    /// (unless a body failed for real, which surfaces as <see cref="AggregateException"/>).
    /// </summary>
    public static class ParallelUtils
    {
        public static async Task ForEachAsync<T>(
            IEnumerable<T> source,
            int maxConcurrency,
            Func<T, CancellationToken, Task> body,
            CancellationToken cancellationToken = default,
            bool collectAllExceptions = false)
        {
            if (source is null) throw new ArgumentNullException(nameof(source));
            if (body is null) throw new ArgumentNullException(nameof(body));
            if (maxConcurrency < 1) throw new ArgumentOutOfRangeException(nameof(maxConcurrency), maxConcurrency, "Must be at least 1.");

            using var throttle = new SemaphoreSlim(maxConcurrency);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var exceptions = new ConcurrentBag<Exception>();
            // Bodies in flight, plus one for the enumeration; a list of every started task grew with the source.
            int inFlight = 1;
            var allDone = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            try
            {
                foreach (T item in source)
                {
                    try
                    {
                        await throttle.WaitAsync(linked.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break; // the caller cancelled, or a sibling failed (fail-fast); sorted out below
                    }

                    T captured = item;
                    Interlocked.Increment(ref inFlight);
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await body(captured, linked.Token).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            // A cancellation once the linked token has fired (the caller's or our fail-fast) is
                            // expected; keep only real failures.
                            if (!(ex is OperationCanceledException && linked.IsCancellationRequested))
                            {
                                exceptions.Add(ex);
                                if (!collectAllExceptions) Cancel(linked, exceptions);
                            }
                        }
                        finally
                        {
                            throttle.Release();
                            if (Interlocked.Decrement(ref inFlight) == 0) allDone.TrySetResult(true);
                        }
                    });
                }
            }
            catch
            {
                // Enumerating the source threw: stop the started bodies and wait for them before rethrowing. A callback a body
                // registered on its token that threw here replaced the source's exception and skipped the wait.
                Cancel(linked, exceptions);
                if (Interlocked.Decrement(ref inFlight) == 0) allDone.TrySetResult(true);
                await allDone.Task.ConfigureAwait(false);
                throw;
            }

            if (Interlocked.Decrement(ref inFlight) == 0) allDone.TrySetResult(true);
            await allDone.Task.ConfigureAwait(false);

            if (!exceptions.IsEmpty)
                throw new AggregateException(exceptions);
            cancellationToken.ThrowIfCancellationRequested();
        }

        // Cancels the bodies' token. A callback a body registered on it may throw: that is the body's failure, collected
        // with the others (it escaped a body's task unobserved, or replaced the source's exception).
        private static void Cancel(CancellationTokenSource linked, ConcurrentBag<Exception> exceptions)
        {
            try { linked.Cancel(); }
            catch (AggregateException ex) { foreach (var inner in ex.InnerExceptions) exceptions.Add(inner); }
        }
    }
}
