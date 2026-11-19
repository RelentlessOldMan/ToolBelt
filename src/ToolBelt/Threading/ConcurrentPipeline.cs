// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ToolBelt.Threading
{
    /// <summary>What happens when an item is offered to a full pipeline.</summary>
    public enum PipelineOverflowPolicy
    {
        /// <summary>Block the producer until space frees up.</summary>
        Block,
        /// <summary>Discard the incoming item and count it as dropped.</summary>
        DropNewest,
        /// <summary>Discard the oldest buffered item to make room.</summary>
        DropOldest,
    }

    /// <summary>
    /// A bounded producer-consumer pipeline: items are buffered up to a capacity and processed by a single
    /// background consumer, with a choice of overflow policy and a count of dropped items. Generalizes the
    /// bounded-queue-plus-background-consumer pattern (the async log sink) into a reusable primitive. The
    /// capacity counts buffered items, for every policy; the item being processed doesn't take a slot.
    /// The consumer runs on a dedicated background thread, not the thread pool: producers blocked for space (often
    /// pool threads themselves) would otherwise leave it waiting on thread injection for every item.
    /// <para>
    /// "Called from the consumer callback" below includes work the callback starts and does not await (a Task.Run, a
    /// timer), until the callback returns: such work inherits the callback's execution context, and no runtime can tell
    /// it from the callback's own awaited code, where waiting for the consumer would wait for good. From there a Block
    /// <see cref="TryEnqueue"/> on a full pipeline throws, and <see cref="CompleteAsync"/> and <see cref="Dispose"/>
    /// return without waiting for the consumer. To have such work wait like any other producer, start it under
    /// <see cref="ExecutionContext.SuppressFlow"/>, or after the callback has returned.
    /// </para>
    /// </summary>
    public sealed class ConcurrentPipeline<T> : IDisposable
    {
        private readonly Func<T, CancellationToken, Task> _consumer;
        private readonly int _capacity;
        private readonly PipelineOverflowPolicy _policy;
        private readonly Action<Exception>? _onError;
        private readonly Queue<T> _queue = new Queue<T>();
        private readonly object _gate = new object();
        private readonly SemaphoreSlim _signal = new SemaphoreSlim(0);
        private readonly SemaphoreSlim? _space;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly Task _loop;
        private long _dropped;
        private int _completed;
        private volatile bool _drained; // the consumer stopped because the buffer emptied after completion
        private int _disposed;
        [ThreadStatic] private static object? t_reporting; // the pipeline whose onError this thread is running
        // The pipeline whose consumer loop owns this thread. The loop blocks on each callback, so anything running on its
        // thread runs inside the consumer: a continuation the callback ran inline (a default TaskCompletionSource it
        // completed) has no Flow, and Dispose from it waited on its own thread for good.
        [ThreadStatic] private static object? t_loop;
        // The callback this flow belongs to: set on the consumer thread for each item, so it follows the callback across
        // awaits. A task the callback starts inherits it too, so it counts only while the callback runs: the pipeline
        // alone was marked, and CompleteAsync or Dispose from a timer or Task.Run the consumer started returned at once
        // (0 of 5 items processed), Dispose leaving its resources undisposed.
        private static readonly AsyncLocal<Flow?> s_consuming = new AsyncLocal<Flow?>();

        private sealed class Flow
        {
            public readonly ConcurrentPipeline<T> Owner;
            public volatile bool Active = true;
            public Flow(ConcurrentPipeline<T> owner) { Owner = owner; }
        }

        // Called from the consumer callback (or work it started while it runs) or from onError: waiting for the consumer
        // or for a slot it frees would wait on the caller itself.
        private bool InConsumer()
        {
            var flow = s_consuming.Value;
            return ReferenceEquals(t_reporting, this) || ReferenceEquals(t_loop, this)
                || (flow != null && flow.Active && ReferenceEquals(flow.Owner, this));
        }

        public ConcurrentPipeline(
            Func<T, CancellationToken, Task> consumer,
            int capacity,
            PipelineOverflowPolicy policy = PipelineOverflowPolicy.Block,
            Action<Exception>? onError = null)
        {
            _consumer = consumer ?? throw new ArgumentNullException(nameof(consumer));
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Capacity must be at least 1.");
            _capacity = capacity;
            _policy = policy;
            _onError = onError;
            if (policy == PipelineOverflowPolicy.Block)
                _space = new SemaphoreSlim(capacity, capacity);
            // A dedicated thread: resumed on the pool after each wait, the consumer competed with the producers it was
            // to unblock, and a starved pool (one core, pool threads blocked in TryEnqueue) gave ~2 items a second.
            _loop = Task.Factory.StartNew(() => Consume(_cts.Token), CancellationToken.None,
                TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        /// <summary>Number of items discarded due to overflow.</summary>
        public long DroppedCount { get { lock (_gate) return _dropped; } }

        /// <summary>
        /// Offers an item. Returns false if it was dropped (DropNewest on a full pipeline). For the Block
        /// policy this waits for space and always returns true; called from the consumer callback or <c>onError</c> on a
        /// full pipeline, where only the caller could free a slot, it throws <see cref="InvalidOperationException"/> instead
        /// of waiting forever. Throws <see cref="InvalidOperationException"/>
        /// once <see cref="CompleteAsync"/> has been called, and <see cref="ObjectDisposedException"/> once the pipeline is
        /// disposed, in both cases including for a producer still waiting for space.
        /// An item for which this returned true is processed unless the pipeline is disposed first, or, under
        /// <see cref="PipelineOverflowPolicy.DropOldest"/>, a later item evicts it while it is still buffered.
        /// </summary>
        public bool TryEnqueue(T item)
        {
            if (Volatile.Read(ref _completed) == 1)
                throw Completed();

            if (_policy == PipelineOverflowPolicy.Block)
            {
                // After Dispose the disposed source's Token threw ObjectDisposedException with no object name.
                if (Volatile.Read(ref _disposed) == 1) throw new ObjectDisposedException(GetType().Name);
                // Dispose cancels the wait: the producer got an OperationCanceledException for a token it never saw, where a
                // call after Dispose threw ObjectDisposedException.
                try
                {
                    // From the consumer, a full buffer never empties: a consumer re-queuing into it hung for good, and so
                    // did CompleteAsync after it.
                    if (InConsumer())
                    {
                        if (!_space!.Wait(0))
                            throw new InvalidOperationException("The pipeline is full, and waiting for space from its own consumer would never end.");
                    }
                    else _space!.Wait(_cts.Token);
                }
                catch (Exception ex) when ((ex is OperationCanceledException || ex is ObjectDisposedException) && Volatile.Read(ref _disposed) == 1)
                {
                    throw new ObjectDisposedException(GetType().Name);
                }
                lock (_gate)
                {
                    if (_completed == 1)
                    {
                        _space.Release(); // pass the slot on to the next waiting producer
                        throw Completed();
                    }
                    // Granted a slot as Dispose ran: the item was buffered for a consumer that had stopped.
                    if (Volatile.Read(ref _disposed) == 1) throw new ObjectDisposedException(GetType().Name);
                    _queue.Enqueue(item);
                }
                _signal.Release();
                return true;
            }

            bool evicted = false;
            lock (_gate)
            {
                // The drop policies never looked: after Dispose a full buffer dropped (or evicted) and returned, and a
                // call from onError returned true for an item the stopped consumer would never process.
                if (Volatile.Read(ref _disposed) == 1)
                    throw new ObjectDisposedException(GetType().Name);
                if (_completed == 1)
                    throw Completed();
                if (_queue.Count >= _capacity)
                {
                    if (_policy == PipelineOverflowPolicy.DropNewest)
                    {
                        _dropped++;
                        return false;
                    }
                    _queue.Dequeue(); // DropOldest
                    _dropped++;
                    evicted = true;
                }
                _queue.Enqueue(item);
            }
            // Replacing an item adds nothing to wake for: a release per eviction piled up one wake-up per drop.
            if (!evicted) _signal.Release();
            return true;
        }

        private static InvalidOperationException Completed() => new InvalidOperationException("The pipeline has been completed.");

        /// <summary>
        /// Signals that no more items will be added, then waits for the buffer to drain. Throws
        /// <see cref="ObjectDisposedException"/> if the pipeline was disposed before every buffered item was processed.
        /// Called from the consumer callback (a consumer that stops on a sentinel item), it marks the pipeline completed and
        /// returns without waiting: the rest of the buffer drains after the callback returns.
        /// </summary>
        public async Task CompleteAsync()
        {
            // Under the gate, so no enqueue can land after the consumer has seen "completed and empty".
            lock (_gate) Volatile.Write(ref _completed, 1);
            // Wake the consumer so it can observe completion. After a Dispose this threw the disposed semaphore's
            // ObjectDisposedException, even for a pipeline that had drained before it; the signal is never disposed now.
            _signal.Release();
            // From the consumer, waiting for the drain waited on the caller itself: it hung, and so did every later
            // CompleteAsync and Dispose.
            if (InConsumer()) return;
            await _loop.ConfigureAwait(false);
            // A Dispose racing the drain stopped the consumer with items still buffered, and this reported success.
            if (!_drained)
                throw new ObjectDisposedException(GetType().Name, "The pipeline was disposed before its buffer drained.");
        }

        private void Consume(CancellationToken ct)
        {
            t_loop = this;   // a dedicated thread, never shared, so it is not cleared
            while (true)
            {
                try { _signal.Wait(ct); }
                catch (OperationCanceledException)
                {
                    lock (_gate) _drained = _completed == 1 && _queue.Count == 0; // nothing was left to lose
                    break;
                }

                while (true)
                {
                    T item;
                    lock (_gate)
                    {
                        // Dispose cancels: the drain handed every buffered item on to a consumer that ignored the token.
                        if (ct.IsCancellationRequested) { _drained = _completed == 1 && _queue.Count == 0; return; }
                        if (_queue.Count == 0) break;
                        item = _queue.Dequeue();
                    }
                    _space?.Release(); // the slot frees when the item leaves the buffer, as for the drop policies
                    var flow = new Flow(this);
                    s_consuming.Value = flow;
                    try
                    {
                        try { _consumer(item, ct).GetAwaiter().GetResult(); }
                        catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
                        catch (Exception ex)
                        {
                            Report(ex);
                            if (ct.IsCancellationRequested) return; // onError disposed the pipeline
                        }
                    }
                    finally { flow.Active = false; }
                }

                lock (_gate)
                    if (_completed == 1 && _queue.Count == 0) { _drained = true; break; }
            }
        }

        private void Report(Exception ex)
        {
            object? outer = t_reporting;
            t_reporting = this;
            try { _onError?.Invoke(ex); }
            catch { /* a throwing error sink must not kill the consumer and strand blocked producers */ }
            finally { t_reporting = outer; }
        }

        /// <summary>
        /// Cancels processing and waits for the consumer to stop. Called from the consumer callback or from
        /// <c>onError</c> it cancels without waiting, since the consumer is the caller. For an orderly drain of buffered
        /// items, call <see cref="CompleteAsync"/> first.
        /// </summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
            // A callback the consumer registered on its token may throw here; it threw out of Dispose, skipping the wait.
            AggregateException? cancelError = null;
            try { _cts.Cancel(); }
            catch (AggregateException ex) { cancelError = ex; }
            // From onError or the callback the consumer is the caller: waiting for it deadlocked. It stops when the caller
            // returns, and its cancellation source and semaphores are disposed then.
            if (InConsumer())
                _loop.ContinueWith(_ => DisposeResources(), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            else
            {
                try { _loop.Wait(); }
                catch (AggregateException) { /* cancellation */ }
                DisposeResources();
            }
            if (cancelError != null) Report(cancelError);
        }

        // The semaphores are left undisposed: producers may still be inside their Wait, and on .NET Framework a semaphore
        // disposed under a waiter threw ArgumentNullException or left the waiter blocked for good. Without its
        // AvailableWaitHandle a SemaphoreSlim holds nothing to free.
        private void DisposeResources() => _cts.Dispose();
    }
}
