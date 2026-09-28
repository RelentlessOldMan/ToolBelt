using System;
using System.Collections.Generic;
using System.Text;
using ToolBelt.Documents;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Documents
{
    public sealed class HtmlReportTests
    {
        public void FullDocument_HasScaffolding()
        {
            var html = new HtmlReport().Title("My Report").H1("Hi").ToString();
            Check.True(html.StartsWith("<!DOCTYPE html>"), html);
            Check.True(html.Contains("<title>My Report</title>"), html);
            Check.True(html.Contains("<style>"), "has embedded CSS");
            Check.True(html.Contains("<h1>Hi</h1>"), html);
            Check.True(html.TrimEnd().EndsWith("</html>"), html);
        }

        public void EscapesTextEverywhere()
        {
            var html = new HtmlReport().Paragraph("a<b>&\"c\"").ToString();
            Check.True(html.Contains("a&lt;b&gt;&amp;\"c\""), html); // < > & escaped in body text
            Check.False(html.Contains("<b>"), "raw tag must not survive escaping");
        }

        public void AttributeEscaping_PreventsBreakout()
        {
            var html = new HtmlReport().Image("a\"b", "u\"rl").ToString();
            Check.True(html.Contains("alt=\"a&quot;b\""), html);
            Check.True(html.Contains("src=\"u&quot;rl\""), html);
        }

        public void Table_Structure()
        {
            var html = new HtmlReport().Table(
                new[] { "H1", "H2" },
                new List<IReadOnlyList<string>> { new[] { "a", "b" } }).ToString();
            Check.True(html.Contains("<thead>"), html);
            Check.True(html.Contains("<th>H1</th><th>H2</th>"), html);
            Check.True(html.Contains("<td>a</td><td>b</td>"), html);
        }

        public void Lists()
        {
            var html = new HtmlReport().BulletList(new[] { "x" }).NumberedList(new[] { "y" }).ToString();
            Check.True(html.Contains("<ul>\n<li>x</li>\n</ul>"), html);
            Check.True(html.Contains("<ol>\n<li>y</li>\n</ol>"), html);
        }

        public void CodeBlock_WithLanguageClass()
        {
            var html = new HtmlReport().CodeBlock("f();", "js").ToString();
            Check.True(html.Contains("<pre><code class=\"language-js\">f();</code></pre>"), html);
        }

        public void ImageData_EmbedsBase64()
        {
            var png = new byte[] { 1, 2, 3, 4 };
            var html = new HtmlReport().ImageData("chart", png).ToString();
            string b64 = Convert.ToBase64String(png);
            Check.True(html.Contains($"src=\"data:image/png;base64,{b64}\""), html);
        }

        public void Fragment_HasNoScaffolding()
        {
            var frag = new HtmlReport().Paragraph("only").ToFragment();
            Check.Equal("<p>only</p>\n", frag);
            Check.False(frag.Contains("<html"), "fragment omits the document wrapper");
        }

        public void RawIsNotEscaped()
        {
            var html = new HtmlReport().Raw("<custom>ok</custom>").ToString();
            Check.True(html.Contains("<custom>ok</custom>"), html);
        }

        public void Link_Escapes()
        {
            Check.Equal("<a href=\"http://x?a=1&amp;b=2\">go&lt;</a>", HtmlReport.Link("go<", "http://x?a=1&b=2"));
        }

        public void InvalidArguments_Throw()
        {
            Check.Throws<ArgumentOutOfRangeException>(() => new HtmlReport().Heading(0, "x"));
            Check.Throws<ArgumentNullException>(() => new HtmlReport().Paragraph(null!));
            Check.Throws<ArgumentNullException>(() => new HtmlReport().ImageData("a", null!));
        }
    }
}
