// ToolBelt drop-in — also copy Logging/Log.cs and Threading/ConcurrentPipeline.cs.
using System;
using System.Threading;
using System.Threading.Tasks;
using ToolBelt.Threading;

namespace ToolBelt.Logging
{
    /// <summary>
    /// Moves logging off the calling thread: events are buffered in a bounded queue and written to the inner
    /// sink by a single background consumer, so a slow sink (a file, a network target) never stalls the
    /// application's hot path. The overflow policy decides what happens when the buffer is full (block, or
    /// drop and count). Disposing drains the buffer first. Built on <see cref="ConcurrentPipeline{T}"/>.
    /// An event emitted from inside the inner sink on the consumer thread (for example a first-chance exception
    /// monitor reporting the sink's own failure into the same logger) is written straight to the inner sink instead
    /// of queued, since the consumer cannot wait for space that only it can free; a second level of such re-entry is
    /// dropped and counted in <see cref="DroppedCount"/>. Likewise an event emitted into a blocking sink from another
    /// async sink's consumer is written straight to the inner sink: two consumers each waiting for space in the
    /// other's full queue would never wake.
    /// </summary>
    public sealed class AsyncLogSink : ILogSink
    {
        private readonly ConcurrentPipeline<LogEvent> _pipeline;
        private readonly ILogSink _inner;
        private readonly bool _ownsInner;
        private readonly bool _blocks;
        private long _reentrantDropped;
        private int _disposed;

        // The sink whose consumer is running on this thread, and how deeply it has re-entered.
        [ThreadStatic] private static AsyncLogSink? t_consuming;
        [ThreadStatic] private static int t_depth;

        public AsyncLogSink(
            ILogSink inner,
            int capacity = 1024,
            PipelineOverflowPolicy overflowPolicy = PipelineOverflowPolicy.Block,
            Action<Exception>? onError = null,
            bool ownsInner = true)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _ownsInner = ownsInner;
            _blocks = overflowPolicy == PipelineOverflowPolicy.Block;
            // onError runs on the consumer thread too: marked as such, an event it logs is written straight through
            // (queueing could wait for space only the consumer frees) and a Dispose from it does not wait for itself.
            Action<Exception>? report = onError is null ? null : ex =>
            {
                AsyncLogSink? saved = t_consuming;
                int savedDepth = t_depth;
                t_consuming = this;
                t_depth = 1;
                try { onError(ex); }
                finally
                {
                    t_consuming = saved;
                    t_depth = savedDepth;
                }
            };
            _pipeline = new ConcurrentPipeline<LogEvent>(
                (logEvent, _) => { Consume(logEvent); return Task.CompletedTask; },
                capacity, overflowPolicy, report);
        }

        /// <summary>Number of events dropped due to overflow (only with a drop policy) or nested re-entry.</summary>
        public long DroppedCount => _pipeline.DroppedCount + Interlocked.Read(ref _reentrantDropped);

        public void Emit(LogEvent logEvent)
        {
            AsyncLogSink? consuming = t_consuming;
            if (consuming != this && (consuming == null || !_blocks))
            {
                _pipeline.TryEnqueue(logEvent);
                return;
            }
            if (consuming != this)
            {
                // Another sink's consumer (a cycle, or a chain of sinks): waiting here for space could deadlock.
                _inner.Emit(logEvent);
                return;
            }
            // Re-entered from the inner sink on our own consumer thread: queueing could wait forever for space.
            if (t_depth > 1)
            {
                Interlocked.Increment(ref _reentrantDropped);
                return;
            }
            t_depth++;
            try { _inner.Emit(logEvent); }
            finally { t_depth--; }
        }

        private void Consume(LogEvent logEvent)
        {
            AsyncLogSink? saved = t_consuming;
            int savedDepth = t_depth;
            t_consuming = this;
            t_depth = 1;
            try { _inner.Emit(logEvent); }
            finally
            {
                t_consuming = saved;
                t_depth = savedDepth;
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            if (t_consuming == this)
            {
                // Called from the inner sink or onError on the consumer thread: waiting for the drain waited for this
                // thread, forever. The consumer drains the rest once this call returns; the cleanup follows it.
                Task drained;
                try { drained = _pipeline.CompleteAsync(); }
                catch { drained = Task.CompletedTask; }
                drained.ContinueWith(_ => Close(), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
                return;
            }
            try { _pipeline.CompleteAsync().GetAwaiter().GetResult(); } // drain buffered events
            catch { /* best-effort drain */ }
            Close();
        }

        private void Close()
        {
            try { _pipeline.Dispose(); }
            finally { if (_ownsInner) _inner.Dispose(); }
        }
    }
}
