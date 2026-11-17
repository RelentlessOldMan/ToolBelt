using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using ToolBelt.Cli;
using ToolBelt.Diagnostics;
using ToolBelt.Logging;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Logging
{
    /// <summary>
    /// Regressions from review round 116 (seventh pass over Logging, Diagnostics, Cli): each test failed before its fix,
    /// but the guards.
    /// </summary>
    public sealed class LoggingReview116Tests
    {
        private static LogEvent Ev(string message) =>
            new LogEvent(new DateTimeOffset(2026, 11, 17, 9, 0, 0, TimeSpan.Zero), LogLevel.Info, "c", message, null);

        // ---------- ConsoleSpinner ----------

        // The columns the spinner counted for "| " + message: drawn, then replaced by "x", whose padding covers the rest.
        private static int Counted(string message)
        {
            var w = new StringWriter();
            using (var s = new ConsoleSpinner(message, w, interactive: true, frames: new[] { "|" }, interval: TimeSpan.FromHours(1)))
            {
                s.Message = "x";
                string all = w.ToString();
                string last = all.Substring(all.LastIndexOf("| x", StringComparison.Ordinal));
                return 3 + last.Length - last.TrimEnd(' ').Length;
            }
        }

        private static string Fit(string line, int columns) =>
            (string)typeof(ConsoleSpinner).GetMethod("Fit", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new object[] { line, columns })!;

        // Small and vertical forms, Hangul Jamo Extended-A, an emoji-presentation heart, Tangut and the kana supplement
        // counted one column each, so the line was measured short and Fit let it wrap.
        public void SpinnerCountsWideCharactersAsTwoColumns()
        {
            string R(string c, int n) => string.Concat(Enumerable.Repeat(c, n));
            foreach (var (text, columns) in new[]
            {
                (R("\uFE50", 10), 20), (R("\uFE10", 3), 6), (R("\uA960", 3), 6), ("\u2764\uFE0F", 2),
                ("\U00017000", 2), ("\U0001B000", 2), ("\U0001B170", 2),
            })
                Check.Equal(2 + columns, Counted(text), string.Join(" ", text.Select(c => ((int)c).ToString("X4"))));
            Check.Equal(R("\uFE50", 5), Fit(R("\uFE50", 10), 10));
        }

        // Guard: narrow text, marks and a heart without the selector keep their widths.
        public void SpinnerNarrowCharactersKeepTheirWidths()
        {
            foreach (var (text, columns) in new[] { ("abc", 3), ("\u2764", 1), ("e\u0301", 1), ("\U0001D400", 1), ("\u4E00", 2), ("ab\uFE0F", 2) })
                Check.Equal(2 + columns, Counted(text), text);
        }

        // ESC became a space, so an AnsiStyle message showed "[32m" as text and was counted 15 columns for 6.
        public void SpinnerKeepsColourSequencesAndCountsThemAsNothing()
        {
            string green = new AnsiStyle(true).Success("done");
            Check.True(green.Contains("\u001B["), "AnsiStyle(true) wrote no escape sequence");
            var w = new StringWriter();
            using (new ConsoleSpinner(green, w, interactive: true, frames: new[] { "|" }, interval: TimeSpan.FromHours(1)))
                Check.True(w.ToString().Contains("| " + green), w.ToString().Replace("\u001B", "<ESC>"));
            Check.Equal(6, Counted(green));
            Check.Equal("\u001B[32mabc\u001B[0m", Fit("\u001B[32mabcdef\u001B[0m", 3));
        }

        // Guard: any other control sequence is still flattened (it could move the cursor off the line).
        public void SpinnerStillBlanksOtherControlSequences()
        {
            var w = new StringWriter();
            using (new ConsoleSpinner("a\u001B[2Jb\u001B[1Ac", w, interactive: true, frames: new[] { "|" }, interval: TimeSpan.FromHours(1)))
                Check.True(w.ToString().Contains("| a [2Jb [1Ac"), w.ToString().Replace("\u001B", "<ESC>"));
            Check.Equal("abc", Fit("abc\u001B[", 5).Substring(0, 3));
        }

        private sealed class BrokenWriter : TextWriter
        {
            public override Encoding Encoding => Encoding.UTF8;
            public override void Write(char value) => throw new IOException("pipe closed");
            public override void Write(string? value) => throw new IOException("pipe closed");
            public override void Flush() => throw new IOException("pipe closed");
        }

        // Tick drew without the guard the constructor and Message setter use, and threw on a closed pipe.
        public void SpinnerTickOnABrokenWriterDoesNotThrow()
        {
            using (var s = new ConsoleSpinner("x", new BrokenWriter(), interactive: true, interval: TimeSpan.FromHours(1)))
            {
                s.Tick();
                s.Tick();
            }
        }

        // ---------- ConsoleTable ----------

        public void TableRejectsAnUndefinedAlignment()
        {
            var t = new ConsoleTable("a", "b");
            var ex = Check.Throws<ArgumentOutOfRangeException>(() => t.AlignColumn(0, (ColumnAlignment)7));
            Check.Equal("alignment", ex.ParamName);
            t.AlignColumn(1, ColumnAlignment.Right);
        }

        // ---------- ExceptionUtils.IsTransient ----------

        // A WebResponse whose StatusCode is of the given enum type, the shape the status is read by.
        private sealed class FtpResponse : WebResponse
        {
            public FtpResponse(FtpStatusCode code) { StatusCode = code; }
            public FtpStatusCode StatusCode { get; }
        }

        private sealed class HttpResponse : WebResponse
        {
            public HttpResponse(HttpStatusCode code) { StatusCode = code; }
            public HttpStatusCode StatusCode { get; }
        }

        private sealed class OtherResponse : WebResponse
        {
            public DayOfWeek StatusCode => DayOfWeek.Friday;
        }

        private static WebException Web(WebResponse response) =>
            new WebException("x", null, WebExceptionStatus.ProtocolError, response);

        // An FTP reply code was read as an HTTP status: 550 (file unavailable) was transient, 450 (busy) and 425 were not.
        public void IsTransientReadsFtpRepliesAsFtp()
        {
            foreach (var code in new[] { FtpStatusCode.ActionNotTakenFileUnavailable, FtpStatusCode.NotLoggedIn, FtpStatusCode.CommandSyntaxError })
                Check.False(ExceptionUtils.IsTransient(Web(new FtpResponse(code))), code.ToString());
            foreach (var code in new[] { FtpStatusCode.ActionNotTakenFileUnavailableOrBusy, FtpStatusCode.CantOpenData, FtpStatusCode.ServiceNotAvailable })
                Check.True(ExceptionUtils.IsTransient(Web(new FtpResponse(code))), code.ToString());
        }

        // Guard: HTTP responses keep their rule, and a status of any other type is unknown (transient, as a WebException).
        public void IsTransientHttpResponsesKeepTheirRule()
        {
            Check.False(ExceptionUtils.IsTransient(Web(new HttpResponse(HttpStatusCode.NotFound))));
            Check.True(ExceptionUtils.IsTransient(Web(new HttpResponse(HttpStatusCode.RequestTimeout))));
            Check.True(ExceptionUtils.IsTransient(Web(new HttpResponse(HttpStatusCode.ServiceUnavailable))));
            Check.True(ExceptionUtils.IsTransient(Web(new OtherResponse())));
            Check.True(ExceptionUtils.IsTransient(new WebException("no response")));
        }

        // Programmer errors were skipped, so what they wrapped or sat beside decided.
        public void IsTransientProgrammerErrorsAnywhereAreNot()
        {
            Check.False(ExceptionUtils.IsTransient(new ArgumentException("bad", new IOException("io"))));
            Check.False(ExceptionUtils.IsTransient(new InvalidOperationException("state", new TimeoutException())));
            Check.False(ExceptionUtils.IsTransient(new AggregateException(new ArgumentException("bad"), new TimeoutException())));
            Check.False(ExceptionUtils.IsTransient(new AggregateException(new TimeoutException(), new NullReferenceException())));
            Check.False(ExceptionUtils.IsTransient(new IOException("io", new ArgumentNullException("p"))));
            // Guards: transient failures alone, and wrapped by a neutral exception.
            Check.True(ExceptionUtils.IsTransient(new TimeoutException()));
            Check.True(ExceptionUtils.IsTransient(new Exception("wrap", new IOException("io"))));
            Check.True(ExceptionUtils.IsTransient(new AggregateException(new TimeoutException(), new OperationCanceledException())));
        }

        // ---------- Logger ----------

        private sealed class Counting : ILogSink
        {
            public int Emitted, Disposed;
            public void Emit(LogEvent logEvent) => Emitted++;
            public void Dispose() => Disposed++;
        }

        // A sink added after Dispose was kept, written to and never disposed (a FileSink's handle leaked).
        public void LoggerRefusesSinksAfterDispose()
        {
            var parent = new Logger();
            var before = parent.ForCategory("before");
            parent.Dispose();
            var after = parent.ForCategory("after");
            foreach (var lg in new[] { parent, before, after })
            {
                var sink = new Counting();
                Check.Throws<ObjectDisposedException>(() => lg.AddSink(sink));
                lg.Info("dropped");
                Check.Equal(0, sink.Emitted);
            }
            parent.Dispose();
        }

        // Guard: a child's Dispose leaves the shared sinks alone, and the parent still takes sinks and disposes them once.
        public void LoggerChildDisposeLeavesTheParentWorking()
        {
            var parent = new Logger();
            var child = parent.ForCategory("c");
            child.Dispose();
            var sink = new Counting();
            parent.AddSink(sink);
            child.Info("one");
            parent.Dispose();
            parent.Dispose();
            Check.Equal(1, sink.Emitted);
            Check.Equal(1, sink.Disposed);
        }

        // ---------- TextWriterSink ----------

        private sealed class BrokenStream : Stream
        {
            public override bool CanRead => false;
            public override bool CanSeek => false;
            public override bool CanWrite => true;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override void Flush() => throw new IOException("broken pipe");
            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new IOException("broken pipe");
        }

        // The guarded Flush failed, then the owned writer's Dispose flushed again and threw out of the sink's Dispose.
        public void TextWriterSinkDisposeOnABrokenOwnedWriterDoesNotThrow()
        {
            var sink = new TextWriterSink(new StreamWriter(new BrokenStream()), ownsWriter: true);
            sink.Emit(Ev("buffered"));
            sink.Dispose();
            sink.Dispose();
            Check.Throws<ObjectDisposedException>(() => sink.Emit(Ev("late")));
        }

        // ---------- FileSink ----------

        // A rotation blocked by a viewer, then a reopen refused (the file read-only): Emit threw after writing the event,
        // and a caller that retried wrote it twice.
        public void FileSinkFailedReopenAfterAWrittenEventDoesNotThrow()
        {
            string dir = Path.Combine(Path.GetTempPath(), "toolbelt-r116-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "app.log");
            FileStream? viewer = null;
            try
            {
                var sink = new FileSink(path, e => e.Message, maxBytes: 10, maxFiles: 3);
                sink.Emit(Ev("a"));
                viewer = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                File.SetAttributes(path, FileAttributes.ReadOnly);
                sink.Emit(Ev("second-line-past-limit"));
                viewer.Dispose();
                viewer = null;
                File.SetAttributes(path, FileAttributes.Normal);
                sink.Emit(Ev("third"));
                sink.Dispose();
                var lines = File.ReadAllLines(path).Concat(Directory.GetFiles(dir, "app.log.*").SelectMany(File.ReadAllLines)).ToList();
                Check.Equal(1, lines.Count(l => l == "second-line-past-limit"));
                Check.Equal(1, lines.Count(l => l == "third"));
            }
            finally
            {
                viewer?.Dispose();
                foreach (string f in Directory.GetFiles(dir)) File.SetAttributes(f, FileAttributes.Normal);
                Directory.Delete(dir, true);
            }
        }
    }
}
