using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using ToolBelt.Configuration;
using ToolBelt.Documents;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Documents
{
    /// <summary>Regressions from review round 126 (documents and configuration, eighth pass): each test failed before its fix.</summary>
    public sealed class DocumentsConfigReview126Tests
    {
        public class BaseOpts { public int X { get; set; } }
        public sealed class HidingOpts : BaseOpts { public new string X { get; set; } = ""; }
        public sealed class DurationOpts { public TimeSpan T { get; set; } }

        private static IReadOnlyDictionary<string, string?> D(params (string k, string? v)[] kv)
            => kv.ToDictionary(p => p.k, p => p.v);

        private static TimeSpan? Env(string text) => EnvironmentVariables.From(new Dictionary<string, string> { ["X"] = text }).GetTimeSpan("X");

        // ---- ConfigBinder: a property hidden with 'new' ----

        public void ConfigBinder_BindsOnlyTheHidingProperty()
        {
            Check.Equal("hello", ConfigBinder.Bind<HidingOpts>(D(("X", "hello"))).X);
            HidingOpts h = ConfigBinder.Bind<HidingOpts>(D(("X", "5")));
            Check.Equal("5", h.X);
            Check.Equal(0, ((BaseOpts)h).X);
            Check.Equal(5, ConfigBinder.Bind<BaseOpts>(D(("X", "5"))).X);
        }

        // ---- Durations: an exact sum, then truncated ----

        public void Durations_AreTruncatedFromTheExactValue()
        {
            Check.Equal(TimeSpan.FromTicks(999_999_999_999_999), Env("99999999.99999999s"));
            Check.Equal(TimeSpan.FromTicks(999_999_999_999_999), ConfigBinder.Bind<DurationOpts>(D(("T", "99999999.99999999s"))).T);
            string nines = "0." + new string('9', 1000) + "ms";                // 9,999.99… ticks
            Check.Equal(TimeSpan.FromTicks(9999), Env(nines));
            Check.Equal(TimeSpan.FromTicks(-9999), ConfigBinder.Bind<DurationOpts>(D(("T", "-" + nines))).T);
            Check.Equal(TimeSpan.FromTicks(15000), Env("1.5e-3s"));
            Check.Equal(TimeSpan.FromTicks(1), Env("0.00005ms 0.00005ms"));     // half a tick twice is one
            Check.Equal(TimeSpan.Zero, Env("1e-99999999999999999999s"));
            Check.Equal(TimeSpan.FromSeconds(1), Env("0000000000000001s"));
            Check.Equal(TimeSpan.MaxValue, Env(long.MaxValue.ToString(CultureInfo.InvariantCulture) + "e-7s"));
            Check.Equal(TimeSpan.MaxValue, Env("922337203685.4775807s"));
            Check.Throws<FormatException>(() => Env("922337203685.4775808s"));   // one tick past the range
            Check.Throws<FormatException>(() => Env("922337203685s 0.4775808s"));
            // Sixteen significant digits, as a decimal reference sums them exactly.
            var rng = new Random(126);
            string[] units = { "d", "h", "m", "s", "ms" };
            long[] per = { TimeSpan.TicksPerDay, TimeSpan.TicksPerHour, TimeSpan.TicksPerMinute, TimeSpan.TicksPerSecond, TimeSpan.TicksPerMillisecond };
            for (int trial = 0; trial < 2000; trial++)
            {
                var sb = new StringBuilder();
                decimal ticks = 0;
                int terms = rng.Next(1, 4);
                for (int k = 0; k < terms; k++)
                {
                    int u = rng.Next(units.Length);
                    // 16 digits, a point anywhere among them (past the range for a long unit is skipped below).
                    decimal n = (decimal)(long)(rng.NextDouble() * 1e16) / (decimal)Math.Pow(10, rng.Next(4, 17));
                    ticks += n * per[u];
                    if (k > 0) sb.Append(' ');
                    sb.Append(n.ToString(CultureInfo.InvariantCulture)).Append(units[u]);
                }
                if (ticks > long.MaxValue) continue;
                var expected = TimeSpan.FromTicks((long)decimal.Truncate(ticks));
                Check.Equal(expected, Env(sb.ToString()));
                Check.Equal(expected, ConfigBinder.Bind<DurationOpts>(D(("T", sb.ToString()))).T);
            }
        }

        // ---- EnvironmentVariables.RequireAll ----

        public void RequireAll_ABadNameIsTheCallersArgument()
        {
            var env = EnvironmentVariables.From(new Dictionary<string, string> { ["A"] = "1" });
            Check.Equal("names", Check.Throws<ArgumentException>(() => env.RequireAll("A", "A=B")).ParamName);
            Check.Equal("names", Check.Throws<ArgumentException>(() => env.RequireAll("A\0B")).ParamName);
            env.RequireAll("A");
        }

        // ---- Undefined enum values ----

        public void Documents_UndefinedEnumValuesAreRejected()
        {
            Check.Equal("pageSize", Check.Throws<ArgumentOutOfRangeException>(() => new PdfWriter((PdfPageSize)42)).ParamName);
            Check.Equal("font", Check.Throws<ArgumentOutOfRangeException>(() => new PdfWriter(font: (PdfFont)42)).ParamName);
            Check.Equal("font", Check.Throws<ArgumentOutOfRangeException>(() => new PdfWriter(font: (PdfFont)(-1))).ParamName);
            Check.Equal("pageSize", Check.Throws<ArgumentOutOfRangeException>(() => new ReportBuilder("T").ToPdf((PdfPageSize)42)).ParamName);
            Check.Equal("kind", Check.Throws<ArgumentOutOfRangeException>(() => new ReportBuilder("T").Callout((CalloutKind)42, "x")).ParamName);
            Check.Equal("alignments", Check.Throws<ArgumentOutOfRangeException>(
                () => new MarkdownReport().Table(new[] { "a" }, new[] { new[] { "1" } }, new[] { (MarkdownAlign)42 })).ParamName);
            foreach (PdfFont f in Enum.GetValues(typeof(PdfFont))) new PdfWriter(PdfPageSize.A4, font: f).Paragraph("x").Build();
            foreach (CalloutKind k in Enum.GetValues(typeof(CalloutKind))) new ReportBuilder("T").Callout(k, "x").ToHtml();
            foreach (MarkdownAlign a in Enum.GetValues(typeof(MarkdownAlign)))
                new MarkdownReport().Table(new[] { "a" }, new[] { new[] { "1" } }, new[] { a });
        }

        // ---- DOCX ----

        private static string Entry(byte[] docx, string name)
        {
            using var zip = new ZipArchive(new MemoryStream(docx), ZipArchiveMode.Read);
            using var r = new StreamReader(zip.GetEntry(name)!.Open());
            return r.ReadToEnd();
        }

        public void Docx_EntriesCarryNoClockTime()
        {
            byte[] png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");
            byte[] docx = new DocxWriter { Title = "t" }.Header("h").Footer("f").Paragraph("p").BulletList(new[] { "a" }).Image(png, 1).Build();
            using var zip = new ZipArchive(new MemoryStream(docx), ZipArchiveMode.Read);
            Check.True(zip.Entries.Count >= 6, zip.Entries.Count + " entries");
            foreach (ZipArchiveEntry e in zip.Entries)
                Check.Equal(new DateTime(1980, 1, 1), e.LastWriteTime.DateTime, e.FullName);
        }

        public void Docx_AVeryWideTableHasNoZeroWidthColumn()
        {
            string[] headers = Enumerable.Range(0, 10_000).Select(i => "c").ToArray();
            byte[] docx = new DocxWriter().Table(headers, new[] { headers }).Build();
            string xml = Entry(docx, "word/document.xml");
            Check.True(!xml.Contains("<w:gridCol w:w=\"0\"/>"), "a zero-width grid column");
            Check.True(xml.Contains("<w:gridCol w:w=\"1\"/>"));
        }

        // ---- HtmlReport ----

        public void HtmlReport_ATableNeedsAColumn()
        {
            var html = new HtmlReport();
            Check.Equal("headers", Check.Throws<ArgumentException>(() => html.Table(Array.Empty<string>(), new[] { new[] { "x" } })).ParamName);
            Check.True(!html.ToString().Contains("<table"), "nothing was written");
        }

        // ---- MarkdownReport.Raw ----

        public void Markdown_ANestedItemInRawDoesNotSetTheMarker()
        {
            string md = new MarkdownReport().Raw("- a\n  * b").BulletList(new[] { "c" }).ToString();
            Check.True(md.Contains("\n* c\n"), md);
            md = new MarkdownReport().Raw("1. a\n   - b").NumberedList(new[] { "c" }).ToString();
            Check.True(md.Contains("\n1) c\n"), md);
            md = new MarkdownReport().Raw("- a\n\n      * code-ish\n  continued").BulletList(new[] { "c" }).ToString();
            Check.True(md.Contains("\n* c\n"), md);
            // An outer item after a nested one is still the one that counts.
            md = new MarkdownReport().Raw("* a\n  - b\n* c").BulletList(new[] { "d" }).ToString();
            Check.True(md.Contains("\n- d\n"), md);
            md = new MarkdownReport().Raw("- a\n  * b\n* c").BulletList(new[] { "d" }).ToString();
            Check.True(md.Contains("\n- d\n"), md);
        }
    }
}
