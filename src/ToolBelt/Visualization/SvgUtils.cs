// ToolBelt drop-in — also copy Visualization/ImageBuffer.cs (for Rgba).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ToolBelt.Visualization
{
    /// <summary>
    /// Shared SVG emission primitives for the vector renderers: XML escaping, culture-invariant number and color
    /// formatting, a path-data builder, transform strings, and a text-width estimate for layout. One audited
    /// implementation instead of each renderer re-deriving them (the drop-in rule still applies: a renderer that
    /// must stand alone may inline what it needs).
    /// </summary>
    public static class SvgUtils
    {
        /// <summary>Escapes text content (<c>&amp;</c>, <c>&lt;</c>, <c>&gt;</c>).</summary>
        public static string EscapeText(string text)
        {
            if (text is null) throw new ArgumentNullException(nameof(text));
            return Escape(text, attribute: false);
        }

        /// <summary>Escapes an attribute value (text escaping plus both quote characters).</summary>
        public static string EscapeAttribute(string text)
        {
            if (text is null) throw new ArgumentNullException(nameof(text));
            return Escape(text, attribute: true);
        }

        /// <summary>
        /// Formats a coordinate or length: invariant culture, at most six decimals, no trailing zeros, and no
        /// negative zero. Throws for NaN or infinity, which SVG cannot represent — a non-finite coordinate is
        /// always a bug upstream, so it fails here rather than producing an unrenderable document.
        /// </summary>
        public static string Number(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "SVG numbers must be finite.");
            string s = value.ToString("0.######", CultureInfo.InvariantCulture);
            return s == "-0" ? "0" : s;
        }

        /// <summary>An SVG color, <c>#rrggbb</c> (alpha is separate — see <see cref="Opacity"/>).</summary>
        public static string Color(Rgba color)
            => "#" + color.R.ToString("x2", CultureInfo.InvariantCulture)
                   + color.G.ToString("x2", CultureInfo.InvariantCulture)
                   + color.B.ToString("x2", CultureInfo.InvariantCulture);

        /// <summary>The color's alpha as an SVG opacity in [0, 1].</summary>
        public static string Opacity(Rgba color) => Number(Math.Round(color.A / 255.0, 4));

        /// <summary>
        /// <c>fill="…"</c> (plus <c>fill-opacity</c> when not opaque) for an optional color; null yields <c>fill="none"</c>.
        /// </summary>
        public static string FillAttributes(Rgba? fill)
        {
            if (fill is null) return "fill=\"none\"";
            Rgba c = fill.Value;
            return c.A == 255
                ? "fill=\"" + Color(c) + "\""
                : "fill=\"" + Color(c) + "\" fill-opacity=\"" + Opacity(c) + "\"";
        }

        /// <summary>
        /// <c>stroke="…" stroke-width="…"</c> (plus opacity and an optional dash pattern) for an optional color;
        /// null yields an empty string (no stroke).
        /// </summary>
        public static string StrokeAttributes(Rgba? stroke, double width = 1, string? dashArray = null)
        {
            if (stroke is null) return string.Empty;
            Rgba c = stroke.Value;
            var sb = new StringBuilder();
            sb.Append("stroke=\"").Append(Color(c)).Append("\" stroke-width=\"").Append(Number(width)).Append('"');
            if (c.A != 255) sb.Append(" stroke-opacity=\"").Append(Opacity(c)).Append('"');
            if (!string.IsNullOrEmpty(dashArray)) sb.Append(" stroke-dasharray=\"").Append(EscapeAttribute(dashArray!)).Append('"');
            return sb.ToString();
        }

        // ---------- transforms ----------

        public static string Translate(double x, double y) => "translate(" + Number(x) + " " + Number(y) + ")";

        public static string Scale(double sx, double sy) => "scale(" + Number(sx) + " " + Number(sy) + ")";

        /// <summary>Rotation in degrees, optionally about (cx, cy).</summary>
        public static string Rotate(double degrees, double cx = 0, double cy = 0)
            => cx == 0 && cy == 0
                ? "rotate(" + Number(degrees) + ")"
                : "rotate(" + Number(degrees) + " " + Number(cx) + " " + Number(cy) + ")";

        public static string Matrix(double a, double b, double c, double d, double e, double f)
            => "matrix(" + Number(a) + " " + Number(b) + " " + Number(c) + " " + Number(d) + " " + Number(e) + " " + Number(f) + ")";

        /// <summary>Joins transforms; SVG applies them right-to-left (the last listed acts first).</summary>
        public static string Combine(params string[] transforms)
        {
            if (transforms is null) throw new ArgumentNullException(nameof(transforms));
            var parts = new List<string>();
            foreach (string t in transforms)
                if (!string.IsNullOrEmpty(t)) parts.Add(t);
            return string.Join(" ", parts);
        }

        // ---------- text metrics ----------

        /// <summary>
        /// Estimates the rendered width of <paramref name="text"/> in a Helvetica/Arial-like sans-serif at
        /// <paramref name="fontSize"/>, from the standard Helvetica advance widths for printable ASCII. Non-ASCII
        /// letters use an average width, East Asian wide characters a full em, and combining marks zero. Bold is
        /// approximated by a uniform widening. An estimate for layout (centering, right-aligning, avoiding overlap)
        /// — the real width depends on the viewer's font, so leave a little slack.
        /// </summary>
        public static double EstimateTextWidth(string text, double fontSize, bool bold = false)
        {
            if (text is null) throw new ArgumentNullException(nameof(text));
            if (!(fontSize >= 0)) throw new ArgumentOutOfRangeException(nameof(fontSize), fontSize, "Font size must be non-negative.");
            double units = 0;
            foreach (char ch in text)
                units += AdvanceWidth(ch);
            return units / 1000.0 * fontSize * (bold ? BoldFactor : 1.0);
        }

        private const double BoldFactor = 1.07;

        private static int AdvanceWidth(char ch)
        {
            if (ch >= 32 && ch <= 126) return HelveticaWidths[ch - 32];
            if (ch >= '̀' && ch <= 'ͯ') return 0;          // combining diacritics
            if (char.IsLowSurrogate(ch)) return 0;                   // counted with its high surrogate
            if (char.IsHighSurrogate(ch)) return 1000;               // astral (emoji etc.): about an em
            if (IsWide(ch)) return 1000;
            if (char.IsControl(ch)) return 0;
            return 556;                                              // average Latin/Greek/Cyrillic letter
        }

        private static bool IsWide(char ch)
            => (ch >= 'ᄀ' && ch <= 'ᅟ')    // Hangul Jamo
            || (ch >= '⺀' && ch <= '꓏')    // CJK radicals .. Yi
            || (ch >= '가' && ch <= '힣')    // Hangul syllables
            || (ch >= '豈' && ch <= '﫿')    // CJK compatibility ideographs
            || (ch >= '︰' && ch <= '﹏')    // CJK compatibility forms
            || (ch >= '＀' && ch <= '｠')    // fullwidth forms
            || (ch >= '￠' && ch <= '￦');

        // Helvetica advance widths (1/1000 em) for U+0020..U+007E, from the standard font metrics.
        private static readonly int[] HelveticaWidths =
        {
            278, 278, 355, 556, 556, 889, 667, 191, 333, 333, 389, 584, 278, 333, 278, 278, // space ! " # $ % & ' ( ) * + , - . /
            556, 556, 556, 556, 556, 556, 556, 556, 556, 556,                               // 0-9
            278, 278, 584, 584, 584, 556, 1015,                                             // : ; < = > ? @
            667, 667, 722, 722, 667, 611, 778, 722, 278, 500, 667, 556, 833,                // A-M
            722, 778, 667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611,                // N-Z
            278, 278, 278, 469, 556, 333,                                                   // [ \ ] ^ _ `
            556, 556, 500, 556, 556, 278, 556, 556, 222, 222, 500, 222, 833,                // a-m
            556, 556, 556, 556, 333, 500, 278, 556, 500, 722, 500, 500, 500,                // n-z
            334, 260, 334, 584,                                                             // { | } ~
        };

        private static string Escape(string text, bool attribute)
        {
            StringBuilder? sb = null;
            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                string? rep = ch switch
                {
                    '&' => "&amp;",
                    '<' => "&lt;",
                    '>' => "&gt;",
                    '"' when attribute => "&quot;",
                    '\'' when attribute => "&#39;",
                    _ => IsXmlIllegal(text, i) ? "�" : null,   // e.g. ANSI escapes in a series name: XML 1.0 forbids them
                };
                if (rep is null)
                {
                    sb?.Append(text[i]);
                    continue;
                }
                if (sb is null)
                {
                    sb = new StringBuilder(text.Length + 16);
                    sb.Append(text, 0, i);
                }
                sb.Append(rep);
            }
            return sb?.ToString() ?? text;
        }

        // Characters XML 1.0 forbids: C0 controls other than tab/LF/CR, U+FFFE/U+FFFF, and unpaired surrogates.
        private static bool IsXmlIllegal(string s, int i)
        {
            char c = s[i];
            if (c < 0x20) return c != '\t' && c != '\n' && c != '\r';
            if (c == '￾' || c == '￿') return true;
            if (char.IsHighSurrogate(c)) return i + 1 >= s.Length || !char.IsLowSurrogate(s[i + 1]);
            if (char.IsLowSurrogate(c)) return i == 0 || !char.IsHighSurrogate(s[i - 1]);
            return false;
        }
    }

    /// <summary>
    /// Builds SVG path data (the <c>d</c> attribute) with absolute commands and culture-invariant numbers:
    /// <c>new PathBuilder().MoveTo(0, 0).LineTo(10, 0).Close()</c>. Non-finite coordinates throw.
    /// </summary>
    public sealed class PathBuilder
    {
        private readonly StringBuilder _d = new StringBuilder();

        /// <summary>True until a command has been added.</summary>
        public bool IsEmpty => _d.Length == 0;

        public PathBuilder MoveTo(double x, double y) => Cmd('M', x, y);
        public PathBuilder LineTo(double x, double y) => Cmd('L', x, y);
        public PathBuilder HorizontalTo(double x) => Cmd('H', x);
        public PathBuilder VerticalTo(double y) => Cmd('V', y);

        /// <summary>Cubic Bézier to (x, y) with control points (x1, y1) and (x2, y2).</summary>
        public PathBuilder CubicTo(double x1, double y1, double x2, double y2, double x, double y) => Cmd('C', x1, y1, x2, y2, x, y);

        /// <summary>Quadratic Bézier to (x, y) with control point (x1, y1).</summary>
        public PathBuilder QuadraticTo(double x1, double y1, double x, double y) => Cmd('Q', x1, y1, x, y);

        /// <summary>Elliptical arc to (x, y).</summary>
        public PathBuilder ArcTo(double rx, double ry, double rotationDegrees, bool largeArc, bool sweep, double x, double y)
        {
            Separator();
            _d.Append('A').Append(SvgUtils.Number(rx)).Append(' ').Append(SvgUtils.Number(ry)).Append(' ')
              .Append(SvgUtils.Number(rotationDegrees)).Append(' ').Append(largeArc ? '1' : '0').Append(' ')
              .Append(sweep ? '1' : '0').Append(' ').Append(SvgUtils.Number(x)).Append(' ').Append(SvgUtils.Number(y));
            return this;
        }

        public PathBuilder Close()
        {
            Separator();
            _d.Append('Z');
            return this;
        }

        /// <summary>A polyline (or, with <paramref name="closed"/>, polygon) through the points; empty input yields an empty path.</summary>
        public static PathBuilder FromPoints(IEnumerable<(double X, double Y)> points, bool closed = false)
        {
            if (points is null) throw new ArgumentNullException(nameof(points));
            var path = new PathBuilder();
            foreach (var (x, y) in points)
            {
                if (path.IsEmpty) path.MoveTo(x, y);
                else path.LineTo(x, y);
            }
            if (closed && !path.IsEmpty) path.Close();
            return path;
        }

        public override string ToString() => _d.ToString();

        private PathBuilder Cmd(char op, params double[] args)
        {
            Separator();
            _d.Append(op);
            for (int i = 0; i < args.Length; i++)
            {
                if (i > 0) _d.Append(' ');
                _d.Append(SvgUtils.Number(args[i]));
            }
            return this;
        }

        private void Separator()
        {
            if (_d.Length > 0) _d.Append(' ');
        }
    }
}
