using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using ToolBelt.Configuration;
using ToolBelt.Documents;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Documents
{
    /// <summary>Regressions from review round 222 (documents and configuration, fourteenth pass): each test failed before its fix.</summary>
    public sealed class DocumentsConfigReview222Tests
    {
        // The markers of the outer list items, in order ('-', '*', '.', ')'), one per item line at column 0.
        private static string Markers(string md) =>
            new string(md.Split('\n').Select(l => Regex.Match(l, @"^(?:([-*+])|\d+([.)]))(?: |$)"))
                .Where(m => m.Success).Select(m => m.Groups[1].Success ? m.Groups[1].Value[0] : m.Groups[2].Value[0]).ToArray());

        // ---------- MarkdownReport.Raw / Paragraph ----------

        // An empty block wrote only blank lines yet reset the list marker: the next list took the same marker and joined
        // the one before, so two lists rendered as one.
        public void EmptyBlockBetweenTwoLists_KeepsThemApart()
        {
            foreach (string empty in new[] { "", " ", "\n\n", " \r\n\t" })
            {
                Check.Equal("-*", Markers(new MarkdownReport().BulletList(new[] { "a" }).Paragraph(empty).BulletList(new[] { "b" }).ToString()), "bullets, Paragraph(" + empty.Length + ")");
                Check.Equal(".)", Markers(new MarkdownReport().NumberedList(new[] { "a" }).Raw(empty).NumberedList(new[] { "b" }).ToString()), "numbers, Raw(" + empty.Length + ")");
                Check.Equal("-*", Markers(new ReportBuilder("T").BulletList(new[] { "a" }).Paragraph(empty).BulletList(new[] { "b" }).ToMarkdown()), "ReportBuilder");
            }
            Check.Equal(new MarkdownReport().ToString(), new MarkdownReport().Raw("").Raw("  \n ").ToString(), "an empty block writes nothing");
            Check.Equal("x\n\ny\n", new MarkdownReport().Raw("x").Raw("").Raw("y").ToString());
        }

        // Raw keeps a first line's indentation ("    x = 1" is code), but right after a list the indented line was read as
        // more of the list's last item.
        public void IndentedBlockAfterAList_IsNotPartOfTheList()
        {
            foreach (string block in new[] { "    x = 1", "  x", "\tx" })
            {
                string md = new MarkdownReport().BulletList(new[] { "a" }).Raw(block).ToString();
                Check.Equal("- a\n\n<!-- -->\n\n" + block + "\n", md, block);
                md = new MarkdownReport().NumberedList(new[] { "a" }).Paragraph(block).ToString();
                Check.Equal("1. a\n\n<!-- -->\n\n" + block + "\n", md, block);
            }
            // Not after a list, and not for an unindented block: no separator.
            Check.Equal("x\n\n    y\n", new MarkdownReport().Raw("x").Raw("    y").ToString());
            Check.Equal("- a\n\nb\n", new MarkdownReport().BulletList(new[] { "a" }).Paragraph("b").ToString());
        }

        // ---------- MarkdownReport list items ----------

        // An item that was a code fence opened an empty code block that took the item's text with it.
        public void ItemThatIsACodeFence_KeepsItsText()
        {
            string md = new MarkdownReport().BulletList(new[] { "```", "x", "~~~ x", "````py", "  ~~~" }).ToString();
            Check.Equal("- \\```\n- x\n- \\~~~ x\n- \\````py\n- \\~~~\n", md);
            // Not a fence: a code span, two backticks, and text after the run.
            Check.Equal("- ```x```\n- ``\n", new MarkdownReport().BulletList(new[] { "```x```", "``" }).ToString());
            Check.Equal("1. \\~~~\n", new MarkdownReport().NumberedList(new[] { "~~~" }).ToString());
        }

        // Only the first line of an item was guarded: a continuation line "---" or "===" made the line above a setext
        // heading, a "```" opened a code block, and "#" or "***" became a heading or a rule.
        public void ContinuationLineOfAnItem_IsGuardedLikeTheFirst()
        {
            string md = new MarkdownReport().BulletList(new[] { "a\n---", "b\n===", "c\n```\nd", "e\n#", "f\n***", "g\n   -" }).ToString();
            Check.Equal("- a\n  \\---\n- b\n  \\===\n- c\n  \\```\n  d\n- e\n  \\#\n- f\n  \\***\n- g\n     \\-\n", md);
            // Four spaces continue the paragraph: left alone, as is ordinary text.
            Check.Equal("1. a\n       ---\n   b = c\n", new MarkdownReport().NumberedList(new[] { "a\n    ---\nb = c" }).ToString());
        }

        // ---------- ReportBuilder.ToMarkdown metadata ----------

        // A key's leading white space stayed outside the bold markers, and the indented first line made the metadata block
        // a code block showing literal asterisks.
        public void MetadataKeyWithEdgeWhiteSpace_StaysBold()
        {
            string md = new ReportBuilder("T").Metadata("    Operator ", "J").Metadata("\tStation", "3").ToMarkdown();
            Check.True(md.Contains("\n**Operator:** J  \n**Station:** 3\n"), md);
        }

        // ---------- ReportTemplates.Comparison ----------

        private static (string Threshold, string Change, bool Warned) Read(ReportBuilder r)
        {
            string md = r.ToMarkdown();
            Match t = Regex.Match(md, @"changed by ([-+0-9.Ee]+)% or more");
            Match c = Regex.Match(md, @"Largest relative change: m, ([-+0-9.Ee]+)%");
            Check.True(t.Success && c.Success, md);
            return (t.Groups[1].Value, c.Groups[1].Value, md.Contains("metric(s) changed"));
        }

        private static double Num(string s) => double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

        // The flag compared the exact change and the text showed it rounded: 4.996% printed "+5%" beside "No metric changed
        // by 5% or more", and a threshold of 0.014 printed "0.01%" beside an unflagged "+0.01%".
        public void Comparison_ChangeAndThresholdPrintOnTheRightSides()
        {
            var (t, c, w) = Read(ReportTemplates.Comparison("C", "A", "B", new[] { new ComparisonRow("m", 100, 104.996) }));
            Check.Equal("5", t);
            Check.False(w);
            Check.True(Num(c) < 5, c);
            (t, c, w) = Read(ReportTemplates.Comparison("C", "A", "B", new[] { new ComparisonRow("m", 100, 100.012) }, flagPercent: 0.014));
            Check.Equal("0.014", t);
            Check.True(!w && Num(c) < 0.014, c);
            Check.Equal("+5", Read(ReportTemplates.Comparison("C", "A", "B", new[] { new ComparisonRow("m", 100, 105) })).Change);

            // Graded over random thresholds and changes near them: the threshold prints exactly, and the printed change is
            // on the same side of it as the flag says.
            var rng = new Random(222);
            for (int trial = 0; trial < 2000; trial++)
            {
                double threshold = Math.Round(rng.NextDouble() * 20, rng.Next(5)) + (trial % 7 == 0 ? 1e-9 : 0);
                if (threshold == 0) continue;
                double p = threshold * (1 + (rng.NextDouble() - 0.5) * Math.Pow(10, -rng.Next(1, 9))) * (rng.Next(2) == 0 ? -1 : 1);
                var row = new ComparisonRow("m", 100, 100 + p);
                (t, c, w) = Read(ReportTemplates.Comparison("C", "A", "B", new[] { row }, flagPercent: threshold));
                Check.Equal(threshold, Num(t), "threshold " + threshold.ToString("R", CultureInfo.InvariantCulture));
                Check.Equal(Math.Abs(row.PercentChange) >= threshold, w, "flag");
                Check.Equal(w, Math.Abs(Num(c)) >= Num(t), $"change {c} against {t}");
            }
        }

#if !NETFRAMEWORK
        // ---------- SettingsStore ----------

        public sealed class S { public int N { get; set; } }

        private static string NewDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), "tb-r222-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        // A reader holding the file for most of a second (another process loading it, a scanner) outlasted the save's ten
        // tries 50 ms apart, and the save threw. The save waits about two seconds.
        public void Settings_SaveOutlastsAReaderHoldingTheFileForASecond()
        {
            string dir = NewDir();
            try
            {
                string path = Path.Combine(dir, "s.json");
                var store = SettingsStore<S>.LoadFrom(path);
                store.Update(v => v.N = 1);
                using var opened = new ManualResetEventSlim(false);
                var holder = new Thread(() =>
                {
                    using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        opened.Set();
                        using var never = new ManualResetEventSlim(false);
                        never.Wait(900);
                    }
                });
                holder.Start();
                opened.Wait();
                store.Update(v => v.N = 2);
                holder.Join();
                Check.Equal(2, SettingsStore<S>.LoadFrom(path).Value.N);
            }
            finally { Directory.Delete(dir, true); }
        }

        // Instances saving one file in one process raced each other's File.Replace, which failed partway: it left its
        // "s.json~RF....TMP" backups behind (dozens a run here) and under load one save missed every retry and threw "Unable
        // to remove the file to be replaced" (about one run in 50 of six instances saving 60 times each). They take turns.
        public void Settings_ManyInstancesSavingOneFile_NeverFail()
        {
            string dir = NewDir();
            try
            {
                string path = Path.Combine(dir, "s.json");
                SettingsStore<S>.LoadFrom(path).Update(v => v.N = 1);
                var errors = new ConcurrentQueue<Exception>();
                var threads = Enumerable.Range(0, 8).Select(t => new Thread(() =>
                {
                    var s = SettingsStore<S>.LoadFrom(path);
                    for (int i = 0; i < 50; i++)
                    {
                        try { s.Update(v => v.N = t * 1000 + i + 1); }
                        catch (Exception ex) { errors.Enqueue(ex); }
                    }
                })).ToList();
                threads.ForEach(t => t.Start());
                threads.ForEach(t => t.Join());
                Check.True(errors.IsEmpty, errors.Count + " failed saves; first: " + errors.FirstOrDefault());
                int n = SettingsStore<S>.LoadFrom(path).Value.N;
                Check.True(n % 1000 == 50, "the file holds some instance's last save: " + n);
                Check.Equal(0, Directory.GetFiles(dir, "*.tmp").Length, "no temp file left");
            }
            finally { Directory.Delete(dir, true); }
        }
#endif
    }
}
