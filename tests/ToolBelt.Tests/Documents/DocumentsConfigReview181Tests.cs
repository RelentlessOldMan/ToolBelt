using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using ToolBelt.Configuration;
using ToolBelt.Documents;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Documents
{
    /// <summary>Regressions from review round 181 (documents and configuration, eleventh pass): each test failed before its fix.</summary>
    public sealed class DocumentsConfigReview181Tests
    {
        public sealed class Inner
        {
            public int Port { get; set; }
        }

        public sealed class Computed
        {
            public Inner Child => new Inner();
        }

        public sealed class Kept
        {
            public Inner Child { get; } = new Inner();
        }

        public sealed class Underscored
        {
            public int Port { get; set; }
            public string? Port_Name { get; set; }
        }

        public sealed class DoubleUnderscored
        {
            public Inner? Max { get; set; }
            public int Max__Size { get; set; }
        }

        public sealed class Timed
        {
            public TimeSpan T { get; set; }
        }

        public sealed class Floats
        {
            public float F { get; set; }
        }

        public sealed class Numbered
        {
            public int N { get; set; }
            public int Port { get; set; } = 1;
        }

        private static Dictionary<string, string?> D(params (string Key, string? Value)[] pairs)
        {
            var d = new Dictionary<string, string?>();
            foreach (var (k, v) in pairs) d[k] = v;
            return d;
        }

        // A computed getter returned a new instance on every read: the section was bound into one nobody kept, silently.
        public void Binder_ComputedSectionGetter_IsAnError()
        {
            var ex = Check.Throws<ConfigBindingException>(() => ConfigBinder.Bind<Computed>(D(("Child:Port", "7"))));
            Check.True(ex.Message.Contains("'Child'") && ex.Message.Contains("read-only"), ex.Message);
            // A get-only property holding one instance still binds into it.
            Check.Equal(7, ConfigBinder.Bind<Kept>(D(("Child:Port", "7"))).Child.Port);
            // Unconfigured, the computed getter is no error.
            ConfigBinder.Bind<Computed>(D());
        }

        // A property named with the separator in it ("Port_Name" beside "Port" with "_") was read as Port's nested key.
        public void Binder_PropertyNameHoldingTheSeparator_IsItsOwnKey()
        {
            var u = ConfigBinder.Bind<Underscored>(D(("Port", "5"), ("Port_Name", "x")), "_");
            Check.Equal(5, u.Port);
            Check.Equal("x", u.Port_Name);
            var d = ConfigBinder.Bind<DoubleUnderscored>(D(("Max__Size", "9")), "__");
            Check.Equal(9, d.Max__Size);
            Check.True(d.Max is null, "Max was created");
            // Max's own section still binds beside it.
            d = ConfigBinder.Bind<DoubleUnderscored>(D(("Max__Size", "9"), ("Max__Port", "3")), "__");
            Check.Equal(3, d.Max!.Port);
            Check.Equal(9, d.Max__Size);
        }

        // A 1,000,000-character value made a 1,000,000-character message.
        public void Errors_TruncateHugeValues()
        {
            string huge = new string('9', 1000000) + "ms";
            var ex = Check.Throws<ConfigBindingException>(() => ConfigBinder.Bind<Numbered>(D(("N", huge))));
            Check.True(ex.Message.Length < 400, "message length " + ex.Message.Length);
            Check.True(ex.Message.Contains("(1000002 characters)"), ex.Message);
            var fe = Check.Throws<FormatException>(() => EnvironmentVariables.From(new Dictionary<string, string> { ["X"] = huge }).GetInt("X"));
            Check.True(fe.Message.Length < 400, "message length " + fe.Message.Length);
            Check.True(fe.Message.Contains("(1000002 characters)"), fe.Message);
            // A surrogate pair at the cut is not split.
            string pairs = new string('a', 99) + "\U0001F600" + new string('b', 50);
            ex = Check.Throws<ConfigBindingException>(() => ConfigBinder.Bind<Numbered>(D(("N", pairs))));
            Check.True(ex.Message.Contains("'" + new string('a', 99) + "... ("), ex.Message);
        }

        // TimeSpan.MinValue in seconds was refused: the range check ran on the magnitude before the sign.
        public void Binder_MinValueInSeconds_Binds()
        {
            Check.Equal(TimeSpan.MinValue, ConfigBinder.Bind<Timed>(D(("T", "-922337203685.4775808s"))).T);
            Check.Equal(TimeSpan.MaxValue, ConfigBinder.Bind<Timed>(D(("T", "922337203685.4775807s"))).T);
            Check.Throws<ConfigBindingException>(() => ConfigBinder.Bind<Timed>(D(("T", "922337203685.4775808s"))));
            Check.Throws<ConfigBindingException>(() => ConfigBinder.Bind<Timed>(D(("T", "-922337203685.4775809s"))));
        }

        // .NET Framework rounded a float twice (to a double, then to even), so a decimal just off a midpoint between two
        // floats went the wrong way. Graded against the exact midpoint over random floats, just above, just below and on it.
        public void Binder_Float_IsTheNearestFloatEverywhere()
        {
            Check.Equal(0x3F800001, Bits(ConfigBinder.Bind<Floats>(D(("F", "1.0000000596046448"))).F));
            Check.Equal(0x3F800001, Bits(ConfigBinder.Bind<Floats>(D(("F", "1.00000017881393432617187499"))).F));
            var rng = new Random(181);
            for (int trial = 0; trial < 3000; trial++)
            {
                int bits = trial < 20 ? trial : trial < 40 ? 0x7F7FFFFE - (trial - 20) : rng.Next(1, 0x7F7FFFFF);
                bool negative = rng.Next(2) == 0;
                double lo = BitConverter.ToSingle(BitConverter.GetBytes(bits), 0);
                double hi = bits == 0x7F7FFFFF ? 0 : BitConverter.ToSingle(BitConverter.GetBytes(bits + 1), 0);
                if (bits == 0x7F7FFFFF) continue;
                double mid = (lo + hi) / 2;                                   // exact
                (BigInteger n, int scale) = Exact(mid);
                string sign = negative ? "-" : "";
                int even = (bits & 1) == 0 ? bits : bits + 1;
                int above = bits + 1, below = bits;
                Check.Equal(Signed(above, negative), Bits(Bind(sign + Decimal(n * 1000000 + 1, scale + 6))));
                Check.Equal(Signed(below, negative), Bits(Bind(sign + Decimal(n * 1000000 - 1, scale + 6))));
                Check.Equal(Signed(even, negative), Bits(Bind(sign + Decimal(n, scale))));
            }

            static float Bind(string s) => ConfigBinder.Bind<Floats>(D(("F", s))).F;
            static int Signed(int bits, bool negative) => negative ? bits | int.MinValue : bits;
        }

        private static int Bits(float f) => BitConverter.ToInt32(BitConverter.GetBytes(f), 0);

        // A positive finite double as n / 10^scale, exactly.
        private static (BigInteger, int) Exact(double m)
        {
            long b = BitConverter.DoubleToInt64Bits(m);
            int e = (int)((b >> 52) & 0x7FF);
            BigInteger man = b & ((1L << 52) - 1);
            if (e == 0) e = 1; else man += 1L << 52;
            int k = e - 1075;
            return k >= 0 ? (man << k, 0) : (man * BigInteger.Pow(5, -k), -k);
        }

        private static string Decimal(BigInteger n, int scale)
        {
            string digits = n.ToString(CultureInfo.InvariantCulture).PadLeft(scale + 1, '0');
            return scale == 0 ? digits : digits.Substring(0, digits.Length - scale) + "." + digits.Substring(digits.Length - scale);
        }

        // A null in a higher layer hid a lower layer's value; provenance named it and Bind took the default.
        public void Layers_NullIsUnset()
        {
            var layers = new ConfigLayers().Add("file", D(("Port", "8080"))).Add("env", D(("Port", null)));
            Check.Equal("8080", layers.Resolve()["Port"]);
            Check.Equal("file", layers.SourceOf("Port"));
            Check.Equal("file", layers.Provenance()["Port"]);
            Check.True(layers.TryGet("port", out string? v) && v == "8080");
            Check.Equal(8080, ConfigBinder.Bind<Numbered>(layers.Resolve()).Port);
            // Null everywhere: null, from the lowest layer holding it.
            var nulls = new ConfigLayers().Add("a", D(("K", null))).Add("b", D(("K", null)));
            Check.True(nulls.Resolve().ContainsKey("K") && nulls.Resolve()["K"] is null);
            Check.Equal("a", nulls.SourceOf("K"));
            Check.Equal("a", nulls.Provenance()["K"]);
            // A later value still wins over an earlier one.
            var over = new ConfigLayers().Add("a", D(("K", null))).Add("b", D(("K", "1"))).Add("c", D(("K", "2")));
            Check.Equal("c", over.SourceOf("K"));
            Check.Equal("2", over.Resolve()["K"]);
        }

        // U+2028 and U+2029 are written as spaces in a heading, but the slug dropped them: the TOC link pointed nowhere.
        public void Slug_LineSeparators_AreSpaces()
        {
            Check.Equal("a-b", ReportBuilder.Slug("a\u2028b"));
            Check.Equal("c-d", ReportBuilder.Slug("c\u2029d"));
            string md = new ReportBuilder("T").TableOfContents().Heading("a\u2028b").ToMarkdown();
            Check.True(md.Contains("(#a-b)"), md);
        }

        // On .NET Framework, letters of newer Unicode versions were unassigned and dropped from slugs.
        public void Slug_NewerUnicodeLetters_AreKeptEverywhere()
        {
            Check.Equal("a\u0860b", ReportBuilder.Slug("A\u0860B"));
            Check.Equal("a\u0870b", ReportBuilder.Slug("A\u0870B"));
            Check.Equal("a\U000104D8b", ReportBuilder.Slug("A\U000104B0B"));
            Check.Equal("a\U0001E922b", ReportBuilder.Slug("A\U0001E900B"));
            Check.Equal("a\U00031350b", ReportBuilder.Slug("A\U00031350B"));   // CJK Extension H
            Check.Equal("a\U000111C9b", ReportBuilder.Slug("A\U000111C9B"));   // a punctuation mark to Framework
        }

        // Raw trimmed the first line's indentation, so an indented code block became a paragraph.
        public void Raw_KeepsFirstLineIndentation()
        {
            Check.Equal("    x = 1\n    y = 2\n", new MarkdownReport().Raw("    x = 1\n    y = 2").ToString());
            Check.Equal("  - a\n", new MarkdownReport().Raw("\n\n  - a  \n\n").ToString());
            // Indented by up to three spaces, a Raw item still follows a list without joining it; by four it is code.
            string md = new MarkdownReport().BulletList(new[] { "x" }).Raw("  - y").ToString();
            Check.True(md.Contains("<!-- -->"), md);
            md = new MarkdownReport().BulletList(new[] { "x" }).Raw("    - y").ToString();
            Check.True(!md.Contains("<!-- -->"), md);
        }

#if !NETFRAMEWORK
        public sealed class Prefs
        {
            public int N { get; set; } = 3;
        }

        // A file holding only null loaded as defaults with no warning, and the next save replaced it.
        public void Settings_NullFile_IsSetAsideWithAWarning()
        {
            string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "tb-r181-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            System.IO.Directory.CreateDirectory(dir);
            try
            {
                string path = System.IO.Path.Combine(dir, "settings.json");
                System.IO.File.WriteAllText(path, "null");
                var store = SettingsStore<Prefs>.LoadFrom(path);
                Check.Equal(3, store.Value.N);
                Check.True(store.LoadWarning != null, "no warning");
                Check.Equal(1, System.IO.Directory.GetFiles(dir, "settings.json.corrupt-*").Length);
            }
            finally { System.IO.Directory.Delete(dir, true); }
        }

#endif

        // The A and B columns printed "-0" for -0.0 and for a value rounding to zero; the Change column never did.
        public void Comparison_ValuesRoundingToZero_PrintAsZero()
        {
            string md = ReportTemplates.Comparison("C", "a", "b", new[] { new ComparisonRow("z", -0.0, -0.001) }, 5, "0.##").ToMarkdown();
            Check.True(md.Contains("| z | 0 | 0 |"), md);
            Check.True(!md.Contains("-0"), md);
        }

        // With a threshold above 0 (the default 5 among them), a change from 0 was never flagged.
        public void Comparison_ChangeFromZero_IsFlaggedAtAnyThreshold()
        {
            string md = ReportTemplates.Comparison("C", "a", "b", new[] { new ComparisonRow("errors", 0, 100) }, 5).ToMarkdown();
            Check.True(md.Contains("errors (from 0)"), md);
            Check.True(!md.Contains("No metric changed"), md);
            md = ReportTemplates.Comparison("C", "a", "b", new[] { new ComparisonRow("errors", 0, 0) }, 5).ToMarkdown();
            Check.True(md.Contains("No metric changed by 5% or more."), md);
        }
    }
}
