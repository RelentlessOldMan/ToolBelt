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
            var exceptions = new List<Exception>();
            foreach (var handler in Snapshot())
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    // A bare OperationCanceledException discarded the failures of the handlers before it.
                    if (exceptions.Count == 0) throw new OperationCanceledException(cancellationToken);
                    exceptions.Add(new OperationCanceledException(cancellationToken));
                    throw new AggregateException(exceptions);
                }
                try { await handler(args, cancellationToken).ConfigureAwait(false); }
                catch (Exception ex) { exceptions.Add(ex); }
            }
            if (exceptions.Count > 0) throw new AggregateException(exceptions);
        }

        /// <summary>Invokes all handlers concurrently and waits for them all.</summary>
        public async Task InvokeParallelAsync(TArgs args, CancellationToken cancellationToken = default)
        {
            var handlers = Snapshot();
            var tasks = new List<Task>(handlers.Count);
            foreach (var handler in handlers)
                tasks.Add(RunGuarded(handler, args, cancellationToken));

            try { await Task.WhenAll(tasks).ConfigureAwait(false); }
            catch { /* individual faults are collected from the tasks below */ }

            var exceptions = new List<Exception>();
            foreach (var t in tasks)
                // The handler's own exception, as InvokeSequentialAsync reports it (RunGuarded is async: exactly one inner).
                // GetBaseException also unwrapped an AggregateException the handler threw itself.
                if (t.Exception != null) exceptions.Add(t.Exception.InnerException!);
                else if (t.IsCanceled) exceptions.Add(new TaskCanceledException(t)); // as InvokeSequentialAsync reports it
            if (exceptions.Count > 0) throw new AggregateException(exceptions);
        }

        private static async Task RunGuarded(Func<TArgs, CancellationToken, Task> handler, TArgs args, CancellationToken ct)
            => await handler(args, ct).ConfigureAwait(false);

        private List<Func<TArgs, CancellationToken, Task>> Snapshot()
        {
            lock (_gate) return new List<Func<TArgs, CancellationToken, Task>>(_handlers);
        }
    }
}
