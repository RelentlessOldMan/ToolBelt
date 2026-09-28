using System;
using System.IO;
using System.Linq;
using ToolBelt.Logging;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Logging
{
    public sealed class LogSinksTests
    {
        private static LogEvent Event(string message, LogLevel level = LogLevel.Info)
            => new LogEvent(new DateTimeOffset(2026, 9, 28, 9, 15, 0, TimeSpan.Zero), level, "cat", message, null);

        public void TextWriterSinkWritesFormattedLines()
        {
            var sw = new StringWriter();
            using (var sink = new TextWriterSink(sw, LogFormatters.Compact))
            {
                sink.Emit(Event("first"));
                sink.Emit(Event("second"));
            }
            string text = sw.ToString();
            Check.True(text.Contains("first") && text.Contains("second"), text);
            int nonEmptyLines = text.Split('\n').Count(l => l.Trim().Length > 0);
            Check.Equal(2, nonEmptyLines);
        }

        public void TextWriterSinkOwnsWriterWhenAsked()
        {
            var sw = new StringWriter();
            var sink = new TextWriterSink(sw, ownsWriter: true);
            sink.Emit(Event("x"));
            sink.Dispose();
            Check.Throws<ObjectDisposedException>(() => sw.Write("after dispose"));
        }

        public void RollingMemorySinkEvictsOldest()
        {
            var sink = new RollingMemorySink(3);
            for (int i = 0; i < 5; i++) sink.Emit(Event("m" + i));
            var snap = sink.Snapshot();
            Check.Equal(3, snap.Count);
            Check.True(snap.Select(e => e.Message).SequenceEqual(new[] { "m2", "m3", "m4" }));
        }

        public void RollingMemorySinkSnapshotIsIndependent()
        {
            var sink = new RollingMemorySink(5);
            sink.Emit(Event("a"));
            var snap1 = sink.Snapshot();
            sink.Emit(Event("b"));
            Check.Equal(1, snap1.Count); // earlier snapshot unaffected by later emits
        }

        public void DelegateSinkInvokesCallback()
        {
            LogEvent? seen = null;
            var sink = new DelegateSink(e => seen = e);
            sink.Emit(Event("hi"));
            Check.Equal("hi", seen!.Message);
        }

        public void InvalidArguments_Throw()
        {
            Check.Throws<ArgumentNullException>(() => new DelegateSink(null!));
            Check.Throws<ArgumentNullException>(() => new TextWriterSink(null!));
            Check.Throws<ArgumentOutOfRangeException>(() => new RollingMemorySink(0));
        }
    }
}
