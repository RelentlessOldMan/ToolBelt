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
            var running = new List<Task>();

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
                    running.Add(Task.Run(async () =>
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
                                if (!collectAllExceptions) linked.Cancel();
                            }
                        }
                        finally
                        {
                            throttle.Release();
                        }
                    }));
                }
            }
            catch
            {
                // Enumerating the source threw: stop the started bodies and wait for them before rethrowing.
                linked.Cancel();
                await Task.WhenAll(running).ConfigureAwait(false);
                throw;
            }

            await Task.WhenAll(running).ConfigureAwait(false);

            if (!exceptions.IsEmpty)
                throw new AggregateException(exceptions);
            cancellationToken.ThrowIfCancellationRequested();
        }
    }
}
