using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ToolBelt.Configuration;
using ToolBelt.Documents;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Documents
{
    /// <summary>Regressions from review round 35 (documents and configuration, third pass): each test failed before its fix.</summary>
    public sealed class DocumentsConfigReview3Tests
    {
        private static readonly Encoding Latin1 = Encoding.GetEncoding(28591);

        private static Dictionary<string, string?> D(params (string Key, string? Value)[] pairs)
        {
            var d = new Dictionary<string, string?>();
            foreach (var (k, v) in pairs) d[k] = v;
            return d;
        }

        // ---------------- ConfigBinder ----------------

        public sealed class GetOnlyScalar { public int Port { get; } = 80; }
        public sealed class Inner { public int X { get; set; } }
        public sealed class ReadOnlyChild { public Inner? Child { get; } }
        public sealed class Hosts { public List<string> Names { get; set; } = new List<string>(); public string[]? Arr { get; set; } }
        public sealed class Node { public int Value { get; set; } public Node? Next { get; set; } }
        public sealed class WithObject { public object? Extra { get; set; } }
        public sealed class Floats { public float F { get; set; } public double D { get; set; } }

        public void Binder_AConfiguredReadOnlyScalarIsAnError()
        {
            var ex = Check.Throws<ConfigBindingException>(() => ConfigBinder.Bind<GetOnlyScalar>(D(("Port", "8080"))));
            Check.True(ex.Errors.Single().Contains("Port"), ex.Errors.Single());
            Check.Equal(80, ConfigBinder.Bind<GetOnlyScalar>(D()).Port);   // unconfigured: fine
        }

        public void Binder_ASectionForANullReadOnlyPropertyIsAnError()
        {
            var ex = Check.Throws<ConfigBindingException>(() => ConfigBinder.Bind<ReadOnlyChild>(D(("Child:X", "5"))));
            Check.True(ex.Errors.Single().Contains("Child"), ex.Errors.Single());
        }

        public void Binder_AListGivenOneValueIsAnError()
        {
            var ex = Check.Throws<ConfigBindingException>(() => ConfigBinder.Bind<Hosts>(D(("Names", "a,b"), ("Arr", "x;y"))));
            Check.Equal(2, ex.Errors.Count);
            Check.True(ex.Errors.Any(e => e.Contains("Names:0")), string.Join(" | ", ex.Errors));
        }

        public void Binder_ANonNumericListIndexIsAnError()
        {
            var ex = Check.Throws<ConfigBindingException>(() => ConfigBinder.Bind<Hosts>(D(("Names:first", "a"))));
            Check.True(ex.Errors.Single().Contains("Names:first"), ex.Errors.Single());
        }

        public void Binder_BindsAPropertyOfItsOwnType()
        {
            var n = ConfigBinder.Bind<Node>(D(("Value", "1"), ("Next:Value", "2"), ("Next:Next:Value", "3")));
            Check.Equal(1, n.Value);
            Check.Equal(2, n.Next!.Value);
            Check.Equal(3, n.Next.Next!.Value);
            Check.Null(n.Next.Next.Next);
        }

        public void Binder_AnObjectPropertyIsNotBoundIntoSilently()
        {
            Check.Throws<ConfigBindingException>(() => ConfigBinder.Bind<WithObject>(D(("Extra:Foo", "1"))));
        }

        public void Binder_AFloatingPointOverflowIsAnError()
        {
            var ex = Check.Throws<ConfigBindingException>(() => ConfigBinder.Bind<Floats>(D(("F", "1e39"), ("D", "1e400"))));
            Check.Equal(2, ex.Errors.Count);
            var ok = ConfigBinder.Bind<Floats>(D(("F", "-Infinity"), ("D", "Infinity")));
            Check.Equal(float.NegativeInfinity, ok.F);
            Check.Equal(double.PositiveInfinity, ok.D);
        }

        // ---------------- ConfigLayers / EnvironmentVariables ----------------

        public void Layers_LookupsDoNotRebuildTheMergedView()
        {
            const int n = 20000;
            var a = new Dictionary<string, string?>();
            var b = new Dictionary<string, string?>();
            for (int i = 0; i < n; i++) { a["a" + i] = "1"; b["b" + i] = "2"; }
            var layers = new ConfigLayers().Add("defaults", a).Add("file", b);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < n; i++)
            {
                Check.True(layers.TryGet("A" + i, out string? v) && v == "1");
                Check.Equal("file", layers.SourceOf("b" + i));
            }
            Check.True(sw.Elapsed.TotalSeconds < 2, "took " + sw.Elapsed);
            Check.Null(layers.SourceOf("missing"));
        }

        public void Env_TheLongestDurationParses()
        {
            var env = new EnvironmentVariables(n => n == "T" ? "922337203685477ms" : n == "U" ? "922337203685477.6ms" : null);
            Check.True(env.GetTimeSpan("T")!.Value > TimeSpan.FromDays(10_000_000));
            Check.Throws<FormatException>(() => env.GetTimeSpan("U"));
        }

        // ---------------- SettingsStore ----------------

        public sealed class WithGetOnly
        {
            public List<string> Recent { get; } = new List<string>();
            public Dictionary<string, int> Counts { get; } = new Dictionary<string, int>();
            public Inner Window { get; } = new Inner();
        }

        public void Settings_GetOnlyCollectionsAndObjectsLoadBack()
        {
            string dir = Path.Combine(Path.GetTempPath(), "tb-r35-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string path = Path.Combine(dir, "settings.json");
                var store = SettingsStore<WithGetOnly>.LoadFrom(path);
                store.Value.Recent.Add("a.txt");
                store.Value.Recent.Add("b.txt");
                store.Value.Counts["x"] = 3;
                store.Value.Window.X = 42;
                store.Save();

                var again = SettingsStore<WithGetOnly>.LoadFrom(path);
                Check.Equal("a.txt|b.txt", string.Join("|", again.Value.Recent));
                Check.Equal(3, again.Value.Counts["x"]);
                Check.Equal(42, again.Value.Window.X);
                again.Save();   // and saving again doesn't erase them
                Check.Equal(2, SettingsStore<WithGetOnly>.LoadFrom(path).Value.Recent.Count);
            }
            finally { Directory.Delete(dir, true); }
        }

        // ---------------- PdfWriter ----------------

        private static string PdfText(PdfWriter w) => Latin1.GetString(w.Build());

        public void Pdf_LeadingSpacesAreKept()
        {
            string pdf = PdfText(new PdfWriter().Lines(new[] { "    indented", "  two" }).Paragraph("   para"));
            Check.True(pdf.Contains("(    indented) Tj"), "Lines");
            Check.True(pdf.Contains("(  two) Tj"), "Lines");
            Check.True(pdf.Contains("(   para) Tj"), "Paragraph");
        }

        public void Pdf_ATinyFontTableFitsOnePage()
        {
            var w = new PdfWriter().Table(new[] { "h" }, new[] { new[] { "l1\nl2\nl3\nl4\nl5" } }, fontSize: 1e-7);
            w.Build();
            Check.Equal(1, w.PageCount);
        }

        public void Pdf_AThinImageIsNotPlacedWithAZeroSide()
        {
            string pdf = PdfText(new PdfWriter().Image(2, 2, new byte[16], widthPoints: 1e-9));
            Check.False(pdf.Contains("q 0 0 0 0 "), "singular matrix");
        }

        // ---------------- ReportBuilder / MarkdownReport ----------------

        public void Report_PdfRejectsAnOverflowingFontSizeByName()
        {
            var ex = Check.Throws<ArgumentOutOfRangeException>(() => new ReportBuilder("T").ToPdf(fontSize: 1e308));
            Check.Equal("fontSize", ex.ParamName);
        }

        public void Report_MarkdownTocNestsByDepthNotLevel()
        {
            string md = new ReportBuilder("T").TableOfContents().Heading("Deep", 3).Heading("Top", 1).Heading("Sub", 2).ToMarkdown();
            Check.True(md.Contains("\n- [Deep]("), md);   // a skipped level is not a 4-space code block
            Check.True(md.Contains("\n- [Top]("), md);
            Check.True(md.Contains("\n  - [Sub]("), md);
        }

        public void Markdown_AMultiParagraphItemStaysInOneList()
        {
            string md = new MarkdownReport().BulletList(new[] { "first\n\nmore", "second" }).ToString();
            Check.Equal("- first\n\n  more\n- second\n", md);
            string ol = new MarkdownReport().NumberedList(new[] { "a\nb" }).ToString();
            Check.Equal("1. a\n   b\n", ol);
        }

        public void Markdown_EmphasisKeepsEdgeSpacesOutsideAndEmptyIsEmpty()
        {
            Check.Equal(" **Key:** ", MarkdownReport.Bold(" Key: "));
            Check.Equal("*x* ", MarkdownReport.Italic("x "));
            Check.Equal("", MarkdownReport.Bold(""));
            Check.Equal("", MarkdownReport.Bold(null!));
            Check.Equal("", MarkdownReport.Code(""));
        }
    }
}
