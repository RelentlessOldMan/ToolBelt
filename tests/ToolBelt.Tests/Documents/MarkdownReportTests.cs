using System;
using System.Collections.Generic;
using ToolBelt.Documents;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Documents
{
    public sealed class MarkdownReportTests
    {
        public void HeadingsAndParagraph()
        {
            var md = new MarkdownReport().H1("Title").H2("Section").Paragraph("Body text.").ToString();
            Check.True(md.Contains("# Title"), md);
            Check.True(md.Contains("## Section"), md);
            Check.True(md.Contains("Body text."), md);
        }

        public void BlocksSeparatedByBlankLines()
        {
            var md = new MarkdownReport().Paragraph("one").Paragraph("two").ToString();
            Check.True(md.Contains("one\n\ntwo"), md); // a blank line between blocks
        }

        public void BulletAndNumberedLists()
        {
            var md = new MarkdownReport()
                .BulletList(new[] { "a", "b" })
                .NumberedList(new[] { "x", "y" })
                .ToString();
            Check.True(md.Contains("- a\n- b"), md);
            Check.True(md.Contains("1. x\n1. y"), md);
        }

        public void Table_WithAlignment()
        {
            var md = new MarkdownReport().Table(
                new[] { "Name", "Value" },
                new List<IReadOnlyList<string>> { new[] { "pi", "3.14" }, new[] { "e", "2.72" } },
                new[] { MarkdownAlign.Left, MarkdownAlign.Right }).ToString();
            Check.True(md.Contains("| Name | Value |"), md);
            Check.True(md.Contains("| :--- | ---: |"), md);
            Check.True(md.Contains("| pi | 3.14 |"), md);
        }

        public void Table_EscapesPipesAndPadsRows()
        {
            var md = new MarkdownReport().Table(
                new[] { "A", "B" },
                new List<IReadOnlyList<string>> { new[] { "x|y" }, new[] { "p", "q" } }).ToString();
            Check.True(md.Contains("x\\|y"), md);   // pipe escaped
            Check.True(md.Contains("| x\\|y |  |"), md); // short row padded to 2 columns
        }

        public void CodeBlock_Fenced()
        {
            var md = new MarkdownReport().CodeBlock("var x = 1;", "csharp").ToString();
            Check.True(md.Contains("```csharp\nvar x = 1;\n```"), md);
        }

        public void Quote_PrefixesEachLine()
        {
            var md = new MarkdownReport().Quote("line1\nline2").ToString();
            Check.True(md.Contains("> line1\n> line2"), md);
        }

        public void InlineHelpers_Escape()
        {
            Check.Equal("**bold**", MarkdownReport.Bold("bold"));
            Check.Equal("*it*", MarkdownReport.Italic("it"));
            Check.Equal("[a\\*b](http://x)", MarkdownReport.Link("a*b", "http://x"));
            Check.Equal("`co\\`de`", MarkdownReport.Code("co`de")); // inline code escapes backticks
        }

        public void ImageAndRule()
        {
            var md = new MarkdownReport().Image("alt", "pic.png").HorizontalRule().ToString();
            Check.True(md.Contains("![alt](pic.png)"), md);
            Check.True(md.Contains("---"), md);
        }

        public void TrailingNewlineExactlyOne()
        {
            var md = new MarkdownReport().Paragraph("x").ToString();
            Check.True(md.EndsWith("x\n"), "should end with a single newline");
            Check.False(md.EndsWith("x\n\n"), "no double trailing newline");
        }

        public void InvalidArguments_Throw()
        {
            Check.Throws<ArgumentOutOfRangeException>(() => new MarkdownReport().Heading(7, "x"));
            Check.Throws<ArgumentNullException>(() => new MarkdownReport().Paragraph(null!));
            Check.Throws<ArgumentException>(() => new MarkdownReport().Table(Array.Empty<string>(), new List<IReadOnlyList<string>>()));
        }
    }
}
