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
    /// bounded-queue-plus-background-consumer pattern (the async log sink) into a reusable primitive.
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
            _loop = ConsumeAsync(_cts.Token);
        }

        /// <summary>Number of items discarded due to overflow.</summary>
        public long DroppedCount { get { lock (_gate) return _dropped; } }

        /// <summary>
        /// Offers an item. Returns false if it was dropped (DropNewest on a full pipeline). For the Block
        /// policy this waits for space and always returns true.
        /// </summary>
        public bool TryEnqueue(T item)
        {
            if (Volatile.Read(ref _completed) == 1)
                throw new InvalidOperationException("The pipeline has been completed.");

            if (_policy == PipelineOverflowPolicy.Block)
            {
                _space!.Wait(_cts.Token);
                lock (_gate) _queue.Enqueue(item);
                _signal.Release();
                return true;
            }

            lock (_gate)
            {
                if (_queue.Count >= _capacity)
                {
                    if (_policy == PipelineOverflowPolicy.DropNewest)
                    {
                        _dropped++;
                        return false;
                    }
                    _queue.Dequeue(); // DropOldest
                    _dropped++;
                }
                _queue.Enqueue(item);
            }
            _signal.Release();
            return true;
        }

        /// <summary>Signals that no more items will be added, then waits for the buffer to drain.</summary>
        public async Task CompleteAsync()
        {
            Volatile.Write(ref _completed, 1);
            _signal.Release(); // wake the consumer so it can observe completion
            await _loop.ConfigureAwait(false);
        }

        private async Task ConsumeAsync(CancellationToken ct)
        {
            while (true)
            {
                try { await _signal.WaitAsync(ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }

                while (true)
                {
                    T item;
                    lock (_gate)
                    {
                        if (_queue.Count == 0) break;
                        item = _queue.Dequeue();
                    }
                    try { await _consumer(item, ct).ConfigureAwait(false); }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
                    catch (Exception ex) { _onError?.Invoke(ex); }
                    _space?.Release();
                }

                if (Volatile.Read(ref _completed) == 1)
                {
                    lock (_gate)
                        if (_queue.Count == 0) break;
                }
            }
        }

        /// <summary>
        /// Cancels processing and waits for the consumer to stop. Do not call from within the consumer
        /// callback itself. For an orderly drain of buffered items, call <see cref="CompleteAsync"/> first.
        /// </summary>
        public void Dispose()
        {
            _cts.Cancel();
            try { _loop.Wait(); }
            catch (AggregateException) { /* cancellation */ }
            _cts.Dispose();
            _signal.Dispose();
            _space?.Dispose();
        }
    }
}
