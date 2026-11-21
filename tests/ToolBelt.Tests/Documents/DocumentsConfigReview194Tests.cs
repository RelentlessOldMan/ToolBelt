using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using ToolBelt.Configuration;
using ToolBelt.Documents;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Documents
{
    /// <summary>Regressions from review round 194 (documents and configuration, twelfth pass): each test failed before its fix.</summary>
    public sealed class DocumentsConfigReview194Tests
    {
        public sealed class Dated
        {
            public DateTime When { get; set; }
        }

        public sealed class Twins
        {
            public int Port { get; set; }
            public int port { get; set; }
            public string Name { get; set; } = "";
        }

        private static Dictionary<string, string?> D(params (string Key, string? Value)[] pairs)
        {
            var d = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var (k, v) in pairs) d[k] = v;
            return d;
        }

        private static string DocumentXml(byte[] docx)
        {
            using var zip = new ZipArchive(new MemoryStream(docx));
            using var r = new StreamReader(zip.GetEntry("word/document.xml")!.Open(), Encoding.UTF8);
            return r.ReadToEnd();
        }

        // Word holds 63 columns: a 70-column table (which HTML and PDF render) failed ToDocx with ArgumentException.
        public void ReportBuilder_ToDocx_SplitsATableWiderThanWord()
        {
            const int width = 70;
            var headers = new string[width];
            var row = new string[width];
            for (int c = 0; c < width; c++) { headers[c] = "h" + c; row[c] = "v" + c; }
            byte[] docx = new ReportBuilder("R").Table(headers, new[] { row }, "wide").ToDocx();
            string xml = DocumentXml(docx);
            Check.True(xml.Contains("Columns 1-63 of 70"), "first group label");
            Check.True(xml.Contains("Columns 64-70 of 70"), "second group label");
            for (int c = 0; c < width; c++)
            {
                Check.True(xml.Contains(">h" + c + "<"), "header " + c);
                Check.True(xml.Contains(">v" + c + "<"), "cell " + c);
            }
            // At the limit the table is still one table, with no group labels.
            var h63 = new string[63];
            for (int c = 0; c < 63; c++) h63[c] = "h" + c;
            Check.False(DocumentXml(new ReportBuilder("R").Table(h63, new[] { h63 }).ToDocx()).Contains("Columns 1-"));
        }

        // The zero check parsed the text back, which fails for "P0", "C" and literal suffixes: "-0 %", "(0.00)", "-0.0 ms".
        public void Comparison_ValuesShowingAsZero_HaveNoSignInAnyFormat()
        {
            foreach (string format in new[] { "P0", "C", "0.0' ms'", "0.##" })
            {
                string zero = 0.0.ToString(format, System.Globalization.CultureInfo.InvariantCulture);
                string md = ReportTemplates.Comparison("C", "a", "b",
                    new[] { new ComparisonRow("z", -0.0001, -0.0001), new ComparisonRow("y", 0.0004, 0.0001) }, 5000, format).ToMarkdown();
                Check.True(md.Contains("| z | " + zero + " | " + zero + " | " + zero + " |"), format + ": " + md);
                Check.True(md.Contains("| y | " + zero + " | " + zero + " | " + zero + " |"), format + ": " + md);
            }
        }

        // "0.##" wrote every digit of a huge percentage: 1e-300 to 1 gave a 303-digit change.
        public void Comparison_HugePercentChange_IsShortened()
        {
            string md = ReportTemplates.Comparison("C", "a", "b", new[] { new ComparisonRow("r", 1e-300, 1) }, 5).ToMarkdown();
            Check.False(md.Contains(new string('0', 20)), md);
            Check.True(md.Contains("+1E+302%"), md);
            // Below a million percent the plain form stays.
            md = ReportTemplates.Comparison("C", "a", "b", new[] { new ComparisonRow("r", 1, 1000) }, 5).ToMarkdown();
            Check.True(md.Contains("+99900%"), md);
        }

        // A DateTime with an offset was converted to the machine's zone (Kind Local): one file, a different time per machine.
        public void ConfigBinder_DateTimeWithOffset_IsUtc()
        {
            DateTime w = ConfigBinder.Bind<Dated>(D(("When", "2024-01-01T00:00:00+05:00"))).When;
            Check.Equal(DateTimeKind.Utc, w.Kind);
            Check.Equal(new DateTime(2023, 12, 31, 19, 0, 0, DateTimeKind.Utc), w);
            w = ConfigBinder.Bind<Dated>(D(("When", "2024-01-01T00:00:00Z"))).When;
            Check.Equal(DateTimeKind.Utc, w.Kind);
            Check.Equal(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), w);
            w = ConfigBinder.Bind<Dated>(D(("When", "2024-01-01T00:00:00"))).When;
            Check.Equal(DateTimeKind.Unspecified, w.Kind);
            Check.Equal(new DateTime(2024, 1, 1), w);
        }

        // "Port" and "port" on one type were both filled from one key, though the binder refuses case ambiguity elsewhere.
        public void ConfigBinder_PropertiesDifferingOnlyInCase_AreAnError()
        {
            var ex = Check.Throws<ConfigBindingException>(() => ConfigBinder.Bind<Twins>(D(("Port", "5"))));
            Check.True(ex.Message.Contains("cannot tell apart"), ex.Message);
            Check.Equal(1, ex.Errors.Count);
            // Left unconfigured, the twins are no error, and the other properties bind.
            Check.Equal("x", ConfigBinder.Bind<Twins>(D(("Name", "x"))).Name);
            Check.Equal("x", ConfigBinder.Bind<Twins>(D(("Name", "x"), ("port", null))).Name);
        }

        // An item that is only a block marker ("+", "1.", "#", ">") became an empty list, heading or quote: its text was lost.
        public void MarkdownReport_ItemThatIsAMarker_KeepsItsText()
        {
            var items = new[] { "+", "1.", "12)", "#", "###", ">", "+ 2.", "b" };
            string md = new MarkdownReport().BulletList(items).ToString();
            foreach (string expected in new[] { "\\+", "1\\.", "12\\)", "\\#", "\\###", "\\>", "\\+ 2.", " b" })
                Check.True(md.Contains(expected), expected + ": " + md);
            md = new MarkdownReport().NumberedList(items).ToString();
            Check.True(md.Contains("1\\.") && md.Contains("\\#") && md.Contains("\\>"), md);
            // Items with text after the marker are left as written.
            md = new MarkdownReport().BulletList(new[] { "> quoted", "# title", "+ x" }).ToString();
            Check.False(md.Contains("\\"), md);
        }
    }
}
