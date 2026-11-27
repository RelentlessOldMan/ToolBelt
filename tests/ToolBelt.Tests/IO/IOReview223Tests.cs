using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
#if !NETFRAMEWORK
using System.Text.Json;
using System.Threading.Tasks;
#endif
using ToolBelt.IO;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.IO
{
    /// <summary>Regressions from review round 223 (IO, fourteenth pass): each test failed before its fix.</summary>
    public sealed class IOReview223Tests
    {
#if !NETFRAMEWORK
        // ---------- JsonLines ----------

        // The reference: records end at '\n' only (a '\r' before it dropped); blank records are skipped.
        private static List<int> Naive(string text)
        {
            var list = new List<int>();
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.EndsWith("\r", StringComparison.Ordinal) ? raw.Substring(0, raw.Length - 1) : raw;
                if (string.IsNullOrWhiteSpace(line)) continue;
                list.Add(JsonSerializer.Deserialize<Dictionary<string, int>>(line)!["a"]);
            }
            return list;
        }

        // ReadLine ended a line at a lone '\r' as well, which is JSON white space, so {"a":\r1} split in two and failed.
        public async Task JsonLines_LoneCarriageReturnInsideARecord_IsWhiteSpace()
        {
            string text = "{\"a\":\r1}\n{\"a\":2}\n";
            Check.Equal("1,2", string.Join(",", JsonLines.Read<Dictionary<string, int>>(new StringReader(text)).Select(d => d["a"])));

            var rng = new Random(223);
            string[] ws = { "", " ", "\r", "\t", "\r\r", " \r " };
            string[] ends = { "\n", "\r\n", "\n\n", "\r\n\r\n", "\n \r\n" };
            for (int trial = 0; trial < 500; trial++)
            {
                var sb = new StringBuilder();
                int count = rng.Next(1, 6);
                for (int i = 0; i < count; i++)
                {
                    string W() => ws[rng.Next(ws.Length)];
                    sb.Append(W()).Append('{').Append(W()).Append("\"a\"").Append(W()).Append(':').Append(W())
                      .Append(rng.Next(1000)).Append(W()).Append('}');
                    // The last record need not end its line; one that does may end with any of the endings.
                    if (i < count - 1 || rng.Next(2) == 0) sb.Append(ends[rng.Next(ends.Length)]);
                }
                string s = sb.ToString();
                string expected = string.Join(",", Naive(s));
                Check.Equal(expected, string.Join(",", JsonLines.Read<Dictionary<string, int>>(new StringReader(s)).Select(d => d["a"])), "sync: " + JsonSerializer.Serialize(s));
                var got = new List<int>();
                await foreach (var d in JsonLines.ReadAsync<Dictionary<string, int>>(new StringReader(s))) got.Add(d["a"]);
                Check.Equal(expected, string.Join(",", got), "async: " + JsonSerializer.Serialize(s));
            }

            // Line numbers count '\n' lines only, as an editor shows them.
            var ex = Check.Throws<JsonException>(() => JsonLines.Read<Dictionary<string, int>>(new StringReader("{\"a\":\r1}\r\n{\"a\":\r\nx")).ToList());
            Check.True(ex.Message.StartsWith("Line 2:", StringComparison.Ordinal), ex.Message);

            // Reading stops at the end of a record, as ReadLine did: what follows is still the caller's.
            var reader = new StringReader("{\"a\":1}\r\n{\"a\":2}\nrest");
            Check.Equal(1, JsonLines.Read<Dictionary<string, int>>(reader).First()["a"]);
            Check.Equal("{\"a\":2}\nrest", reader.ReadToEnd());
        }

        // 2^31 blank lines, then a bad record; nothing is allocated per line (ReadLine for the old reader, Read for the new).
        private sealed class BlankLinesThenBad : TextReader
        {
            private const long Blank = 1L << 31;
            private long _lines;
            private int _tail;
            private const string Bad = "x\n";
            public override int Read()
            {
                if (_lines < Blank) { _lines++; return '\n'; }
                return _tail < Bad.Length ? Bad[_tail++] : -1;
            }

            public override string? ReadLine()
            {
                if (_lines < Blank) { _lines++; return ""; }
                return _tail++ == 0 ? "x" : null;
            }
        }

        // The counter was an int: past 2^31 lines a bad record was reported on "Line -2147483648".
        public void JsonLines_ErrorPastTwoToThe31Lines_HasItsLineNumber()
        {
            var ex = Check.Throws<JsonException>(() => JsonLines.Read<int>(new BlankLinesThenBad()).ToList());
            Check.True(ex.Message.StartsWith("Line 2147483649:", StringComparison.Ordinal), ex.Message.Substring(0, Math.Min(60, ex.Message.Length)));
            Check.Equal(2147483648L, ex.LineNumber);
        }
#endif

        // ---------- TempDirectory ----------

        private static bool TryJunction(string link, string target)
        {
            var mk = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true });
            mk!.WaitForExit(10000);
            return Directory.Exists(link);
        }

        // DeleteTree checked subfolders for links but not the root: a TempDirectory whose folder had been swapped for a
        // junction emptied the junction's target on Dispose. Now the link alone goes.
        public void TempDirectory_RootSwappedForAJunction_LeavesTheTargetAlone()
        {
            if (!TestPlatform.IsWindows) return;
            string outside = Path.Combine(Path.GetTempPath(), "tb-r223-" + Guid.NewGuid().ToString("N"));
            string victim = Path.Combine(outside, "victim");
            Directory.CreateDirectory(Path.Combine(victim, "sub"));
            File.WriteAllText(Path.Combine(victim, "keep.txt"), "k");
            File.WriteAllText(Path.Combine(victim, "sub", "deep.txt"), "d");
            try
            {
                var td = new TempDirectory();
                Directory.Delete(td.Path, true);
                Check.True(TryJunction(td.Path, victim), "junction not made");
                td.Dispose();
                Check.False(Directory.Exists(td.Path), "the link is removed");
                Check.True(File.Exists(Path.Combine(victim, "keep.txt")), "target's file kept");
                Check.True(File.Exists(Path.Combine(victim, "sub", "deep.txt")), "target's subfolder kept");
            }
            finally { Directory.Delete(outside, true); }
        }
    }
}
