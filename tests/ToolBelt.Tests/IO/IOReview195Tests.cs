using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;
using ToolBelt.Configuration;
using ToolBelt.IO;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.IO
{
    /// <summary>
    /// Round 195 review regressions (IO, twelfth pass): each failed on the code before its fix (the rounding and word
    /// ones on .NET Framework, where the netstandard2.0 build runs).
    /// </summary>
    public sealed class IOReview195Tests
    {
        public sealed class Row
        {
            public double D { get; set; }
            public float F { get; set; }
        }

        public sealed class Settings
        {
            public double D { get; set; }
        }

        // char.IsDigit took Arabic-Indic and fullwidth digits, and BigInteger.Parse then threw out of TryParse.
        public void ByteSize_TryParse_NonAsciiDigits_ReturnFalse()
        {
            foreach (string s in new[] { "\u0663 KB", "\uFF11 KB", "1\u0663 KB", "\u0661\u0662" })
            {
                Check.False(ByteSize.TryParse(s, out long v), s);
                Check.Equal(0L, v);
                Check.Throws<FormatException>(() => ByteSize.Parse(s));
            }
            Check.True(ByteSize.TryParse("13 KB", out long kb));
            Check.Equal(13L * 1000, kb);
        }

        // "nan" and "infinity" in any case were numbers on .NET 8 and errors on .NET Framework.
        public void CsvNumbers_FloatWords_AreTheSameOnEveryRuntime()
        {
            var words = new[] { ("nan", double.NaN), ("-NaN", double.NaN), ("NAN", double.NaN), ("infinity", double.PositiveInfinity),
                ("INFINITY", double.PositiveInfinity), ("+Infinity", double.PositiveInfinity), ("-infinity", double.NegativeInfinity),
                (" Infinity ", double.PositiveInfinity), ("\u221E", double.PositiveInfinity), ("-\u221E", double.NegativeInfinity) };
            var binder = new CsvBinder<Row>(new[] { "D", "F" });
            foreach (var (text, expected) in words)
            {
                Check.True(CsvDialect.Rfc4180.TryParseNumber(text, out double d), text);
                Check.True(d.Equals(expected), text + " gave " + d.ToString("R", CultureInfo.InvariantCulture));
                Row r = binder.Bind(new[] { text, text });
                Check.True(r.D.Equals(expected), text);
                Check.True(r.F.Equals((float)expected), text);
            }
            Check.False(CsvDialect.Rfc4180.TryParseNumber("nanx", out _));
            Check.False(CsvDialect.Rfc4180.TryParseNumber("inf", out _));
        }

        // .NET Framework's parse is not correctly rounded: about 2% of long decimals came out a unit in the last place off.
        public void CsvNumbers_AreTheNearestDouble()
        {
            var rng = new Random(195);
            var binder = new CsvBinder<Row>(new[] { "D", "F" });
            int failures = 0;
            var sample = new List<string>();
            for (int trial = 0; trial < 6000; trial++)
            {
                string text = RandomDecimal(rng, out BigInteger digits, out int exp10);
                Check.True(CsvDialect.Rfc4180.TryParseNumber(text, out double d), text);
                double bound = binder.Bind(new[] { text, "0" }).D;
                bool ok = IsNearest(d, digits, exp10, 52, -1074) && bound.Equals(d);
                if (!ok && ++failures <= 5) sample.Add(text);
            }
            Check.True(failures == 0, failures + " not nearest, e.g. " + string.Join(", ", sample));
            Check.Equal(9.8598758830020559e-205, Nearest("9.8598758830020559e-205"));
            Check.Equal(double.MaxValue, Nearest("1.7976931348623158e308"));
            Check.Equal(double.PositiveInfinity, Nearest("1.7976931348623159e308"));
            Check.Equal(double.Epsilon, Nearest("2.4703282292062328e-324"));
            Check.Equal(0.0, Nearest("2.4703282292062327e-324"));
        }

        // Guard: with reading rounded right, what the binder writes still reads back bit for bit (its "R" check used the old parse).
        public void CsvBinder_WrittenFloats_ReadBackExactly()
        {
            var rng = new Random(1952);
            var binder = new CsvBinder<Row>(new[] { "D", "F" });
            for (int i = 0; i < 4000; i++)
            {
                double d = BitConverter.Int64BitsToDouble((long)(rng.NextDouble() * long.MaxValue) * (rng.Next(2) * 2 - 1));
                float f = BitConverter.ToSingle(BitConverter.GetBytes(rng.Next(int.MinValue, int.MaxValue)), 0);
                Row back = binder.Bind(binder.ToFields(new Row { D = d, F = f }));
                if (!double.IsNaN(d)) Check.Equal(BitConverter.DoubleToInt64Bits(d), BitConverter.DoubleToInt64Bits(back.D), d.ToString("G17", CultureInfo.InvariantCulture));
                if (!float.IsNaN(f)) Check.Equal(BitConverter.ToInt32(BitConverter.GetBytes(f), 0), BitConverter.ToInt32(BitConverter.GetBytes(back.F), 0), f.ToString("G9", CultureInfo.InvariantCulture));
            }
        }

        public void ConfigBinder_Double_IsTheNearestDouble()
        {
            var rng = new Random(1951);
            int failures = 0;
            string first = "";
            for (int trial = 0; trial < 6000; trial++)
            {
                string text = RandomDecimal(rng, out BigInteger digits, out int exp10);
                double d;
                try { d = ConfigBinder.Bind<Settings>(new Dictionary<string, string?> { ["D"] = text }).D; }
                catch (Exception) { continue; }                  // past double's range: an error, by design
                if (!IsNearest(d, digits, exp10, 52, -1074) && failures++ == 0) first = text;
            }
            Check.True(failures == 0, failures + " not nearest, e.g. " + first);
            Check.Equal(9.8598758830020559e-205, ConfigBinder.Bind<Settings>(new Dictionary<string, string?> { ["D"] = "9.8598758830020559e-205" }).D);
        }

        private static double Nearest(string text) => CsvDialect.Rfc4180.ParseNumber(text);

        // .NET Framework's float parse went through a double and rounded twice.
        public void CsvBinder_Float_IsTheNearestFloat()
        {
            var binder = new CsvBinder<Row>(new[] { "D", "F" });
            Check.Equal(16777218f, binder.Bind(new[] { "0", "16777217.0000000001" }).F);
            Check.Equal(1.0000001f, binder.Bind(new[] { "0", "1.0000000596046448" }).F);
            var rng = new Random(1950);
            int failures = 0;
            string first = "";
            for (int trial = 0; trial < 3000; trial++)
            {
                string text = RandomDecimal(rng, out BigInteger digits, out int exp10, 50);
                float f = binder.Bind(new[] { "0", text }).F;
                if (!IsNearest(f, digits, exp10, 23, -149) && failures++ == 0) first = text;
            }
            Check.True(failures == 0, failures + " not nearest, e.g. " + first);
        }

        // A replaced file's deletion was dropped: "D a; R c->a; D a" reported only c deleted, and a new file renamed away
        // from the name it replaced was reported as the original renamed.
        public void FileChangeCoalescer_ReplacedFiles_AreReportedGone()
        {
            var t = new DateTime(2020, 1, 1);
            var co = new FileChangeCoalescer(ignoreCase: false);
            co.Add(new FileChange(FileChangeKind.Deleted, "a"), t);
            co.Add(new FileChange(FileChangeKind.Renamed, "a", "c"), t);
            co.Add(new FileChange(FileChangeKind.Deleted, "a"), t);
            var outp = co.TakeAll();
            Check.Equal("Deleted a|Deleted c", string.Join("|", outp.Select(c => c.ToString()).OrderBy(s => s, StringComparer.Ordinal)));

            // A new file renamed away from the name it replaced is not the original renamed.
            Check.Equal("Created a|Deleted b", Coalesce(t, "D b", "C b", "R b>a"));
            Check.Equal("Created a|Deleted b|Renamed a -> c", Coalesce(t, "D c", "R a>c", "M b", "C a", "D b", "R a>b", "R b>a"));
            // A renamed file deleted while another holds its old name: the original there is gone too.
            Check.Equal("Deleted c|Renamed a -> d", Coalesce(t, "R c>d", "R a>c", "D d", "R c>d"));
            Check.Equal("Created b|Deleted c", Coalesce(t, "R c>a", "C c", "D a", "R c>b"));

            // Against a model file system: applying the output to the files before must give the files after.
            var rng = new Random(195);
            string[] names = { "a", "b", "c", "d" };
            int failures = 0;
            string firstFailure = "";
            for (int trial = 0; trial < 20000; trial++)
            {
                var fs = new Dictionary<string, (int Id, int Ver)>();
                int nextId = 1;
                foreach (var n in names) if (rng.Next(2) == 0) fs[n] = (nextId++, 0);
                var before = new Dictionary<string, (int Id, int Ver)>(fs);
                co = new FileChangeCoalescer(ignoreCase: false);
                var log = new List<string>();
                int ops = rng.Next(1, 7);
                for (int k = 0; k < ops; k++)
                {
                    t = t.AddMilliseconds(1);
                    string p = names[rng.Next(names.Length)];
                    int op = rng.Next(4);
                    if (op == 0 && !fs.ContainsKey(p)) { fs[p] = (nextId++, 0); co.Add(new FileChange(FileChangeKind.Created, p), t); log.Add("C " + p); }
                    else if (op == 1 && fs.ContainsKey(p)) { fs[p] = (fs[p].Id, fs[p].Ver + 1); co.Add(new FileChange(FileChangeKind.Changed, p), t); log.Add("M " + p); }
                    else if (op == 2 && fs.ContainsKey(p)) { fs.Remove(p); co.Add(new FileChange(FileChangeKind.Deleted, p), t); log.Add("D " + p); }
                    else if (op == 3 && fs.ContainsKey(p))
                    {
                        string q = names[rng.Next(names.Length)];
                        if (q == p) continue;
                        if (fs.ContainsKey(q)) { co.Add(new FileChange(FileChangeKind.Deleted, q), t); log.Add("D " + q); }
                        fs[q] = fs[p];
                        fs.Remove(p);
                        co.Add(new FileChange(FileChangeKind.Renamed, q, p), t);
                        log.Add("R " + p + "->" + q);
                    }
                }
                outp = co.TakeAll();
                string? err = Model(before, fs, outp);
                if (err != null && failures++ < 4)
                    firstFailure += " || [" + string.Join("; ", log) + "] gave [" + string.Join("; ", outp) + "]: " + err;
            }
            Check.True(failures == 0, failures + " failures:" + firstFailure);
        }

        // Feeds "C p", "M p", "D p" and "R old>new" events and returns the sorted output.
        private static string Coalesce(DateTime t, params string[] events)
        {
            var co = new FileChangeCoalescer(ignoreCase: false);
            foreach (string ev in events)
            {
                string p = ev.Substring(2);
                switch (ev[0])
                {
                    case 'C': co.Add(new FileChange(FileChangeKind.Created, p), t); break;
                    case 'M': co.Add(new FileChange(FileChangeKind.Changed, p), t); break;
                    case 'D': co.Add(new FileChange(FileChangeKind.Deleted, p), t); break;
                    default: co.Add(new FileChange(FileChangeKind.Renamed, p.Split('>')[1], p.Split('>')[0]), t); break;
                }
            }
            return string.Join("|", co.TakeAll().Select(c => c.ToString()).OrderBy(x => x, StringComparer.Ordinal));
        }

        // Null when applying the reported changes to the files before gives the files after.
        private static string? Model(Dictionary<string, (int Id, int Ver)> before, Dictionary<string, (int Id, int Ver)> after, IReadOnlyList<FileChange> outp)
        {
            var removed = new HashSet<string>();
            var added = new HashSet<string>();
            var changed = new HashSet<string>();
            var seen = new HashSet<string>();
            foreach (var c in outp)
            {
                if (!seen.Add(c.FullPath)) return "path reported twice: " + c.FullPath;
                switch (c.Kind)
                {
                    case FileChangeKind.Created: added.Add(c.FullPath); break;
                    case FileChangeKind.Deleted: removed.Add(c.FullPath); break;
                    case FileChangeKind.Changed: changed.Add(c.FullPath); break;
                    case FileChangeKind.Renamed:
                        if (!removed.Add(c.OldFullPath!)) return "old path removed twice";
                        added.Add(c.FullPath);
                        break;
                }
            }
            foreach (var r in removed) if (!before.ContainsKey(r)) return "removed " + r + ", which was not there before";
            // A rename onto a file there before replaces it.
            foreach (var c in outp) if (c.Kind == FileChangeKind.Renamed && before.ContainsKey(c.FullPath)) removed.Add(c.FullPath);
            foreach (var c in changed) if (!before.ContainsKey(c) || !after.ContainsKey(c)) return "Changed " + c + " not there before and after";
            var s = new HashSet<string>(before.Keys);
            s.ExceptWith(removed);
            foreach (var a in added)
            {
                if (s.Contains(a)) return "added " + a + ", which is already there";
                s.Add(a);
            }
            if (!s.SetEquals(after.Keys)) return "applying the output gives {" + string.Join(",", s.OrderBy(x => x, StringComparer.Ordinal)) + "}";
            foreach (var kv in after)
                if (!(before.TryGetValue(kv.Key, out var b) && b == kv.Value) && !seen.Contains(kv.Key)) return "content at " + kv.Key + " changed unreported";
            foreach (var c in outp.Where(c => c.Kind == FileChangeKind.Renamed))
                if (before[c.OldFullPath!].Id != after[c.FullPath].Id) return "Renamed " + c.OldFullPath + "->" + c.FullPath + " is not the same file";
            return null;
        }

        // A positive decimal with 1 to maxDigits significant digits and an exponent over the whole range (subnormals and
        // overflow included); digits * 10^exp10 is its exact value.
        private static string RandomDecimal(Random rng, out BigInteger digits, out int exp10, int maxDigits = 30)
        {
            int count = rng.Next(1, maxDigits + 1);
            var sb = new StringBuilder();
            sb.Append((char)('1' + rng.Next(9)));
            for (int i = 1; i < count; i++) sb.Append((char)('0' + rng.Next(10)));
            digits = BigInteger.Parse(sb.ToString(), CultureInfo.InvariantCulture);
            int point = rng.Next(0, count + 1);                 // digits before the point
            int e = rng.Next(-345, 330) - point;
            exp10 = e + point - count;
            string mantissa = point == count ? sb.ToString() : sb.ToString(0, point) + "." + sb.ToString(point, count - point);
            if (point == 0) mantissa = "0" + mantissa;
            return mantissa + "e" + e.ToString(CultureInfo.InvariantCulture);
        }

        // True when v is the value of its format nearest digits * 10^exp10, ties to even (infinity past the last midpoint).
        internal static bool IsNearest(double v, BigInteger digits, int exp10, int mantissaBits, int least)
        {
            if (v < 0 || double.IsNaN(v)) return false;
            long bits = mantissaBits == 52 ? BitConverter.DoubleToInt64Bits(v) : BitConverter.ToInt32(BitConverter.GetBytes((float)v), 0);
            if (bits > 0 && CompareToMidpoint(digits, exp10, bits - 1, mantissaBits, least) is int lower && (lower < 0 || (lower == 0 && (bits & 1) == 1)))
                return false;
            long infinity = mantissaBits == 52 ? 0x7FF0000000000000 : 0x7F800000;
            if (bits == infinity) return true;
            int upper = CompareToMidpoint(digits, exp10, bits, mantissaBits, least);
            return upper < 0 || (upper == 0 && (bits & 1) == 0);
        }

        // Compares digits * 10^exp10 with the midpoint between the values with bits b and b + 1.
        private static int CompareToMidpoint(BigInteger digits, int exp10, long b, int mantissaBits, int least)
        {
            Value(b, mantissaBits, least, out BigInteger m1, out int k1);
            Value(b + 1, mantissaBits, least, out BigInteger m2, out int k2);
            int k = Math.Min(k1, k2);
            BigInteger mid = (m1 << (k1 - k)) + (m2 << (k2 - k));   // the midpoint is mid * 2^(k - 1)
            k--;
            BigInteger lhs = digits * BigInteger.Pow(10, Math.Max(exp10, 0)) * BigInteger.Pow(2, Math.Max(-k, 0));
            BigInteger rhs = mid * BigInteger.Pow(2, Math.Max(k, 0)) * BigInteger.Pow(10, Math.Max(-exp10, 0));
            return lhs.CompareTo(rhs);
        }

        // The value with these bits as m * 2^k (the infinity bits read as the power of two past the largest value).
        private static void Value(long bits, int mantissaBits, int least, out BigInteger m, out int k)
        {
            long mantissa = bits & ((1L << mantissaBits) - 1);
            int exponent = (int)(bits >> mantissaBits);
            if (exponent == 0) { m = mantissa; k = least; }
            else { m = mantissa | (1L << mantissaBits); k = least + exponent - 1; }
        }
    }
}
