using System;
using ToolBelt.Logging;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Logging
{
    public sealed class LogFormattersTests
    {
        private static LogEvent Event(string message, LogLevel level = LogLevel.Info, string category = "cat", Exception? ex = null)
            => new LogEvent(new DateTimeOffset(2026, 9, 28, 9, 15, 0, TimeSpan.Zero), level, category, message, ex);

        public void Plain()
        {
            string s = LogFormatters.Plain(Event("hello"));
            Check.True(s.Contains("[INFO ]"), s);
            Check.True(s.Contains("cat: hello"), s);
            Check.True(s.Contains("2026-09-28T09:15:00"), s);
        }

        public void PlainIncludesException()
        {
            string s = LogFormatters.Plain(Event("failed", LogLevel.Error, ex: new InvalidOperationException("boom")));
            Check.True(s.Contains("[ERROR]"), s);
            Check.True(s.Contains("InvalidOperationException"), s);
        }

        public void Compact()
        {
            string s = LogFormatters.Compact(Event("terse"));
            Check.True(s.Contains("09:15:00"), s);
            Check.True(s.Contains("INFO"), s);
            Check.True(s.Contains("terse"), s);
        }

        public void JsonLineIsValidishAndEscaped()
        {
            string s = LogFormatters.JsonLine(Event("he said \"hi\"\tand left", LogLevel.Warning));
            Check.True(s.StartsWith("{") && s.EndsWith("}"), s);
            Check.True(s.Contains("\"level\":\"Warning\""), s);
            Check.True(s.Contains("\\\"hi\\\""), s);   // embedded quotes escaped
            Check.True(s.Contains("\\t"), s);           // tab escaped
            Check.False(s.Contains("\n"));               // single line
        }

        public void Null_Throws()
        {
            Check.Throws<ArgumentNullException>(() => LogFormatters.Plain(null!));
            Check.Throws<ArgumentNullException>(() => LogFormatters.JsonLine(null!));
        }
    }
}
