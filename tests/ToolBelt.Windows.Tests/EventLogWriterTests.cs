using System;
using System.ComponentModel;
using System.Diagnostics;
using ToolBelt.Tests.Framework;
using ToolBelt.Windows;

namespace ToolBelt.Windows.Tests
{
    public sealed class EventLogWriterTests
    {
        // Reads back the newest Application-log event from `source` as XML via wevtutil (no EventLog package needed). XML, not
        // text: an unregistered source has no message file, so the rendered description is empty but the raw string is there.
        private static string NewestEventText(string source)
        {
            var psi = new ProcessStartInfo("wevtutil.exe",
                $"qe Application /q:\"*[System[Provider[@Name='{source}']]]\" /c:1 /rd:true /f:xml")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi)!;
            string output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(10000);
            return output;
        }

        public void Write_LandsInTheApplicationLog()
        {
            string source = "ToolBeltTest";
            string marker = "toolbelt-eventlog-" + Guid.NewGuid().ToString("N");
            using (var log = new EventLogWriter(source))
                log.Warning("probe " + marker, eventId: 4242);
            string text = "";
            for (int i = 0; i < 20 && !text.Contains(marker); i++)
            {
                text = NewestEventText(source);
                if (!text.Contains(marker)) System.Threading.Thread.Sleep(100);
            }
            Check.True(text.Contains(marker), "event not found: " + text);
            Check.True(text.Contains(">4242</EventID>"), "event id");
            Check.True(text.Contains("<Level>3</Level>"), "warning level");
        }

        public void LongMessagesAreTruncatedNotRejected()
        {
            using var log = new EventLogWriter("ToolBeltTest");
            log.Information(new string('x', EventLogWriter.MaxMessageLength + 5000));
        }

        public void Validation()
        {
            Check.Throws<ArgumentException>(() => new EventLogWriter(" "));
            using var log = new EventLogWriter("ToolBeltTest");
            Check.Throws<ArgumentOutOfRangeException>(() => log.Write("x", eventId: 70000));
            log.Dispose();
            Check.Throws<ObjectDisposedException>(() => log.Write("x"));
            Check.False(EventLogWriter.IsSourceRegistered("ToolBelt-NoSuchSource-" + Guid.NewGuid().ToString("N")));
            Check.True(EventLogWriter.IsSourceRegistered("Application Error"));        // a source every Windows install has
        }
    }
}
