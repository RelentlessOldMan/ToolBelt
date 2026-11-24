// ToolBelt drop-in — also copy Logging/Log.cs and Threading/ConcurrentPipeline.cs.
using System;
using System.Collections.Generic;
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
    /// The drainTimeout argument bounds how long Dispose waits for the buffer to drain (null waits until it has):
    /// past it, Dispose returns, the drain goes on in the background, and the inner sink is closed when it ends, so an
    /// inner sink that hangs no longer hangs Dispose.
    /// </summary>
    public sealed class AsyncLogSink : ILogSink
    {
        private readonly ConcurrentPipeline<LogEvent> _pipeline;
        private readonly ILogSink _inner;
        private readonly bool _ownsInner;
        private readonly bool _blocks;
        private long _reentrantDropped;
        private int _disposed;
        private readonly Action<Exception>? _onError;
        private readonly TimeSpan? _drainTimeout;

        // The sink whose consumer is running on this thread, and how deeply it has re-entered.
        [ThreadStatic] private static AsyncLogSink? t_consuming;
        [ThreadStatic] private static int t_depth;
        // The sinks being written straight through on this thread for another sink's consumer, innermost last.
        [ThreadStatic] private static List<AsyncLogSink>? t_direct;

        public AsyncLogSink(
            ILogSink inner,
            int capacity = 1024,
            PipelineOverflowPolicy overflowPolicy = PipelineOverflowPolicy.Block,
            Action<Exception>? onError = null,
            bool ownsInner = true,
            TimeSpan? drainTimeout = null)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            if (drainTimeout is TimeSpan dt && dt < TimeSpan.Zero && dt != Timeout.InfiniteTimeSpan)
                throw new ArgumentOutOfRangeException(nameof(drainTimeout), dt, "The drain timeout must not be negative.");
            if (drainTimeout is TimeSpan big && big.TotalMilliseconds > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(drainTimeout), big, "The drain timeout is too long; use null to wait until drained.");
            _drainTimeout = drainTimeout == Timeout.InfiniteTimeSpan ? null : drainTimeout;
            // An undefined policy acted as DropOldest without complaint.
            if (overflowPolicy != PipelineOverflowPolicy.Block && overflowPolicy != PipelineOverflowPolicy.DropOldest && overflowPolicy != PipelineOverflowPolicy.DropNewest)
                throw new ArgumentOutOfRangeException(nameof(overflowPolicy), overflowPolicy, "Unknown overflow policy.");
            _ownsInner = ownsInner;
            _onError = onError;
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
            if (logEvent is null) throw new ArgumentNullException(nameof(logEvent));   // every sink does the same
            AsyncLogSink? consuming = t_consuming;
            if (consuming != this && (consuming == null || !_blocks))
            {
                // After Dispose this threw the pipeline's InvalidOperationException ("completed"), not the
                // ObjectDisposedException the other sinks throw.
                if (Volatile.Read(ref _disposed) != 0) throw new ObjectDisposedException(nameof(AsyncLogSink));
                try { _pipeline.TryEnqueue(logEvent); }
                catch (Exception ex) when (Volatile.Read(ref _disposed) != 0 && (ex is InvalidOperationException || ex is OperationCanceledException))
                {
                    throw new ObjectDisposedException(nameof(AsyncLogSink));   // Dispose ran while this was queueing
                }
                catch (InvalidOperationException)
                {
                    // Pool work the inner sink started carries the consumer's flow, so a full blocking queue refuses it
                    // rather than wait on its own consumer; the event was lost to a raw InvalidOperationException. It
                    // is written through, as from another sink's consumer.
                    EmitDirect(logEvent);
                }
                return;
            }
            if (consuming != this)
            {
                // Another sink's consumer (a cycle, or a chain of sinks): waiting here for space could deadlock.
                EmitDirect(logEvent);
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

        private void EmitDirect(LogEvent logEvent)
        {
            // Checked for Dispose as the queued path is: the event reached an inner sink already released.
            if (Volatile.Read(ref _disposed) != 0) throw new ObjectDisposedException(nameof(AsyncLogSink));
            // Bounded as on our own consumer: an inner sink that logged its own failure back into this sink recursed
            // here until the stack overflowed, as only the consuming sink's depth was counted.
            var direct = t_direct ??= new List<AsyncLogSink>();
            int entered = 0;
            foreach (AsyncLogSink s in direct) if (s == this) entered++;
            if (entered > 1)
            {
                Interlocked.Increment(ref _reentrantDropped);
                return;
            }
            direct.Add(this);
            try { _inner.Emit(logEvent); }
            finally { direct.RemoveAt(direct.Count - 1); }
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
                // CompleteAsync from the consumer returns at once, so closing after it cancelled the events still queued
                // (one queued behind the call was lost on .NET Framework). The drain is awaited from the pool instead,
                // outside the consumer's flow, so it waits for the consumer to finish.
                Task drained;
                using (ExecutionContext.SuppressFlow()) drained = Task.Run(() => Complete());
                CloseAfter(drained);
                return;
            }
            Task drain = Complete();
            if (_drainTimeout is TimeSpan limit)
            {
                // An inner sink that hung hung Dispose with it. Past the limit the drain goes on, and closes the sink at its end.
                bool done;
                // Rounded up to a whole millisecond: Wait truncates, so a limit under 1 ms did not wait at all.
                try { done = drain.Wait(TimeSpan.FromTicks((limit.Ticks + TimeSpan.TicksPerMillisecond - 1) / TimeSpan.TicksPerMillisecond * TimeSpan.TicksPerMillisecond)); }
                catch { done = true; /* best-effort drain */ }
                if (!done) { CloseAfter(drain); return; }
            }
            else
            {
                try { drain.GetAwaiter().GetResult(); } // drain buffered events
                catch { /* best-effort drain */ }
            }
            Close();
        }

        private Task Complete()
        {
            try { return _pipeline.CompleteAsync(); }
            catch { return Task.CompletedTask; }
        }

        // A Close that throws here has no caller to reach: it goes to onError instead of becoming an unobserved task exception.
        private void CloseAfter(Task drained)
        {
            drained.ContinueWith(_ =>
            {
                try { Close(); }
                catch (Exception ex)
                {
                    try { _onError?.Invoke(ex); } catch { /* the error handler itself failed */ }
                }
            }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        }

        private void Close()
        {
            try { _pipeline.Dispose(); }
            finally { if (_ownsInner) _inner.Dispose(); }
        }
    }
}
