using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using ToolBelt.Configuration;
using ToolBelt.Documents;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Documents
{
    /// <summary>Regressions from review round 217 (documents and configuration, thirteenth pass): each test failed before its fix.</summary>
    public sealed class DocumentsConfigReview217Tests
    {
        private static readonly Encoding Latin1 = Encoding.GetEncoding("ISO-8859-1");

        // Every drawn string with its x position (written to 3 decimals), the WinAnsi bullet and dash mapped back.
        private static List<(double X, string Text)> Drawn(PdfWriter w)
        {
            string pdf = Latin1.GetString(w.Build());
            var list = new List<(double, string)>();
            foreach (Match m in Regex.Matches(pdf, @"([-0-9.]+) [-0-9.]+ Td \(((?:[^()\\]|\\.)*)\) Tj"))
            {
                string t = Regex.Replace(m.Groups[2].Value, @"\\(.)", "$1").Replace('\u0095', '•').Replace('\u0096', '–');
                list.Add((double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), t));
            }
            return list;
        }

        // ---------- EnvironmentVariables.GetDouble ----------

        // .NET Framework's parse is not correctly rounded: "9.8598758830020559e-205" came out a unit in the last place
        // off, where ConfigBinder read the same text right. Graded against the exact midpoint between two neighbouring
        // doubles: decimals just below and just above it, over random magnitudes (subnormals included).
        public void EnvironmentVariables_GetDouble_IsCorrectlyRounded()
        {
            var values = new Dictionary<string, string> { ["X"] = "9.8598758830020559e-205" };
            Check.Equal(0x1593c8df2d8c79ffL, BitConverter.DoubleToInt64Bits(EnvironmentVariables.From(values).GetDouble("X", 0)));
            var rng = new Random(217);
            for (int trial = 0; trial < 4000; trial++)
            {
                long bits = trial % 10 == 0 ? (long)(rng.NextDouble() * (1L << 52)) : (long)(rng.NextDouble() * 0x4330000000000000L);
                bits = Math.Max(1, bits);
                double d = BitConverter.Int64BitsToDouble(bits), u = BitConverter.Int64BitsToDouble(bits + 1);
                int exp = (int)((bits >> 52) & 0x7FF);
                BigInteger mant = (bits & 0xFFFFFFFFFFFFFL) | (exp == 0 ? 0 : 1L << 52);
                int k = 1075 - Math.Max(exp, 1) + 1;                  // midpoint = (2 mant + 1) * 2^-k = digits * 10^-k
                string digits = ((2 * mant + 1) * BigInteger.Pow(5, k)).ToString(CultureInfo.InvariantCulture);
                int p = Math.Min(digits.Length - 1, 17 + rng.Next(14));
                bool above = rng.Next(2) == 0;
                string kept = digits.Substring(0, p);
                if (above) kept = (BigInteger.Parse(kept, CultureInfo.InvariantCulture) + 1).ToString(CultureInfo.InvariantCulture);
                int e10 = digits.Length - 1 - k + (kept.Length - p);
                bool negative = rng.Next(2) == 0;
                string text = (negative ? "-" : "") + kept.Substring(0, 1) + "." + kept.Substring(1) + "e" + e10.ToString(CultureInfo.InvariantCulture);
                double expected = (above ? u : d) * (negative ? -1 : 1);
                var env = EnvironmentVariables.From(new Dictionary<string, string> { ["X"] = text });
                Check.Equal(BitConverter.DoubleToInt64Bits(expected), BitConverter.DoubleToInt64Bits(env.GetDouble("X", 0)), text);
            }
        }

        // ---------- PdfWriter wrapping ----------

        // Two or more spaces at a break started the next line a space or more right of the margin ("( bbb)" after 84
        // Courier characters), and trailing spaces added a near-blank line.
        public void Pdf_SpacesAtABreak_DoNotIndentTheNextLine()
        {
            var w = new PdfWriter(font: PdfFont.Courier, fontSize: 10).Paragraph(new string('a', 84) + "  bbb");
            var drawn = Drawn(w);
            Check.Equal(2, drawn.Count);
            Check.Equal("bbb", drawn[1].Text);
            Check.Equal(1, Drawn(new PdfWriter(font: PdfFont.Courier, fontSize: 10).Lines(new[] { new string('a', 84) + "   " })).Count);

            var rng = new Random(2170);
            for (int trial = 0; trial < 400; trial++)
            {
                var words = new List<string>();
                var sb = new StringBuilder();
                int count = rng.Next(1, 40);
                for (int i = 0; i < count; i++)
                {
                    string word = new string((char)('a' + rng.Next(26)), rng.Next(1, 30));
                    words.Add(word);
                    sb.Append(word).Append(' ', rng.Next(1, 6));
                }
                if (rng.Next(2) == 0) sb.Length -= 1;
                var font = (PdfFont)rng.Next(3);
                double size = 6 + rng.NextDouble() * 30;
                var pw = new PdfWriter(font: font, fontSize: size);
                bool table = rng.Next(3) == 0;
                if (table) pw.Table(new[] { "H" }, new[] { new[] { sb.ToString() } });
                else pw.Lines(new[] { sb.ToString() });
                var lines = Drawn(pw).Select(x => x.Text).Where(t => !table || t != "H").ToList();   // the header repeats on each page
                string at = font + " " + size.ToString("R", CultureInfo.InvariantCulture) + (table ? " table" : "") + ": " + sb;
                foreach (string line in lines)
                {
                    Check.False(line.StartsWith(" ", StringComparison.Ordinal), at + " -> [" + line + "]");
                    Check.True(line.Trim().Length > 0, at + " -> a blank line");
                }
                // Nothing lost: the words come back, a hard-split word in pieces.
                string back = string.Concat(lines.SelectMany(l => l.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)));
                Check.Equal(string.Concat(words), back, at);
            }
        }

        // ---------- PdfWriter.BulletList ----------

        // The nested indent plus the bullet was never capped against the line: in Courier 150 the level-1 "B" started at
        // x = 564, past the 558 margin, and ran off the page. Every drawn string stays inside the margins, over random
        // fonts, sizes (up to where a bullet and the widest letter fit on the line) and levels.
        public void Pdf_BulletList_NestedItemsStayInsideTheMargins()
        {
            var big = new PdfWriter(font: PdfFont.Courier, fontSize: 150).BulletList(new[] { (0, "A"), (1, "B") });
            foreach (var (x, t) in Drawn(big)) Check.True(x >= 54 && x + big.Measure(t, 150) <= 558 + 1e-3, t + " at " + x);

            var rng = new Random(2171);
            for (int trial = 0; trial < 300; trial++)
            {
                var font = (PdfFont)rng.Next(3);
                double size = Math.Pow(10, 0.7 + rng.NextDouble() * 1.7);    // 5 to 250 points
                var items = new List<(int, string)>();
                int count = rng.Next(1, 6);
                for (int i = 0; i < count; i++)
                {
                    var words = Enumerable.Range(0, rng.Next(1, 4)).Select(_ => new string((char)('A' + rng.Next(26)), rng.Next(1, 8)));
                    items.Add((rng.Next(0, 9), string.Join(" ", words)));
                }
                var w = new PdfWriter(font: font, fontSize: size).BulletList(items);
                string at = font + " " + size.ToString("R", CultureInfo.InvariantCulture) + " " + string.Join(", ", items);
                foreach (var (x, t) in Drawn(w))
                    Check.True(x >= 54 - 1e-3 && x + w.Measure(t, size) <= 558 + 1e-3, at + ": " + t + " at " + x + " to " + (x + w.Measure(t, size)));
            }
        }

        // ---------- Durations ----------

        public sealed class Timed
        {
            public TimeSpan T { get; set; }
        }

        private static long? EnvTicks(string text)
        {
            try { return EnvironmentVariables.From(new Dictionary<string, string> { ["X"] = text }).GetTimeSpan("X")?.Ticks; }
            catch (FormatException) { return null; }
        }

        private static long? BinderTicks(string text)
        {
            try { return ConfigBinder.Bind<Timed>(new Dictionary<string, string?> { ["T"] = text }).T.Ticks; }
            catch (ConfigBindingException) { return null; }
        }

        // Past 7 fractional digits the runtimes disagreed: .NET read "00:00:00.00000005" as 1 tick and refused
        // ".12345678", .NET Framework read the first as 5 ticks and refused others. The fraction is truncated to whole
        // ticks, as the unit form is; graded against the unit form of the same duration over random fractions.
        public void Duration_ColonFormPastSevenDigits_TruncatesToTicks()
        {
            Check.Equal(0L, EnvTicks("00:00:00.00000005"));
            Check.Equal(0L, EnvTicks("00:00:00.00000009"));
            Check.Equal(1234567L, EnvTicks("0:0:0.12345678"));
            Check.Equal(0L, BinderTicks("0.0:0:0.000000001"));
            var rng = new Random(2172);
            for (int trial = 0; trial < 2000; trial++)
            {
                int digits = rng.Next(1, 16);
                var frac = new StringBuilder();
                for (int i = 0; i < digits; i++) frac.Append((char)('0' + rng.Next(10)));
                int sec = rng.Next(60), min = rng.Next(60), hour = rng.Next(24), day = rng.Next(0, 1000);
                bool negative = rng.Next(4) == 0;
                string colon = (negative ? "-" : "") + day + "." + hour.ToString("00", CultureInfo.InvariantCulture) + ":" + min.ToString("00", CultureInfo.InvariantCulture)
                    + ":" + sec.ToString("00", CultureInfo.InvariantCulture) + "." + frac;
                string units = (negative ? "-" : "") + day + "d " + hour + "h " + min + "m " + sec + "." + frac + "s";
                long? expected = BinderTicks(units);
                Check.True(expected != null, units);
                Check.Equal(expected, BinderTicks(colon), colon);
                // EnvironmentVariables takes no negative duration.
                Check.Equal(negative ? null : expected, EnvTicks(colon), colon);
            }
        }

        // ---------- PdfWriter.Code ----------

        // Code counted UTF-16 units against the columns: 80 emoji (one '?' each) wrapped onto two lines where 83
        // characters fit on one. Graded against the column count over random mixes of pairs and plain characters.
        public void Pdf_Code_CountsASurrogatePairAsOneColumn()
        {
            Check.Equal(1, Drawn(new PdfWriter().Code(string.Concat(Enumerable.Repeat("\U0001F600", 80)))).Count);
            var rng = new Random(2174);
            for (int trial = 0; trial < 300; trial++)
            {
                var sb = new StringBuilder();
                int n = rng.Next(1, 400);
                for (int i = 0; i < n; i++) sb.Append(rng.Next(3) == 0 ? "\U0001F600" : ((char)('a' + rng.Next(26))).ToString());
                double size = 4 + rng.NextDouble() * 20;
                var w = new PdfWriter();
                int perLine = (int)Math.Floor((612 - 108 - 10) / (size * 0.6));
                var lines = Drawn(w.Code(sb.ToString(), size)).Select(x => x.Text).ToList();
                string at = size.ToString("R", CultureInfo.InvariantCulture) + " " + n;
                Check.Equal((n + perLine - 1) / perLine, lines.Count, at);
                for (int i = 0; i < lines.Count; i++) Check.Equal(i < lines.Count - 1 ? perLine : n - perLine * (lines.Count - 1), lines[i].Length, at);
            }
        }

        // ---------- ConfigBinder.Bind ----------

        public sealed class Throwing
        {
            public Throwing() => throw new InvalidOperationException("no");
        }

        // The root's constructor ran through Activator, so its exception escaped as a TargetInvocationException.
        public void Bind_RootConstructorThrows_ItsOwnExceptionEscapes()
        {
            var ex = Check.Throws<InvalidOperationException>(() => ConfigBinder.Bind<Throwing>(new Dictionary<string, string?>()));
            Check.Equal("no", ex.Message);
        }

        // ---------- HtmlReport ----------

        // A language with whitespace became several classes: "c sharp onclick=x" was language-c, sharp and onclick=x.
        public void Html_CodeBlockLanguage_IsOneClassToken()
        {
            string html = new HtmlReport().CodeBlock("x", "c sharp\tonclick=x").ToString();
            Check.True(html.Contains("<code class=\"language-c-sharp-onclick=x\">"), html);
            Check.True(new HtmlReport().CodeBlock("x", "csharp").ToString().Contains("<code class=\"language-csharp\">"));
            Check.True(new HtmlReport().CodeBlock("x").ToString().Contains("<pre><code>x</code></pre>"));
        }

        // The MIME type was not checked: null made "data:;base64,..." and other text a broken URI.
        public void Html_ImageData_ChecksTheMimeType()
        {
            var bytes = new byte[] { 1 };
            Check.Equal("mimeType", Check.Throws<ArgumentNullException>(() => new HtmlReport().ImageData("a", bytes, null!)).ParamName);
            foreach (string bad in new[] { "", "image", "/png", "image/", "text/html\" onerror=\"x", "image/png;x", "image png", "a/b/c" })
                Check.Equal("mimeType", Check.Throws<ArgumentException>(() => new HtmlReport().ImageData("a", bytes, bad)).ParamName, bad);
            foreach (string good in new[] { "image/png", "image/svg+xml", "image/vnd.microsoft.icon", "IMAGE/JPEG" })
                Check.True(new HtmlReport().ImageData("a", bytes, good).ToString().Contains("src=\"data:" + good + ";base64,AQ==\""), good);
        }

        // ---------- ConfigLayers ----------

        // Key identity came from the runtime's ordinal ignore-case: .NET matched µ/Μ, ς/Σ and ǅ/Ǆ, .NET Framework did
        // not, so one source loaded on one runtime and was refused on the other. The fixed rule gives the same answer on
        // both (this test runs on both).
        public void ConfigLayers_KeyCasing_IsTheSameOnEveryRuntime()
        {
            (string, string, bool)[] pairs =
            {
                ("port", "PORT", true), ("café", "CAFÉ", true), ("ÿ", "Ÿ", true), ("σ", "Σ", true),
                ("ж", "Ж", true), ("ա", "Ա", true), ("ａ", "Ａ", true), ("ǆ", "Ǆ", true),
                ("µ", "Μ", false), ("ς", "Σ", false), ("ǅ", "Ǆ", false), ("ſ", "S", false),
                ("K", "k", false), ("ı", "I", false), ("ა", "Ა", false), ("ɐ", "Ɐ", false),
                ("ͅ", "Ι", false),
            };
            foreach (var (a, b, same) in pairs)
            {
                string at = a + "/" + b;
                var src = new Dictionary<string, string?> { [a] = "1", [b] = "2" };
                if (same)
                {
                    Check.Equal("values", Check.Throws<ArgumentException>(() => new ConfigLayers().Add("s", src)).ParamName, at);
                    var layers = new ConfigLayers().Add("low", new Dictionary<string, string?> { [a] = "1" }).Add("high", new Dictionary<string, string?> { [b] = "2" });
                    Check.True(layers.TryGet(a, out string? v) && v == "2", at);
                    Check.Equal(1, layers.Resolve().Count, at);
                }
                else
                {
                    var layers = new ConfigLayers().Add("s", src);
                    Check.True(layers.TryGet(a, out string? va) && va == "1", at);
                    Check.True(layers.TryGet(b, out string? vb) && vb == "2", at);
                    Check.Equal(2, layers.Resolve().Count, at);
                }
            }
        }

        // A key held only as null is unset, as the doc says, but TryGet returned true with a null value.
        public void ConfigLayers_TryGet_KeyHeldOnlyAsNull_IsNotFound()
        {
            var layers = new ConfigLayers().Add("a", new Dictionary<string, string?> { ["k"] = null }).Add("b", new Dictionary<string, string?> { ["k"] = null, ["j"] = "1" });
            Check.False(layers.TryGet("k", out string? v));
            Check.Equal(null, v);
            Check.True(layers.TryGet("J", out string? j) && j == "1");
            Check.Equal("a", layers.SourceOf("k"));
            var lower = new ConfigLayers().Add("a", new Dictionary<string, string?> { ["k"] = "x" }).Add("b", new Dictionary<string, string?> { ["k"] = null });
            Check.True(lower.TryGet("K", out string? x) && x == "x");
        }
    }
}
