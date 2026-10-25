// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ToolBelt.Visualization
{
    /// <summary>
    /// A fluent builder for SVG vector graphics — the resolution-independent counterpart to the raster
    /// <see cref="ImageBuffer"/>. Emits well-formed SVG 1.1 with a <c>viewBox</c>; coordinates are always
    /// formatted with the invariant culture (so no comma-decimal surprises) and text is XML-escaped (characters
    /// XML 1.0 forbids become U+FFFD). A NaN or infinite coordinate throws: SVG cannot represent it.
    /// </summary>
    public sealed class SvgDocument
    {
        private readonly StringBuilder _body = new StringBuilder();
        private readonly double _width;
        private readonly double _height;

        /// <summary>Creates a canvas of the given size (user units, which map 1:1 to the viewBox).</summary>
        public SvgDocument(double width, double height)
        {
            if (!(width > 0) || double.IsInfinity(width)) throw new ArgumentOutOfRangeException(nameof(width), width, "Width must be positive and finite.");
            if (!(height > 0) || double.IsInfinity(height)) throw new ArgumentOutOfRangeException(nameof(height), height, "Height must be positive and finite.");
            _width = width;
            _height = height;
        }

        /// <summary>Draws a rectangle. A null stroke omits the outline; a null fill omits the fill.</summary>
        public SvgDocument Rectangle(double x, double y, double width, double height,
            string? fill = "black", string? stroke = null, double strokeWidth = 1, double cornerRadius = 0)
        {
            _body.Append("<rect x=\"").Append(F(x)).Append("\" y=\"").Append(F(y))
                 .Append("\" width=\"").Append(F(width)).Append("\" height=\"").Append(F(height)).Append('"');
            if (cornerRadius > 0) _body.Append(" rx=\"").Append(F(cornerRadius)).Append('"');
            AppendPaint(fill, stroke, strokeWidth);
            _body.Append("/>\n");
            return this;
        }

        /// <summary>Draws a line segment.</summary>
        public SvgDocument Line(double x1, double y1, double x2, double y2, string stroke = "black", double strokeWidth = 1)
        {
            if (stroke is null) throw new ArgumentNullException(nameof(stroke));
            _body.Append("<line x1=\"").Append(F(x1)).Append("\" y1=\"").Append(F(y1))
                 .Append("\" x2=\"").Append(F(x2)).Append("\" y2=\"").Append(F(y2))
                 .Append("\" stroke=\"").Append(EscapeAttr(stroke)).Append("\" stroke-width=\"").Append(F(strokeWidth))
                 .Append("\"/>\n");
            return this;
        }

        /// <summary>Draws a circle.</summary>
        public SvgDocument Circle(double cx, double cy, double radius,
            string? fill = "black", string? stroke = null, double strokeWidth = 1)
        {
            if (!(radius >= 0)) throw new ArgumentOutOfRangeException(nameof(radius), radius, "Radius must be non-negative.");
            _body.Append("<circle cx=\"").Append(F(cx)).Append("\" cy=\"").Append(F(cy))
                 .Append("\" r=\"").Append(F(radius)).Append('"');
            AppendPaint(fill, stroke, strokeWidth);
            _body.Append("/>\n");
            return this;
        }

        /// <summary>Draws an open polyline through the given points.</summary>
        public SvgDocument Polyline(IEnumerable<(double X, double Y)> points, string stroke = "black", double strokeWidth = 1)
            => Poly("polyline", points, null, stroke, strokeWidth);

        /// <summary>Draws a closed, optionally filled polygon.</summary>
        public SvgDocument Polygon(IEnumerable<(double X, double Y)> points,
            string? fill = "black", string? stroke = null, double strokeWidth = 1)
            => Poly("polygon", points, fill, stroke, strokeWidth);

        /// <summary>Draws raw path data (the <c>d</c> attribute of an SVG <c>&lt;path&gt;</c>).</summary>
        public SvgDocument Path(string data, string? fill = "black", string? stroke = null, double strokeWidth = 1)
        {
            if (data is null) throw new ArgumentNullException(nameof(data));
            _body.Append("<path d=\"").Append(EscapeAttr(data)).Append('"');
            AppendPaint(fill, stroke, strokeWidth);
            _body.Append("/>\n");
            return this;
        }

        /// <summary>Draws a text label. <paramref name="anchor"/> is one of start/middle/end.</summary>
        public SvgDocument Text(double x, double y, string text,
            double fontSize = 12, string fill = "black", string anchor = "start", string fontFamily = "sans-serif")
        {
            if (text is null) throw new ArgumentNullException(nameof(text));
            if (fill is null) throw new ArgumentNullException(nameof(fill));
            if (anchor is null) throw new ArgumentNullException(nameof(anchor));
            if (fontFamily is null) throw new ArgumentNullException(nameof(fontFamily));
            _body.Append("<text x=\"").Append(F(x)).Append("\" y=\"").Append(F(y))
                 .Append("\" font-size=\"").Append(F(fontSize)).Append("\" font-family=\"").Append(EscapeAttr(fontFamily))
                 .Append("\" fill=\"").Append(EscapeAttr(fill)).Append("\" text-anchor=\"").Append(EscapeAttr(anchor))
                 .Append("\">").Append(EscapeText(text)).Append("</text>\n");
            return this;
        }

        /// <summary>Appends already-formed SVG markup verbatim (caller owns its correctness).</summary>
        public SvgDocument Raw(string svg)
        {
            if (svg is null) throw new ArgumentNullException(nameof(svg));
            _body.Append(svg);
            if (!svg.EndsWith("\n", StringComparison.Ordinal)) _body.Append('\n');
            return this;
        }

        /// <summary>The finished SVG document.</summary>
        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
            sb.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"").Append(F(_width))
              .Append("\" height=\"").Append(F(_height))
              .Append("\" viewBox=\"0 0 ").Append(F(_width)).Append(' ').Append(F(_height)).Append("\">\n");
            sb.Append(_body);
            sb.Append("</svg>\n");
            return sb.ToString();
        }

        private SvgDocument Poly(string tag, IEnumerable<(double X, double Y)> points,
            string? fill, string? stroke, double strokeWidth)
        {
            if (points is null) throw new ArgumentNullException(nameof(points));
            var sb = new StringBuilder();
            bool first = true;
            foreach (var (x, y) in points)
            {
                if (!first) sb.Append(' ');
                sb.Append(F(x)).Append(',').Append(F(y));
                first = false;
            }
            _body.Append('<').Append(tag).Append(" points=\"").Append(sb).Append('"');
            // A polyline defaults to no fill; a polygon honors the fill argument.
            if (tag == "polyline") AppendPaint("none", stroke ?? "black", strokeWidth);
            else AppendPaint(fill, stroke, strokeWidth);
            _body.Append("/>\n");
            return this;
        }

        private void AppendPaint(string? fill, string? stroke, double strokeWidth)
        {
            _body.Append(" fill=\"").Append(fill is null ? "none" : EscapeAttr(fill)).Append('"');
            if (stroke != null)
                _body.Append(" stroke=\"").Append(EscapeAttr(stroke)).Append("\" stroke-width=\"").Append(F(strokeWidth)).Append('"');
        }

        // NaN/∞ used to be written as "NaN"/"Infinity" (an unrenderable document) and -0.0000001 as "-0".
        private static string F(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "SVG numbers must be finite.");
            string s = value.ToString("0.######", CultureInfo.InvariantCulture);
            return s == "-0" ? "0" : s;
        }

        private static string EscapeText(string text) => Escape(text, attribute: false);

        private static string EscapeAttr(string text) => Escape(text, attribute: true);

        private static string Escape(string text, bool attribute)
        {
            var sb = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                switch (ch)
                {
                    case '&': sb.Append("&amp;"); break;
                    case '<': sb.Append("&lt;"); break;
                    case '>': sb.Append("&gt;"); break;
                    case '"' when attribute: sb.Append("&quot;"); break;
                    default: sb.Append(IsXmlIllegal(text, i) ? '\uFFFD' : ch); break;   // e.g. an ANSI escape in a label
                }
            }
            return sb.ToString();
        }

        // Characters XML 1.0 forbids: C0 controls other than tab/LF/CR, U+FFFE/U+FFFF, and unpaired surrogates.
        private static bool IsXmlIllegal(string s, int i)
        {
            char c = s[i];
            if (c < 0x20) return c != '\t' && c != '\n' && c != '\r';
            if (c == '\uFFFE' || c == '\uFFFF') return true;
            if (char.IsHighSurrogate(c)) return i + 1 >= s.Length || !char.IsLowSurrogate(s[i + 1]);
            if (char.IsLowSurrogate(c)) return i == 0 || !char.IsHighSurrogate(s[i - 1]);
            return false;
        }
    }
}
