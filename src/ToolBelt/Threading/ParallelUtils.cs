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
    /// genuine framework gap. At most <c>maxConcurrency</c> bodies run at once.
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

            foreach (T item in source)
            {
                try
                {
                    await throttle.WaitAsync(linked.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    if (cancellationToken.IsCancellationRequested) throw; // external cancellation
                    break; // fail-fast: a sibling already failed
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
                        // Ignore cancellations we induced ourselves for fail-fast; keep real failures.
                        bool selfInduced = ex is OperationCanceledException
                            && linked.IsCancellationRequested
                            && !cancellationToken.IsCancellationRequested;
                        if (!selfInduced)
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

            await Task.WhenAll(running).ConfigureAwait(false);

            if (!exceptions.IsEmpty)
                throw new AggregateException(exceptions);
            cancellationToken.ThrowIfCancellationRequested();
        }
    }
}
