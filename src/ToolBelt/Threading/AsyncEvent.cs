// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ToolBelt.Threading
{
    /// <summary>
    /// An awaitable multicast event: asynchronous handlers are invoked sequentially or in parallel, and
    /// every handler's failure is captured and surfaced as an <see cref="AggregateException"/> instead of
    /// being lost the way an <c>async void</c> event handler would. Subscription is thread-safe.
    /// </summary>
    public sealed class AsyncEvent<TArgs>
    {
        private readonly List<Func<TArgs, CancellationToken, Task>> _handlers = new List<Func<TArgs, CancellationToken, Task>>();
        private readonly object _gate = new object();

        public void Subscribe(Func<TArgs, CancellationToken, Task> handler)
        {
            if (handler is null) throw new ArgumentNullException(nameof(handler));
            lock (_gate) _handlers.Add(handler);
        }

        public bool Unsubscribe(Func<TArgs, CancellationToken, Task> handler)
        {
            if (handler is null) throw new ArgumentNullException(nameof(handler));
            lock (_gate) return _handlers.Remove(handler);
        }

        public int HandlerCount
        {
            get { lock (_gate) return _handlers.Count; }
        }

        /// <summary>
        /// Invokes handlers one at a time, in subscription order. Failures are collected and thrown together as an
        /// <see cref="AggregateException"/>; a cancellation stops before the next handler, and is added to the
        /// failures already collected (or thrown alone if there are none).
        /// </summary>
        public async Task InvokeSequentialAsync(TArgs args, CancellationToken cancellationToken = default)
        {
            // As InvokeParallelAsync: with no handlers, an already-cancelled token reported success here and threw there.
            cancellationToken.ThrowIfCancellationRequested();
            var exceptions = new List<Exception>();
            bool canceled = false;
            foreach (var handler in Snapshot())
            {
                if (cancellationToken.IsCancellationRequested) { canceled = true; break; }
                try { await handler(args, cancellationToken).ConfigureAwait(false); }
                // A handler that honoured the caller's cancellation is the stop, not a failure (it was reported as both).
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { canceled = true; break; }
                catch (Exception ex) { exceptions.Add(ex); }
            }
            if (canceled)
            {
                // A bare OperationCanceledException discarded the failures of the handlers before it.
                if (exceptions.Count == 0) throw new OperationCanceledException(cancellationToken);
                exceptions.Add(new OperationCanceledException(cancellationToken));
                throw new AggregateException(exceptions);
            }
            if (exceptions.Count > 0) throw new AggregateException(exceptions);
        }

        /// <summary>
        /// Invokes all handlers concurrently and waits for them all. Failures are thrown together as an
        /// <see cref="AggregateException"/>; handlers that stopped for the caller's cancellation count as the cancellation,
        /// thrown alone as an <see cref="OperationCanceledException"/> or added once to the failures, as by
        /// <see cref="InvokeSequentialAsync"/>.
        /// </summary>
        public async Task InvokeParallelAsync(TArgs args, CancellationToken cancellationToken = default)
        {
            // As InvokeSequentialAsync: an already-cancelled token ran every handler and reported success.
            cancellationToken.ThrowIfCancellationRequested();
            var handlers = Snapshot();
            var tasks = new List<Task<Exception?>>(handlers.Count);
            foreach (var handler in handlers)
                tasks.Add(RunGuarded(handler, args, cancellationToken));
            await Task.WhenAll(tasks).ConfigureAwait(false); // RunGuarded never throws

            var exceptions = new List<Exception>();
            bool canceled = false;
            foreach (var t in tasks)
            {
                Exception? ex = t.Result;
                // A handler that honoured the caller's cancellation is the stop, not a failure: a cancel surfaced as an
                // AggregateException of TaskCanceledExceptions, where InvokeSequentialAsync threw OperationCanceledException.
                if (ex is OperationCanceledException && cancellationToken.IsCancellationRequested) canceled = true;
                else if (ex != null) exceptions.Add(ex);
            }
            if (canceled)
            {
                if (exceptions.Count == 0) throw new OperationCanceledException(cancellationToken);
                exceptions.Add(new OperationCanceledException(cancellationToken));
            }
            if (exceptions.Count > 0) throw new AggregateException(exceptions);
        }

        // The handler's own exception, as InvokeSequentialAsync catches it. Left to fault the task, an
        // OperationCanceledException made it Canceled and was replaced by a new TaskCanceledException.
        private static async Task<Exception?> RunGuarded(Func<TArgs, CancellationToken, Task> handler, TArgs args, CancellationToken ct)
        {
            try
            {
                await handler(args, ct).ConfigureAwait(false);
                return null;
            }
            catch (Exception ex) { return ex; }
        }

        private List<Func<TArgs, CancellationToken, Task>> Snapshot()
        {
            lock (_gate) return new List<Func<TArgs, CancellationToken, Task>>(_handlers);
        }
    }
}
