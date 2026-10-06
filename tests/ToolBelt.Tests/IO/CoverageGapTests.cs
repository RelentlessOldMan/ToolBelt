using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ToolBelt.Cli;
using ToolBelt.Configuration;
using ToolBelt.Grids;
using ToolBelt.IO;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.IO
{
    /// <summary>Fills coverage holes found by the 2026-10-05 coverlet run (behaviour, not just line touches).</summary>
    public sealed class CoverageGapTests
    {
        public async Task CountingStream_PassThroughMembersAndAsync()
        {
            var inner = new MemoryStream();
            using var c = new CountingStream(inner, leaveOpen: true);
            Check.True(c.CanRead && c.CanWrite && c.CanSeek);
            await c.WriteAsync(new byte[] { 1, 2, 3, 4, 5 }, 0, 5);
            await c.FlushAsync();
            c.Flush();
            Check.Equal(5L, c.Length);
            c.SetLength(4);
            Check.Equal(4L, inner.Length);
            Check.Equal(0L, c.Seek(0, SeekOrigin.Begin));
            var buf = new byte[10];
            int n = await c.ReadAsync(buf, 0, 10);
            Check.Equal(4, n);
            Check.Equal(4L, c.BytesRead);
            Check.Equal(5L, c.BytesWritten);                                           // truncation doesn't un-count writes
            Check.Equal(4L, c.Position);
            Check.Throws<ArgumentNullException>(() => new CountingStream(null!));
        }

        public async Task TeeStream_AsyncReadWriteAndUnsupportedMembers()
        {
            var copy = new MemoryStream();
            using (var tee = new TeeStream(new MemoryStream(new byte[] { 9, 8, 7 }), copy, leaveCopyOpen: true))
            {
                Check.True(tee.CanRead);
                Check.False(tee.CanSeek);
                var buf = new byte[8];
                Check.Equal(3, await tee.ReadAsync(buf, 0, 8));
                Check.Equal(0, await tee.ReadAsync(buf, 0, 8));                       // EOF copies nothing
                Check.Throws<NotSupportedException>(() => _ = tee.Length);
                Check.Throws<NotSupportedException>(() => _ = tee.Position);
                Check.Throws<NotSupportedException>(() => tee.Position = 1);
                Check.Throws<NotSupportedException>(() => tee.SetLength(1));
            }
            Check.Equal("9,8,7", string.Join(",", copy.ToArray()));

            var a = new MemoryStream();
            var b = new MemoryStream();
            using (var w = new TeeStream(a, b, true, true))
            {
                Check.True(w.CanWrite);
                await w.WriteAsync(new byte[] { 1, 2 }, 0, 2);
                w.Flush();
            }
            Check.Equal("1,2", string.Join(",", a.ToArray()));
            Check.Equal("1,2", string.Join(",", b.ToArray()));
            Check.Throws<ArgumentException>(() => new TeeStream(new MemoryStream(), new MemoryStream(new byte[1], writable: false)));
        }

        public void EnvironmentVariables_RemainingReaders()
        {
            var e = EnvironmentVariables.From(new Dictionary<string, string> { ["NAME"] = "svc", ["BIG"] = "9000000000", ["FLAG"] = "off" });
            Check.Equal("svc", e.GetString("NAME", "x"));
            Check.Equal("x", e.GetString("MISSING", "x"));
            Check.Equal(9000000000L, e.GetLong("BIG", 0));
            Check.Equal(false, e.GetBoolOrNull("FLAG"));
            Check.Null(e.GetBoolOrNull("MISSING"));
            Check.Throws<ArgumentException>(() => e.Get(""));

            string prefix = "TOOLBELT_TEST_" + Guid.NewGuid().ToString("N").Substring(0, 8) + "_";
            Environment.SetEnvironmentVariable(prefix + "PORT", "8080");
            Environment.SetEnvironmentVariable(prefix + "HOST", "box");
            try
            {
                var found = EnvironmentVariables.WithPrefix(prefix);
                Check.Equal("HOST=box,PORT=8080", string.Join(",", found.Select(kv => kv.Key + "=" + kv.Value)));
                Check.True(EnvironmentVariables.WithPrefix(prefix, stripPrefix: false).ContainsKey(prefix + "PORT"));
                Check.Equal("8080", EnvironmentVariables.Process.Get(prefix + "PORT"));
            }
            finally
            {
                Environment.SetEnvironmentVariable(prefix + "PORT", null);
                Environment.SetEnvironmentVariable(prefix + "HOST", null);
            }
        }

        public void AnsiStyle_EveryAttribute()
        {
            var on = new AnsiStyle(true);
            var expected = new (Func<string, string> Style, string Code)[]
            {
                (on.Bold, "1"), (on.Dim, "2"), (on.Italic, "3"), (on.Underline, "4"), (on.Inverse, "7"),
                (on.Success, "32"), (on.Warning, "33"), (on.Error, "31"), (on.Muted, "90"),
                (t => on.Background256(t, 17), "48;5;17"), (t => on.BackgroundRgb(t, 4, 5, 6), "48;2;4;5;6"),
            };
            foreach (var (style, code) in expected) Check.Equal("\u001b[" + code + "mx\u001b[0m", style("x"), code);
            Check.Equal("", on.Bold(""));                                                  // nothing to wrap
            var console = AnsiStyle.ForConsole();                                          // decision depends on this process's env
            Check.Equal(console.Enabled ? "\u001b[1mx\u001b[0m" : "x", console.Bold("x"));
        }

        public void Cell_ValueSemantics()
        {
            var a = new Cell(2, 3);
            var (r, c) = a;
            Check.Equal((2, 3), (r, c));
            Check.True(a == new Cell(2, 3) && a != new Cell(3, 2));
            Check.True(a.Equals((object)new Cell(2, 3)) && !a.Equals("(2, 3)"));
            Check.Equal(new Cell(2, 3).GetHashCode(), a.GetHashCode());
            Check.Equal(2, new HashSet<Cell> { a, new Cell(2, 3), new Cell(3, 2) }.Count);
            Check.Equal("(2, 3)", a.ToString());
        }
    }
}
