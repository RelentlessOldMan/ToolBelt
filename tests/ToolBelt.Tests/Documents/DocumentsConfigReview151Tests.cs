using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using ToolBelt.Configuration;
using ToolBelt.Documents;
using ToolBelt.Tests.Framework;
using ToolBelt.Visualization;

namespace ToolBelt.Tests.Documents
{
    /// <summary>Regressions from review round 151 (documents and configuration, ninth pass): each test failed before its fix.</summary>
    public sealed class DocumentsConfigReview151Tests
    {
        // ---------------- MarkdownReport ----------------

        // A trailing '#' run after a space is a closing sequence ("# Issue #" renders as "Issue"), and a line break inside
        // the text ended the heading. The heading's text is graded by the CommonMark closing rule, applied by hand.
        public void Markdown_HeadingKeepsATrailingHashAndStaysOnOneLine()
        {
            foreach (string text in new[] { "Issue #", "C#", "#", "a ##", "a\t#", "##" })
            {
                string md = new MarkdownReport().Heading(2, text).ToString().TrimEnd('\n');
                Check.True(md.StartsWith("## ", StringComparison.Ordinal), md);
                Check.Equal(text, RenderedHeading(md.Substring(3)));
            }
            Check.Equal("## a b\n", new MarkdownReport().Heading(2, "a\nb").ToString());
            Check.Equal("# a b\n", new MarkdownReport().Heading(1, "a\r\nb").ToString());
        }

        // CommonMark: the content after "## ", minus an optional closing run of '#' preceded by a space or tab (or the whole
        // content), then backslash escapes resolved.
        private static string RenderedHeading(string content)
        {
            int run = content.Length;
            while (run > 0 && content[run - 1] == '#') run--;
            if (run < content.Length && (run == 0 || content[run - 1] == ' ' || content[run - 1] == '\t'))
            {
                int b = 0;
                for (int k = run - 1; k >= 0 && content[k] == '\\'; k--) b++;
                if (b % 2 == 0) content = content.Substring(0, run).TrimEnd(' ', '\t');
            }
            return Regex.Replace(content, @"\\([!-/:-@\[-`{-~])", "$1");
        }

        // Paragraph took no notice of the list before or after it: "1. x" right after a numbered list joined it as an item,
        // and "- x" took in the bullet list after it. Graded by counting the lists a CommonMark reader would see: blocks
        // separated by blank lines whose first lines carry the same marker are one list.
        public void Markdown_AParagraphStartingLikeAListItemDoesNotJoinAList()
        {
            string a = new MarkdownReport().NumberedList(new[] { "a" }).Paragraph("1. x").ToString();
            Check.Equal(2, Lists(a), a);
            string b = new MarkdownReport().Paragraph("- x").BulletList(new[] { "y" }).ToString();
            Check.Equal(2, Lists(b), b);
            string c = new MarkdownReport().BulletList(new[] { "a" }).Raw("- b").ToString();
            Check.Equal(2, Lists(c), c);
            Check.Equal("plain\n", new MarkdownReport().Paragraph("plain").ToString());
        }

        private static int Lists(string md)
        {
            int lists = 0;
            char open = '\0';
            foreach (string block in md.TrimEnd('\n').Split(new[] { "\n\n" }, StringSplitOptions.None))
            {
                char m = Marker(block);
                if (m != '\0' && m != open) lists++;
                open = m;
            }
            return lists;
        }

        private static char Marker(string block)
        {
            var mm = Regex.Match(block, @"^(?:[-*+]|\d{1,9}[.)])(?= |$)");
            return mm.Success ? mm.Value[mm.Value.Length - 1] : '\0';
        }

        // Empty code wrote a blank line between the fences (rendered as one empty line of code); a CR before the final LF
        // stayed in the last line.
        public void Markdown_EmptyCodeBlockHasNothingBetweenTheFences()
        {
            Check.Equal("```\n```\n", new MarkdownReport().CodeBlock("").ToString());
            Check.Equal("```cs\n```\n", new MarkdownReport().CodeBlock("\n\n", "cs").ToString());
            Check.Equal("```\na\n```\n", new MarkdownReport().CodeBlock("a\r\n").ToString());
        }

        // ---------------- HtmlReport ----------------

        // NUL was dropped by the parser, a lone surrogate can't be encoded as UTF-8, and other controls are parse errors:
        // the text that reaches the page is graded against what an HTML parser shows for each (U+FFFD).
        public void Html_ControlsAndLoneSurrogatesBecomeTheReplacementCharacter()
        {
            string Body(string text)
            {
                string html = new HtmlReport().Title("t").Paragraph(text).ToString();
                int s = html.IndexOf("<p>", StringComparison.Ordinal) + 3;
                return html.Substring(s, html.IndexOf("</p>", s, StringComparison.Ordinal) - s);
            }
            Check.Equal("a&#xFFFD;b", Body("a\0b"));
            Check.Equal("a&#xFFFD;b", Body("a\uD800b"));
            Check.Equal("a&#xFFFD;", Body("a\uDC00"));
            Check.Equal("&#xFFFD;&#xFFFD;", Body("\u0001\u000B"));
            Check.Equal("&#xFFFD;", Body("\u0085"));
            Check.Equal("a\tb\nc \U0001F600 &amp;", Body("a\tb\nc \U0001F600 &"));
            var strict = new UTF8Encoding(false, true);
            for (int ch = 0; ch < 0x10000; ch++)
            {
                string html = new HtmlReport().Title("t").Paragraph("x" + (char)ch + "y").ToString();
                strict.GetBytes(html);                                           // throws on a lone surrogate
                foreach (char k in html)
                    Check.True(k >= ' ' || k == '\n' || k == '\t' || k == '\r' || k == '\f', $"U+{ch:X4} left U+{(int)k:X4} in the page");
            }
        }

        // ---------------- ReportBuilder ----------------

        // Invariant casing leaves U+0130 alone, so "İstanbul" got the anchor "İstanbul"; GitHub lowers it as Unicode does.
        public void Slug_DottedCapitalILowersLikeUnicode()
        {
            MethodInfo slug = typeof(ReportBuilder).GetMethod("GithubSlug", BindingFlags.NonPublic | BindingFlags.Static)!;
            Check.Equal("i\u0307stanbul", (string)slug.Invoke(null, new object[] { "\u0130stanbul" })!);
            Check.Equal("abc", (string)slug.Invoke(null, new object[] { "ABC" })!);
        }

        // ---------------- ReportTemplates ----------------

        // The figure kept the caller's array, so a buffer reused before the report was built changed it.
        public void ReportFigure_CopiesThePng()
        {
            var buf = new byte[] { 1, 2, 3 };
            var fig = new ReportFigure("c", png: buf);
            buf[0] = 9;
            Check.Equal(1, (int)fig.Png![0]);
            Check.False(ReferenceEquals(buf, fig.Png));
        }

        // Infinity turned flagging off with "No metric changed by Infinity% or more"; a threshold under 0.005 was shown as
        // "0%", which reads as "any change".
        public void Comparison_RefusesAnInfiniteThresholdAndShowsASmallOne()
        {
            var rows = new[] { new ComparisonRow("m", 100, 150), new ComparisonRow("n", 1e6, 1e6 + 0.02) };
            Check.Equal("flagPercent", Check.Throws<ArgumentOutOfRangeException>(
                () => ReportTemplates.Comparison("t", "a", "b", rows, double.PositiveInfinity)).ParamName);
            string md = ReportTemplates.Comparison("t", "a", "b", rows, 0.001).ToMarkdown();
            Check.True(md.Contains("by 0.001% or more"), md);
            Check.True(md.Contains("m (+50%)") && md.Contains("+2E-06%"), md);
            Check.False(md.Contains(" 0% or more"), md);
        }

        // ---------------- DocxWriter ----------------

        private static byte[] SamplePng(int w = 30, int h = 20)
        {
            var img = new ImageBuffer(w, h, new Rgba(240, 240, 255));
            img.DrawLine(0, 0, w - 1, h - 1, new Rgba(200, 0, 0));
            return PngWriter.Encode(img);
        }

        // Only the 24-byte header was read, so a truncated PNG was embedded and Word showed a broken image (ToPdf refused
        // it). Every cut of a real PNG is graded against PdfWriter, which decodes it.
        public void Docx_ATruncatedPngIsRefusedLikeThePdfWriterRefusesIt()
        {
            byte[] png = SamplePng();
            new DocxWriter().Image(png);
            for (int cut = 24; cut < png.Length; cut++)
            {
                byte[] part = png.Take(cut).ToArray();
                bool pdf = true;
                try { new PdfWriter().ImagePng(part); } catch (ArgumentException) { pdf = false; }
                if (pdf) continue;   // the cut fell after IEND's data
                Check.Equal("png", Check.Throws<ArgumentException>(() => new DocxWriter().Image(part)).ParamName);
            }
        }

        // Word holds at most 63 columns: a wider table built, and Word reported the file damaged.
        public void Docx_ATableWiderThanWordAllowsIsRefused()
        {
            var ok = Enumerable.Range(0, 63).Select(i => "c" + i).ToArray();
            new DocxWriter().Table(ok, new[] { ok });
            var wide = Enumerable.Range(0, 64).Select(i => "c" + i).ToArray();
            Check.Equal("headers", Check.Throws<ArgumentException>(() => new DocxWriter().Table(wide, new[] { wide })).ParamName);
        }

        // Save wrote straight over the file, so for the length of the write the file was empty or half-written: a reader
        // (or a crash) caught a broken document where a good one had been. Graded by reading the file while it is saved over
        // and over: every read must be one of the two whole documents.
        public void Save_ReadersNeverSeeAHalfWrittenFile()
        {
            string dir = Path.Combine(Path.GetTempPath(), "tb-r151-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                foreach (bool docx in new[] { false, true })
                {
                    string path = Path.Combine(dir, docx ? "a.docx" : "a.pdf");
                    string big = string.Join(" ", Enumerable.Range(0, 60000).Select(i => "w" + i));
                    Action<string> save = text =>
                    {
                        if (docx) new DocxWriter().Paragraph(text).Save(path);
                        else new PdfWriter().Paragraph(text).Save(path);
                    };
                    save("a " + big);
                    byte[] a = File.ReadAllBytes(path);
                    save("b " + big);
                    byte[] b = File.ReadAllBytes(path);
                    bool stop = false;
                    Exception? failed = null;
                    var saver = new Thread(() =>
                    {
                        try { for (int i = 0; !Volatile.Read(ref stop); i++) save((i % 2 == 0 ? "a " : "b ") + big); }
                        catch (Exception ex) { failed = ex; }
                    });
                    saver.Start();
                    int reads = 0, torn = 0;
                    var until = DateTime.UtcNow.AddSeconds(2);
                    while (DateTime.UtcNow < until && failed is null)
                    {
                        byte[] got;
                        try
                        {
                            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                            {
                                var ms = new MemoryStream();
                                fs.CopyTo(ms);
                                got = ms.ToArray();
                            }
                        }
                        catch (IOException) { continue; }                      // gone for a moment mid-replace
                        catch (UnauthorizedAccessException) { continue; }
                        reads++;
                        if (!got.SequenceEqual(a) && !got.SequenceEqual(b)) torn++;
                    }
                    Volatile.Write(ref stop, true);
                    saver.Join();
                    Check.True(failed is null, failed?.ToString() ?? "");
                    Check.True(reads > 20, "only " + reads + " reads");
                    Check.Equal(0, torn);
                }
                Check.Equal(2, Directory.GetFiles(dir).Length);
            }
            finally { Directory.Delete(dir, true); }
        }

        // A save whose move is refused (the target is locked) must leave the old bytes and no temp file behind.
        public void Save_AFailedSaveLeavesTheOldFileWhole()
        {
            string dir = Path.Combine(Path.GetTempPath(), "tb-r151-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string pdf = Path.Combine(dir, "a.pdf"), docx = Path.Combine(dir, "a.docx");
                new PdfWriter().Paragraph("old").Save(pdf);
                new DocxWriter().Paragraph("old").Save(docx);
                byte[] oldPdf = File.ReadAllBytes(pdf), oldDocx = File.ReadAllBytes(docx);
                using (new FileStream(pdf, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (new FileStream(docx, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    Check.Throws<IOException>(() => new PdfWriter().Paragraph(new string('x', 5000)).Save(pdf));
                    Check.Throws<IOException>(() => new DocxWriter().Paragraph(new string('x', 5000)).Save(docx));
                }
                Check.True(File.ReadAllBytes(pdf).SequenceEqual(oldPdf));
                Check.True(File.ReadAllBytes(docx).SequenceEqual(oldDocx));
                Check.Equal(2, Directory.GetFiles(dir).Length);
                new PdfWriter().Paragraph("new").Save(pdf);
                Check.False(File.ReadAllBytes(pdf).SequenceEqual(oldPdf));
            }
            finally { Directory.Delete(dir, true); }
        }

        // ---------------- PdfWriter ----------------

        // At a large font size the page-number footer was centred far off both edges (x = -2169 at size 1000). Graded by
        // the writer's own Measure of the label at the size the content stream sets.
        public void Pdf_PageNumbersStayOnThePage()
        {
            foreach (double fs in new[] { 11.0, 100, 1000, 10000 })
            {
                var pdf = new PdfWriter(margin: 0, fontSize: fs) { PageNumbers = true };
                pdf.Paragraph("x");
                string content = string.Join("\n", Streams(pdf.Build()));
                var m = Regex.Match(content, @"/F1 ([0-9.]+) Tf ([-0-9.]+) [-0-9.]+ Td \((Page 1 of 1)\) Tj");
                Check.True(m.Success, "no footer at " + fs);
                double size = double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                double x = double.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
                double width = pdf.Measure(m.Groups[3].Value, size);
                Check.True(x >= 0 && x + width <= 612 + 1e-6, $"font {fs}: footer from {x} to {x + width}");
            }
        }

        private static IEnumerable<string> Streams(byte[] pdf)
        {
            string s = Encoding.GetEncoding(28591).GetString(pdf);
            for (int at = s.IndexOf("stream\n", StringComparison.Ordinal); at >= 0; at = s.IndexOf("stream\n", at + 1, StringComparison.Ordinal))
            {
                if (at >= 3 && s.Substring(at - 3, 3) == "end") continue;
                int start = at + 7, end = s.IndexOf("endstream", start, StringComparison.Ordinal);
                byte[] body = new byte[end - start];
                Array.Copy(pdf, start, body, 0, body.Length);
                string text;
                try
                {
                    using (var z = new DeflateStream(new MemoryStream(body, 2, body.Length - 2), CompressionMode.Decompress))
                    using (var r = new StreamReader(z, Encoding.GetEncoding(28591)))
                        text = r.ReadToEnd();
                }
                catch (InvalidDataException) { text = Encoding.GetEncoding(28591).GetString(body); }
                yield return text;
            }
        }

        // Wrap broke only at spaces, so words separated by tabs were split mid-word. Each wrapped line must hold whole
        // words only, and fit.
        public void Pdf_TabsAreBreakPoints()
        {
            MethodInfo wrap = typeof(PdfWriter).GetMethod("Wrap", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var pdf = new PdfWriter(PdfPageSize.Letter, 54, PdfFont.Helvetica, 11);
            var words = Enumerable.Range(0, 40).Select(i => "word" + i).ToArray();
            var lines = (List<string>)wrap.Invoke(pdf, new object[] { string.Join("\t", words), 300.0, 11.0, false })!;
            Check.True(lines.Count > 1);
            var seen = new List<string>();
            foreach (string line in lines)
            {
                Check.True(pdf.Measure(line, 11) <= 300 + 1e-9, line);
                seen.AddRange(line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries));
            }
            Check.Equal(string.Join(",", words), string.Join(",", seen));
        }

#if !NETFRAMEWORK   // SettingsStore is net8.0-only
        // ---------------- SettingsStore ----------------

        public sealed class S { public int N { get; set; } }

        private static string NewDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), "tb-r151-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        // File.Replace leaves no file at the name for a moment, and a load then took defaults with no warning (so the next
        // save overwrote the user's settings). Staged: a fresh temp file is there and the file appears 100 ms later.
        public void Settings_ALoadDuringAnotherSaveWaitsForTheFile()
        {
            string dir = NewDir();
            try
            {
                string path = Path.Combine(dir, "s.json");
                File.WriteAllText(Path.Combine(dir, ".tb-" + Guid.NewGuid().ToString("N") + ".tmp"), "{\"N\":5}");
                var writer = new Thread(() => { Thread.Sleep(100); File.WriteAllText(path, "{\"N\":5}"); });
                writer.Start();
                var store = SettingsStore<S>.LoadFrom(path);
                writer.Join();
                Check.Equal(5, store.Value.N);
                Check.True(store.LoadWarning is null);

                // No save under way: a missing file is "no file yet", at once.
                string other = Path.Combine(NewDir(), "t.json");
                Check.Equal(0, SettingsStore<S>.LoadFrom(other).Value.N);
                Directory.Delete(Path.GetDirectoryName(other)!, true);
            }
            finally { Directory.Delete(dir, true); }
        }

        // Live loads against a store saving in a loop: no load may see defaults without a warning (1,189 of 68,449 did).
        public void Settings_LoadsDuringSavesNeverGetSilentDefaults()
        {
            string dir = NewDir();
            try
            {
                string path = Path.Combine(dir, "s.json");
                var store = SettingsStore<S>.LoadFrom(path);
                store.Update(v => v.N = 1);
                bool stop = false;
                Exception? failed = null;
                var saver = new Thread(() =>
                {
                    try { for (int i = 2; !Volatile.Read(ref stop); i++) store.Update(v => v.N = i); }
                    catch (Exception ex) { failed = ex; }
                });
                saver.Start();
                int silent = 0, loads = 0;
                var until = DateTime.UtcNow.AddSeconds(3);
                while (DateTime.UtcNow < until)
                {
                    var r = SettingsStore<S>.LoadFrom(path);
                    loads++;
                    if (r.Value.N == 0 && r.LoadWarning is null) silent++;
                }
                Volatile.Write(ref stop, true);
                saver.Join();
                Check.True(failed is null, failed?.ToString() ?? "");
                Check.True(loads > 100, "only " + loads + " loads");
                Check.Equal(0, silent);
            }
            finally { Directory.Delete(dir, true); }
        }

        // A temp file two hours old (a share whose clock runs behind, or a slow save) was deleted as stale; File.Replace's
        // "name~RF....TMP" leftovers were never cleaned up. A day is the cutoff for both.
        public void Settings_StaleTempCleanupUsesADayAndCoversReplaceLeftovers()
        {
            string dir = NewDir();
            try
            {
                string path = Path.Combine(dir, "s.json");
                var store = SettingsStore<S>.LoadFrom(path);
                store.Update(v => v.N = 1);
                string young = Path.Combine(dir, ".tb-" + Guid.NewGuid().ToString("N") + ".tmp");
                string old = Path.Combine(dir, ".tb-" + Guid.NewGuid().ToString("N") + ".tmp");
                string rfOld = Path.Combine(dir, "s.json~RF1a2b3c4d.TMP"), rfYoung = Path.Combine(dir, "s.json~RF5e6f7a8b.TMP");
                string unrelated = Path.Combine(dir, "t.json~RF1a2b3c4d.TMP");
                foreach (string f in new[] { young, old, rfOld, rfYoung, unrelated }) File.WriteAllText(f, "{}");
                File.SetLastWriteTimeUtc(young, DateTime.UtcNow.AddHours(-2));
                File.SetLastWriteTimeUtc(rfYoung, DateTime.UtcNow.AddHours(-2));
                File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-2));
                File.SetLastWriteTimeUtc(rfOld, DateTime.UtcNow.AddDays(-2));
                File.SetLastWriteTimeUtc(unrelated, DateTime.UtcNow.AddDays(-2));
                store.Update(v => v.N = 2);
                Check.True(File.Exists(young), "a 2-hour-old temp was deleted");
                Check.True(File.Exists(rfYoung));
                Check.False(File.Exists(old));
                Check.Equal(OperatingSystem.IsWindows(), !File.Exists(rfOld));
                Check.True(File.Exists(unrelated));
            }
            finally { Directory.Delete(dir, true); }
        }

        // Several instances saving one file: a replace that found the target missing for a moment (another instance's
        // replace under way) gave up at once with FileNotFoundException or IOException.
        public void Settings_SeveralInstancesSavingOneFileAllSucceed()
        {
            string dir = NewDir();
            try
            {
                string path = Path.Combine(dir, "s.json");
                SettingsStore<S>.LoadFrom(path).Update(v => v.N = 1);
                var errors = new System.Collections.Concurrent.ConcurrentQueue<Exception>();
                var threads = Enumerable.Range(0, 6).Select(t => new Thread(() =>
                {
                    var s = SettingsStore<S>.LoadFrom(path);
                    for (int i = 0; i < 60; i++)
                    {
                        try { s.Update(v => v.N = t * 1000 + i); }
                        catch (Exception ex) { errors.Enqueue(ex); }
                    }
                })).ToList();
                threads.ForEach(t => t.Start());
                threads.ForEach(t => t.Join());
                Check.True(errors.IsEmpty, errors.Count + " failed saves; first: " + errors.FirstOrDefault());
                Check.True(SettingsStore<S>.LoadFrom(path).Value.N > 0);
            }
            finally { Directory.Delete(dir, true); }
        }
#endif
    }
}
