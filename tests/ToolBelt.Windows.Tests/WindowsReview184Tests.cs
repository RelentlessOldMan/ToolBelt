using System;
using System.Collections.Concurrent;
using ToolBelt.Tests.Framework;
using ToolBelt.Windows;

namespace ToolBelt.Windows.Tests
{
    // Review round 184 (Windows satellite, pass 11) regressions.
    public sealed class WindowsReview184Tests
    {
        // The command line is a C string: "a\0b c" read back as just "a", losing the rest.
        public void JoinArguments_WithANul_Throws()
        {
            var ex = Check.Throws<ArgumentException>(() => RunElevated.JoinArguments(new[] { "a\0b", "c" }));
            Check.Equal("arguments", ex.ParamName);
            Check.Equal("a b", RunElevated.JoinArguments(new[] { "a", "b" }));
        }

        // The shell cut the file name at a NUL (here a missing file, so the old code failed with Win32Exception and never
        // prompted); the arguments and working directory are cut the same way.
        public void Start_WithANul_ThrowsBeforeTheShell()
        {
            var ex = Check.Throws<ArgumentException>(() => RunElevated.Start("ToolBeltReview184-missing\0.exe"));
            Check.Equal("fileName", ex.ParamName);
            ex = Check.Throws<ArgumentException>(() => RunElevated.Start("ToolBeltReview184-missing.exe", "a\0b"));
            Check.Equal("arguments", ex.ParamName);
            ex = Check.Throws<ArgumentException>(() => RunElevated.Start("ToolBeltReview184-missing.exe", null, "C:\\x\0"));
            Check.Equal("workingDirectory", ex.ParamName);
        }

        // A zero forward timeout gave each attempt 1 ms: one that fell between two clients, while the listener built its
        // next pipe, lost the launch to a primary that was up and ready (about 1 in 75).
        public void Start_WithAZeroTimeout_ReachesAReadyPrimary()
        {
            string id = "ToolBeltReview184-" + Guid.NewGuid().ToString("N");
            using var primary = SingleInstanceApp.Start(id, Array.Empty<string>());
            Check.True(primary.IsPrimary);
            var got = new ConcurrentQueue<string>();
            primary.SecondInstanceStarted += (_, e) => got.Enqueue(e.Args[0]);
            int lost = 0;
            string detail = "";
            for (int i = 0; i < 400; i++)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                using var other = SingleInstanceApp.Start(id, new[] { "launch" + i }, TimeSpan.Zero);
                Check.False(other.IsPrimary);
                if (!other.ForwardedToPrimary) { lost++; detail += $" {i}@{sw.ElapsedMilliseconds}ms"; }
            }
            Check.Equal(0, lost, detail);
        }
    }
}
