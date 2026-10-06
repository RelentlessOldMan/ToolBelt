using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using ToolBelt.Configuration;
using ToolBelt.IO;
using ToolBelt.Security;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.IO
{
    /// <summary>File retention, stream wrappers, CSV dialects, environment variables and hashing additions.</summary>
    public sealed class FilesWaveTests
    {
        // ---------- retention ----------

        private static readonly DateTime Now = new DateTime(2026, 3, 10, 12, 0, 0, DateTimeKind.Utc);

        private static void Make(TempDirectory tmp, string name, int ageHours, int bytes)
        {
            string p = tmp.Combine(name);
            File.WriteAllBytes(p, new byte[bytes]);
            File.SetLastWriteTimeUtc(p, Now.AddHours(-ageHours));
        }

        public void Prune_ByCountAgeAndSize()
        {
            using var tmp = new TempDirectory();
            for (int i = 0; i < 6; i++) Make(tmp, $"log{i}.txt", ageHours: i * 24, bytes: 100);
            Make(tmp, "keep.dat", 1000, 5);                                          // doesn't match the pattern

            var dry = FileRetention.WhatIf(tmp.Path, "*.txt", new RetentionPolicy { MaxCount = 4 });
            Check.Equal(2, dry.Deleted.Count);
            Check.Equal(6, Directory.GetFiles(tmp.Path, "*.txt").Length);             // dry run deleted nothing

            var byAge = FileRetention.Prune(tmp.Path, "*.txt", new RetentionPolicy { MaxAge = TimeSpan.FromDays(3.5) }, Now);
            Check.Equal("log4.txt,log5.txt", string.Join(",", byAge.Deleted.Select(Path.GetFileName).OrderBy(n => n)));
            Check.Equal(200L, byAge.BytesFreed);

            var bySize = FileRetention.Prune(tmp.Path, "*.txt", new RetentionPolicy { MaxTotalBytes = 250 }, Now);
            Check.Equal("log0.txt,log1.txt", string.Join(",", bySize.Kept.Select(Path.GetFileName).OrderBy(n => n)));
            Check.True(File.Exists(tmp.Combine("keep.dat")));
        }

        public void Prune_KeepAtLeastWins()
        {
            using var tmp = new TempDirectory();
            for (int i = 0; i < 3; i++) Make(tmp, $"c{i}.bin", 1000 + i, 10);
            var r = FileRetention.Prune(tmp.Path, "*.bin", new RetentionPolicy { MaxAge = TimeSpan.FromHours(1), KeepAtLeast = 2 }, Now);
            Check.Equal(1, r.Deleted.Count);
            Check.Equal("c2.bin", Path.GetFileName(r.Deleted[0]));
            Check.Equal(0, FileRetention.Prune(Path.Combine(tmp.Path, "missing"), "*", new RetentionPolicy { MaxCount = 0 }).Deleted.Count);
        }

        public void ShiftNumbered_RotatesAndTrims()
        {
            using var tmp = new TempDirectory();
            string log = tmp.Combine("app.log");
            for (int gen = 1; gen <= 4; gen++)
            {
                File.WriteAllText(log, "gen" + gen);
                Check.True(FileRetention.ShiftNumbered(log, keep: 2));
            }
            Check.False(File.Exists(log));
            Check.Equal("gen4", File.ReadAllText(log + ".1"));
            Check.Equal("gen3", File.ReadAllText(log + ".2"));
            Check.False(File.Exists(log + ".3"));
            Check.False(FileRetention.ShiftNumbered(log, 2));
        }

        public void TimestampedName_SortsAndAvoidsCollisions()
        {
            using var tmp = new TempDirectory();
            var t = new DateTime(2026, 3, 5, 14, 22, 33, 120);
            Check.Equal("capture_20260305_142233.csv", FileRetention.TimestampedName("capture", "csv", t));
            Check.Equal("20260305_142233_120.bin", FileRetention.TimestampedName("", ".bin", t, milliseconds: true));
            File.WriteAllText(tmp.Combine("x_20260305_142233.txt"), "");
            Check.Equal("x_20260305_142233_2.txt", FileRetention.TimestampedName("x", "txt", t, directory: tmp.Path));
        }

        // ---------- streams ----------

        public void CountingStream_CountsBothWays()
        {
            var ms = new MemoryStream();
            using (var c = new CountingStream(ms, leaveOpen: true))
            {
                c.Write(new byte[100], 0, 100);
                c.WriteAsync(new byte[50], 0, 50).GetAwaiter().GetResult();
                c.Position = 0;
                var buf = new byte[64];
                while (c.Read(buf, 0, buf.Length) > 0) { }
                Check.Equal(150L, c.BytesWritten);
                Check.Equal(150L, c.BytesRead);
            }
            Check.True(ms.CanRead, "leaveOpen honoured");
        }

        public void TeeStream_CopiesWhatIsRead()
        {
            byte[] data = Enumerable.Range(0, 10000).Select(i => (byte)(i * 7)).ToArray();
            var copy = new MemoryStream();
            using (var tee = new TeeStream(new MemoryStream(data), copy, leaveCopyOpen: true))
            using (var sha = SHA256.Create())
            {
                byte[] viaTee = sha.ComputeHash(tee);                                // hash while copying
                Check.Equal(Hashing.ToHex(Hashing.Sha256(data)), Hashing.ToHex(viaTee));
            }
            Check.Equal(Convert.ToBase64String(data), Convert.ToBase64String(copy.ToArray()));

            var a = new MemoryStream();
            var b = new MemoryStream();
            using (var w = new TeeStream(a, b, true, true)) w.Write(new byte[] { 1, 2, 3 }, 0, 3);
            Check.Equal(3L, a.Length);
            Check.Equal(3L, b.Length);
            Check.Throws<NotSupportedException>(() => new TeeStream(new MemoryStream(), new MemoryStream()).Seek(0, SeekOrigin.Begin));
        }

        // ---------- CSV dialects ----------

        public void Dialect_DetectsCommonVariants()
        {
            var us = CsvDialect.Detect(new[] { "time,value,label", "0.5,1.25,a", "1.0,2.5,\"b, c\"" });
            Check.Equal(',', us.Delimiter);
            Check.Equal('.', us.DecimalSeparator);
            Check.True(us.HasHeader);

            var eu = CsvDialect.Detect(new[] { "Zeit;Wert", "0,5;1,25", "1,0;2,5" });
            Check.Equal(';', eu.Delimiter);
            Check.Equal(',', eu.DecimalSeparator);
            Check.Close(1.25, eu.ParseNumber(eu.ParseLine("0,5;1,25")[1]));

            var tab = CsvDialect.Detect(new[] { "1\t2\t3", "4\t5\t6" });
            Check.Equal('\t', tab.Delimiter);
            Check.False(tab.HasHeader);
            Check.Equal('|', CsvDialect.Detect(new[] { "a|b", "c|d" }).Delimiter);
        }

        public void Dialect_NumbersRoundTrip()
        {
            Check.Equal("3,25", CsvDialect.ExcelEuropean.FormatNumber(3.25));
            Check.Equal(3.25, CsvDialect.ExcelEuropean.ParseNumber(" 3,25 "));
            Check.False(CsvDialect.ExcelEuropean.TryParseNumber("1.234,5", out _));  // no guessing at thousands separators
            Check.Equal("a;\"b;c\"", CsvDialect.ExcelEuropean.FormatLine(new[] { "a", "b;c" }));
            var rng = new ToolBelt.Numerics.DeterministicRandom(1);
            for (int i = 0; i < 200; i++)
            {
                double v = (rng.NextDouble() - 0.5) * Math.Pow(10, rng.Next(-8, 9));
                Check.Equal(v, CsvDialect.ExcelEuropean.ParseNumber(CsvDialect.ExcelEuropean.FormatNumber(v)));
            }
            Check.Throws<ArgumentException>(() => new CsvDialect(',', ','));
        }

        // ---------- environment variables ----------

        private static EnvironmentVariables Env(params (string, string)[] vars) => EnvironmentVariables.From(vars.ToDictionary(v => v.Item1, v => v.Item2));

        public void Env_TypedReads()
        {
            var e = Env(("PORT", "8081"), ("VERBOSE", "yes"), ("TIMEOUT", "1.5m"), ("POLL", "250ms"), ("EMPTY", ""), ("LIST", "a, b;;c"),
                ("LEVEL", "warning"), ("RATIO", "0.25"), ("SPAN", "00:00:05"));
            Check.Equal(8081, e.GetInt("PORT", 1));
            Check.Equal(1, e.GetInt("MISSING", 1));
            Check.Equal(7, e.GetInt("EMPTY", 7));                                     // empty = unset
            Check.True(e.GetBool("VERBOSE"));
            Check.Equal(TimeSpan.FromSeconds(90), e.GetTimeSpan("TIMEOUT", TimeSpan.Zero));
            Check.Equal(TimeSpan.FromMilliseconds(250), e.GetTimeSpan("POLL", TimeSpan.Zero));
            Check.Equal(TimeSpan.FromSeconds(5), e.GetTimeSpan("SPAN", TimeSpan.Zero));
            Check.Equal("a|b|c", string.Join("|", e.GetList("LIST")));
            Check.Equal(Level.Warning, e.GetEnum("LEVEL", Level.Info));
            Check.Equal(0.25, e.GetDouble("RATIO", 0));
        }

        private enum Level { Info, Warning }

        public void Env_MalformedValuesThrowNamingTheVariable()
        {
            var e = Env(("PORT", "80x"), ("FLAG", "maybe"), ("T", "5 parsecs"), ("L", "loud"));
            var ex = Check.Throws<FormatException>(() => e.GetInt("PORT", 1));
            Check.True(ex.Message.Contains("PORT") && ex.Message.Contains("80x"), ex.Message);
            Check.Throws<FormatException>(() => e.GetBool("FLAG"));
            Check.Throws<FormatException>(() => e.GetTimeSpan("T", TimeSpan.Zero));
            Check.Throws<FormatException>(() => e.GetEnum("L", Level.Info));
            foreach (string bad in new[] { "NaNs", "NaNms", "-5s", "Infinitym" })
                Check.Throws<FormatException>(() => Env(("T", bad)).GetTimeSpan("T", TimeSpan.Zero), bad);
            var missing = Check.Throws<InvalidOperationException>(() => e.RequireAll("PORT", "A", "B"));
            Check.True(missing.Message.Contains("A, B"), missing.Message);
            Check.Equal("80x", e.Require("PORT"));
        }

        // ---------- hashing ----------

        public void Hashing_FileAndIncrementalAgree()
        {
            using var tmp = new TempDirectory();
            byte[] data = Enumerable.Range(0, 300000).Select(i => (byte)(i % 251)).ToArray();
            string path = tmp.Combine("blob.bin");
            File.WriteAllBytes(path, data);
            string expected = Hashing.ToHex(Hashing.Sha256(data));
            Check.Equal(expected, Hashing.Sha256FileHex(path));
            Check.Equal(Hashing.ToHex(Hashing.Sha512(data)), Hashing.ToHex(Hashing.Sha512File(path)));
            using var inc = Hashing.CreateIncremental();
            for (int off = 0; off < data.Length; off += 7777) inc.AppendData(data, off, Math.Min(7777, data.Length - off));
            Check.Equal(expected, Hashing.ToHex(inc.GetHashAndReset()));
            Check.Equal("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", Hashing.ToHex(inc.GetHashAndReset()));   // empty
        }
    }
}
