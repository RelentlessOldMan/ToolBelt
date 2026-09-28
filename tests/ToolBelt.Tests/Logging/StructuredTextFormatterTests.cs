using System;
using ToolBelt.Logging;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Logging
{
    public sealed class StructuredTextFormatterTests
    {
        private static LogEvent Ev(string message, LogLevel level = LogLevel.Warning, string category = "net")
            => new LogEvent(new DateTimeOffset(2026, 9, 28, 9, 15, 0, TimeSpan.Zero), level, category, message, null);

        public void CustomFieldsAndDelimiter()
        {
            var fmt = new StructuredTextFormatter(new[] { LogField.Level, LogField.Category, LogField.Message }, delimiter: " | ");
            Check.Equal("Warning | net | hello", fmt.Format(Ev("hello")));
        }

        public void DelimitedPresetIsTabSeparated()
        {
            string line = StructuredTextFormatter.Delimited().Format(Ev("msg"));
            string[] parts = line.Split('\t');
            Check.Equal(4, parts.Length);
            Check.Equal("Warning", parts[1]);
            Check.Equal("net", parts[2]);
            Check.Equal("msg", parts[3]);
        }

        public void SanitizesDelimiterAndNewlinesInValues()
        {
            var fmt = StructuredTextFormatter.Delimited(); // tab delimiter
            string line = fmt.Format(Ev("a\tb\nc"));
            Check.Equal(4, line.Split('\t').Length); // embedded tab/newline replaced -> field count unchanged
            Check.False(line.Contains("\n"));
        }

        public void TimestampFormatHonored()
        {
            var fmt = new StructuredTextFormatter(new[] { LogField.Timestamp }, timestampFormat: "yyyy-MM-dd");
            Check.Equal("2026-09-28", fmt.Format(Ev("x")));
        }

        public void UsableAsSinkFormatter()
        {
            var captured = new System.Collections.Generic.List<LogEvent>();
            var fmt = StructuredTextFormatter.Delimited();
            using var writer = new System.IO.StringWriter();
            using var sink = new TextWriterSink(writer, fmt.Format);
            sink.Emit(Ev("routed"));
            Check.True(writer.ToString().Contains("routed"));
        }

        public void InvalidArguments_Throw()
        {
            Check.Throws<ArgumentException>(() => new StructuredTextFormatter(Array.Empty<LogField>()));
            Check.Throws<ArgumentNullException>(() => new StructuredTextFormatter(null!));
        }
    }
}
