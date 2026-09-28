// ToolBelt drop-in — also copy ToolBelt.Threading/ConcurrentPipeline.cs.
using System;
using System.Threading.Tasks;
using ToolBelt.Threading;

namespace ToolBelt.Logging
{
    /// <summary>
    /// Moves logging off the calling thread: events are buffered in a bounded queue and written to the inner
    /// sink by a single background consumer, so a slow sink (a file, a network target) never stalls the
    /// application's hot path. The overflow policy decides what happens when the buffer is full (block, or
    /// drop and count). Disposing drains the buffer first. Built on <see cref="ConcurrentPipeline{T}"/>.
    /// </summary>
    public sealed class AsyncLogSink : ILogSink
    {
        private readonly ConcurrentPipeline<LogEvent> _pipeline;
        private readonly ILogSink _inner;
        private readonly bool _ownsInner;

        public AsyncLogSink(
            ILogSink inner,
            int capacity = 1024,
            PipelineOverflowPolicy overflowPolicy = PipelineOverflowPolicy.Block,
            Action<Exception>? onError = null,
            bool ownsInner = true)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _ownsInner = ownsInner;
            _pipeline = new ConcurrentPipeline<LogEvent>(
                (logEvent, _) => { inner.Emit(logEvent); return Task.CompletedTask; },
                capacity, overflowPolicy, onError);
        }

        /// <summary>Number of events dropped due to overflow (only with a drop policy).</summary>
        public long DroppedCount => _pipeline.DroppedCount;

        public void Emit(LogEvent logEvent) => _pipeline.TryEnqueue(logEvent);

        public void Dispose()
        {
            try { _pipeline.CompleteAsync().GetAwaiter().GetResult(); } // drain buffered events
            catch { /* best-effort drain */ }
            _pipeline.Dispose();
            if (_ownsInner) _inner.Dispose();
        }
    }
}
