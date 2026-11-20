// ToolBelt drop-in — also copy Visualization/ImageBuffer.cs (for Rgba).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
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
        /// <summary>
        /// Escapes text content: <c>&amp;</c>, <c>&lt;</c> and <c>&gt;</c>, a carriage return as <c>&amp;#13;</c> (a parser
        /// turns it into a line feed), and each character XML 1.0 forbids (control characters, unpaired surrogates,
        /// U+FFFE and U+FFFF) as U+FFFD.
        /// </summary>
        public static string EscapeText(string text)
        {
            if (text is null) throw new ArgumentNullException(nameof(text));
            return Escape(text, attribute: false);
        }

        /// <summary>
        /// Escapes an attribute value: text escaping plus both quote characters, and tab and line feed as character
        /// references (a parser turns them into spaces).
        /// </summary>
        public static string EscapeAttribute(string text)
        {
            if (text is null) throw new ArgumentNullException(nameof(text));
            return Escape(text, attribute: true);
        }

        /// <summary>
        /// Formats a coordinate or length: invariant culture, the exact value rounded to at most six decimals (halves
        /// away from zero), no trailing zeros, and no negative zero. Throws for NaN or infinity, which SVG cannot represent — a non-finite coordinate is
        /// always a bug upstream, so it fails here rather than producing an unrenderable document.
        /// </summary>
        public static string Number(double value) => Number(value, nameof(value));

        // Same, naming the caller's argument in the exception.
        internal static string Number(double value, string paramName)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new ArgumentOutOfRangeException(paramName, value, "SVG numbers must be finite.");
            return SixDecimals(value);
        }

        // The double's exact value rounded to six decimals (halves away from zero), without trailing zeros or a negative
        // zero. "0.######" rounded to 15 significant digits first: 0.7551384999999999 came out 0.755139, 4503599627370497
        // as 4503599627370500, and values past about 7e11 differed between .NET Framework and .NET.
        private static string SixDecimals(double value)
        {
            if (value == Math.Floor(value) && Math.Abs(value) < 9007199254740992.0)
                return ((long)value).ToString(CultureInfo.InvariantCulture);       // an integer, exactly (−0 gives "0")
            long bits = BitConverter.DoubleToInt64Bits(value);
            bool negative = bits < 0;
            int exponent = (int)((bits >> 52) & 0x7FF);
            long mantissa = bits & 0xFFFFFFFFFFFFFL;
            if (exponent == 0) exponent = 1; else mantissa |= 1L << 52;
            exponent -= 1075;                                                       // |value| = mantissa · 2^exponent
            BigInteger scaled;                                                      // round(|value| · 10⁶)
            if (exponent >= 0) scaled = (new BigInteger(mantissa) << exponent) * 1000000;
            else
            {
                BigInteger product = new BigInteger(mantissa) * 1000000;
                int shift = -exponent;
                scaled = product >> shift;
                if (!(product - (scaled << shift) < BigInteger.One << (shift - 1))) scaled += 1;
            }
            if (scaled.IsZero) return "0";
            BigInteger whole = BigInteger.DivRem(scaled, 1000000, out BigInteger fraction);
            var sb = new StringBuilder();
            if (negative) sb.Append('-');
            sb.Append(whole.ToString(CultureInfo.InvariantCulture));
            if (!fraction.IsZero) sb.Append('.').Append(((int)fraction).ToString("D6", CultureInfo.InvariantCulture).TrimEnd('0'));
            return sb.ToString();
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
        /// null yields an empty string (no stroke). A positive width too small to write at six decimals is written as
        /// 0.000001, the smallest that is still a stroke.
        /// </summary>
        public static string StrokeAttributes(Rgba? stroke, double width = 1, string? dashArray = null)
        {
            if (stroke is null) return string.Empty;
            if (!(width >= 0)) throw new ArgumentOutOfRangeException(nameof(width), width, "Stroke width must be non-negative.");   // SVG rejects a negative one
            Rgba c = stroke.Value;
            var sb = new StringBuilder();
            // A positive width written as "0" (1e-7) dropped the stroke: stroke-width="0" draws nothing.
            string written = Number(width, nameof(width));
            if (written == "0" && width > 0) written = "0.000001";
            sb.Append("stroke=\"").Append(Color(c)).Append("\" stroke-width=\"").Append(written).Append('"');
            if (c.A != 255) sb.Append(" stroke-opacity=\"").Append(Opacity(c)).Append('"');
            if (!string.IsNullOrEmpty(dashArray)) sb.Append(" stroke-dasharray=\"").Append(EscapeAttribute(dashArray!)).Append('"');
            return sb.ToString();
        }

        // ---------- transforms ----------

        public static string Translate(double x, double y) => "translate(" + Number(x, nameof(x)) + " " + Number(y, nameof(y)) + ")";

        public static string Scale(double sx, double sy) => "scale(" + Number(sx, nameof(sx)) + " " + Number(sy, nameof(sy)) + ")";

        /// <summary>Rotation in degrees, optionally about (cx, cy).</summary>
        public static string Rotate(double degrees, double cx = 0, double cy = 0)
            => cx == 0 && cy == 0
                ? "rotate(" + Number(degrees, nameof(degrees)) + ")"
                : "rotate(" + Number(degrees, nameof(degrees)) + " " + Number(cx, nameof(cx)) + " " + Number(cy, nameof(cy)) + ")";

        public static string Matrix(double a, double b, double c, double d, double e, double f)
            => "matrix(" + Number(a, nameof(a)) + " " + Number(b, nameof(b)) + " " + Number(c, nameof(c)) + " "
               + Number(d, nameof(d)) + " " + Number(e, nameof(e)) + " " + Number(f, nameof(f)) + ")";

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
        /// letters use an average width, East Asian wide characters and emoji a full em (an emoji joined by U+200D, or a
        /// flag's pair of regional indicators, counts once), and combining marks, joiners, variation selectors and line breaks (including U+2028 and U+2029) zero
        /// (classed by Unicode 15.0 on every framework, so the estimate is the same everywhere). Bold is approximated by a uniform widening. An estimate for layout (centering, right-aligning, avoiding overlap)
        /// — the real width depends on the viewer's font, so leave a little slack.
        /// </summary>
        public static double EstimateTextWidth(string text, double fontSize, bool bold = false)
            => EstimateWidth(text, fontSize, bold);

        /// <summary>
        /// The attributes that squeeze a text element into <paramref name="maxWidth"/> pixels (<c>textLength</c> with
        /// <c>lengthAdjust="spacingAndGlyphs"</c>, led by a space) when its estimated width is wider, else "". Append
        /// them inside the element's start tag so a label too long for its room stays on the chart, whole. A
        /// <paramref name="maxWidth"/> of 0 or less, or ∞, sets no limit, and so does one that writes as 0 at six decimals
        /// (below 0.0000005).
        /// </summary>
        public static string FitWidth(string text, double fontSize, double maxWidth, bool bold = false)
        {
            if (double.IsNaN(maxWidth)) throw new ArgumentOutOfRangeException(nameof(maxWidth), maxWidth, "Width must not be NaN.");
            double w = EstimateWidth(text, fontSize, bold);
            // A positive width written as "0" (1e-7) squeezed the text to nothing: textLength="0", the very value that sets
            // no limit.
            string written = maxWidth > 0 && !double.IsInfinity(maxWidth) ? Number(maxWidth) : "0";
            return written != "0" && w > maxWidth
                ? " textLength=\"" + written + "\" lengthAdjust=\"spacingAndGlyphs\""
                : "";
        }

        private static double EstimateWidth(string text, double fontSize, bool bold)
        {
            if (text is null) throw new ArgumentNullException(nameof(text));
            if (!(fontSize >= 0) || double.IsInfinity(fontSize))                   // ∞ gave NaN for "" and ∞ otherwise
                throw new ArgumentOutOfRangeException(nameof(fontSize), fontSize, "Font size must be non-negative and finite.");
            double units = 0;
            bool joined = false, flagHalf = false;
            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                // Measured as EscapeText writes it: a control character or unpaired surrogate is written as U+FFFD and
                // counted as one.
                if (IsXmlIllegal(text, i)) ch = '\uFFFD';
                if (char.IsHighSurrogate(ch) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    // An astral character: nothing after a zero-width joiner (the parts of an emoji sequence render as one
                    // glyph), for a skin-tone modifier, or for an astral mark or tag; an em for a wide one (emoji, CJK
                    // extensions, kana and Tangut), and the average letter otherwise: every astral character counted an
                    // em, so a math-bold A or a Deseret letter measured 1000 where é is 556.
                    int cp = char.ConvertToUtf32(ch, text[i + 1]);
                    // A flag is a pair of regional indicators, paired left to right: the second of a pair counted
                    // another em.
                    bool indicator = cp >= 0x1F1E6 && cp <= 0x1F1FF, second = indicator && flagHalf;
                    flagHalf = indicator && !flagHalf;
                    bool zero = joined || second || (cp >= 0x1F3FB && cp <= 0x1F3FF) || IsZeroWidth(cp);
                    units += zero ? 0 : IsWideAstral(cp) ? 1000 : 556;
                    joined = false;
                    i++;
                    continue;
                }
                flagHalf = false;
                // A symbol joined by U+200D (the heart in a couple emoji) is part of the glyph before it.
                units += joined && IsOtherSymbol(ch) ? 0 : AdvanceWidth(ch);
                if (ch != '\uFE0F') joined = ch == '\u200D';             // a variation selector keeps the join
            }
            double width = units / 1000.0 * fontSize * (bold ? BoldFactor : 1.0);
            if (double.IsInfinity(width))                                          // a finite size near MaxValue gave ∞
                throw new ArgumentOutOfRangeException(nameof(fontSize), fontSize, "Font size too large: the width is not representable.");
            return width;
        }

        private const double BoldFactor = 1.07;

        private static int AdvanceWidth(char ch)
        {
            if (ch >= 32 && ch <= 126) return HelveticaWidths[ch - 32];
            if (ch == '\u00A0') return 278;                          // no-break space: a space
            if (ch == '\u2028' || ch == '\u2029') return 0;          // line and paragraph separators break, as \n does (they were 556)
            // Combining marks of every block (only U+0300–036F were zero), joiners, variation selectors and other
            // format characters take no advance.
            if (IsZeroWidth(ch)) return 0;
            if (IsWide(ch)) return 1000;
            if (char.IsControl(ch)) return 0;
            return 556;                                              // average Latin/Greek/Cyrillic letter
        }

        // Zero width: Unicode 15.0's non-spacing and enclosing marks and format characters. The framework's own tables
        // differ by version (440 code points measured differently on .NET Framework 4.8 and .NET 8), so they are fixed here.
        private static bool IsZeroWidth(int cp) => InRanges(ZeroWidthRanges, cp);

        private static bool IsOtherSymbol(char ch) => InRanges(OtherSymbolRanges, ch);

        // Ranges as inclusive start, end pairs in ascending order.
        private static bool InRanges(int[] ranges, int cp)
        {
            int lo = 0, hi = ranges.Length / 2 - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                if (cp < ranges[2 * mid]) hi = mid - 1;
                else if (cp > ranges[2 * mid + 1]) lo = mid + 1;
                else return true;
            }
            return false;
        }

        // Unicode 15.0 categories Mn, Me and Cf.
        private static readonly int[] ZeroWidthRanges =
        {
            0xAD, 0xAD, 0x300, 0x36F, 0x483, 0x489, 0x591, 0x5BD, 0x5BF, 0x5BF, 0x5C1, 0x5C2,
            0x5C4, 0x5C5, 0x5C7, 0x5C7, 0x600, 0x605, 0x610, 0x61A, 0x61C, 0x61C, 0x64B, 0x65F,
            0x670, 0x670, 0x6D6, 0x6DD, 0x6DF, 0x6E4, 0x6E7, 0x6E8, 0x6EA, 0x6ED, 0x70F, 0x70F,
            0x711, 0x711, 0x730, 0x74A, 0x7A6, 0x7B0, 0x7EB, 0x7F3, 0x7FD, 0x7FD, 0x816, 0x819,
            0x81B, 0x823, 0x825, 0x827, 0x829, 0x82D, 0x859, 0x85B, 0x890, 0x891, 0x898, 0x89F,
            0x8CA, 0x902, 0x93A, 0x93A, 0x93C, 0x93C, 0x941, 0x948, 0x94D, 0x94D, 0x951, 0x957,
            0x962, 0x963, 0x981, 0x981, 0x9BC, 0x9BC, 0x9C1, 0x9C4, 0x9CD, 0x9CD, 0x9E2, 0x9E3,
            0x9FE, 0x9FE, 0xA01, 0xA02, 0xA3C, 0xA3C, 0xA41, 0xA42, 0xA47, 0xA48, 0xA4B, 0xA4D,
            0xA51, 0xA51, 0xA70, 0xA71, 0xA75, 0xA75, 0xA81, 0xA82, 0xABC, 0xABC, 0xAC1, 0xAC5,
            0xAC7, 0xAC8, 0xACD, 0xACD, 0xAE2, 0xAE3, 0xAFA, 0xAFF, 0xB01, 0xB01, 0xB3C, 0xB3C,
            0xB3F, 0xB3F, 0xB41, 0xB44, 0xB4D, 0xB4D, 0xB55, 0xB56, 0xB62, 0xB63, 0xB82, 0xB82,
            0xBC0, 0xBC0, 0xBCD, 0xBCD, 0xC00, 0xC00, 0xC04, 0xC04, 0xC3C, 0xC3C, 0xC3E, 0xC40,
            0xC46, 0xC48, 0xC4A, 0xC4D, 0xC55, 0xC56, 0xC62, 0xC63, 0xC81, 0xC81, 0xCBC, 0xCBC,
            0xCBF, 0xCBF, 0xCC6, 0xCC6, 0xCCC, 0xCCD, 0xCE2, 0xCE3, 0xD00, 0xD01, 0xD3B, 0xD3C,
            0xD41, 0xD44, 0xD4D, 0xD4D, 0xD62, 0xD63, 0xD81, 0xD81, 0xDCA, 0xDCA, 0xDD2, 0xDD4,
            0xDD6, 0xDD6, 0xE31, 0xE31, 0xE34, 0xE3A, 0xE47, 0xE4E, 0xEB1, 0xEB1, 0xEB4, 0xEBC,
            0xEC8, 0xECE, 0xF18, 0xF19, 0xF35, 0xF35, 0xF37, 0xF37, 0xF39, 0xF39, 0xF71, 0xF7E,
            0xF80, 0xF84, 0xF86, 0xF87, 0xF8D, 0xF97, 0xF99, 0xFBC, 0xFC6, 0xFC6, 0x102D, 0x1030,
            0x1032, 0x1037, 0x1039, 0x103A, 0x103D, 0x103E, 0x1058, 0x1059, 0x105E, 0x1060, 0x1071, 0x1074,
            0x1082, 0x1082, 0x1085, 0x1086, 0x108D, 0x108D, 0x109D, 0x109D, 0x135D, 0x135F, 0x1712, 0x1714,
            0x1732, 0x1733, 0x1752, 0x1753, 0x1772, 0x1773, 0x17B4, 0x17B5, 0x17B7, 0x17BD, 0x17C6, 0x17C6,
            0x17C9, 0x17D3, 0x17DD, 0x17DD, 0x180B, 0x180F, 0x1885, 0x1886, 0x18A9, 0x18A9, 0x1920, 0x1922,
            0x1927, 0x1928, 0x1932, 0x1932, 0x1939, 0x193B, 0x1A17, 0x1A18, 0x1A1B, 0x1A1B, 0x1A56, 0x1A56,
            0x1A58, 0x1A5E, 0x1A60, 0x1A60, 0x1A62, 0x1A62, 0x1A65, 0x1A6C, 0x1A73, 0x1A7C, 0x1A7F, 0x1A7F,
            0x1AB0, 0x1ACE, 0x1B00, 0x1B03, 0x1B34, 0x1B34, 0x1B36, 0x1B3A, 0x1B3C, 0x1B3C, 0x1B42, 0x1B42,
            0x1B6B, 0x1B73, 0x1B80, 0x1B81, 0x1BA2, 0x1BA5, 0x1BA8, 0x1BA9, 0x1BAB, 0x1BAD, 0x1BE6, 0x1BE6,
            0x1BE8, 0x1BE9, 0x1BED, 0x1BED, 0x1BEF, 0x1BF1, 0x1C2C, 0x1C33, 0x1C36, 0x1C37, 0x1CD0, 0x1CD2,
            0x1CD4, 0x1CE0, 0x1CE2, 0x1CE8, 0x1CED, 0x1CED, 0x1CF4, 0x1CF4, 0x1CF8, 0x1CF9, 0x1DC0, 0x1DFF,
            0x200B, 0x200F, 0x202A, 0x202E, 0x2060, 0x2064, 0x2066, 0x206F, 0x20D0, 0x20F0, 0x2CEF, 0x2CF1,
            0x2D7F, 0x2D7F, 0x2DE0, 0x2DFF, 0x302A, 0x302D, 0x3099, 0x309A, 0xA66F, 0xA672, 0xA674, 0xA67D,
            0xA69E, 0xA69F, 0xA6F0, 0xA6F1, 0xA802, 0xA802, 0xA806, 0xA806, 0xA80B, 0xA80B, 0xA825, 0xA826,
            0xA82C, 0xA82C, 0xA8C4, 0xA8C5, 0xA8E0, 0xA8F1, 0xA8FF, 0xA8FF, 0xA926, 0xA92D, 0xA947, 0xA951,
            0xA980, 0xA982, 0xA9B3, 0xA9B3, 0xA9B6, 0xA9B9, 0xA9BC, 0xA9BD, 0xA9E5, 0xA9E5, 0xAA29, 0xAA2E,
            0xAA31, 0xAA32, 0xAA35, 0xAA36, 0xAA43, 0xAA43, 0xAA4C, 0xAA4C, 0xAA7C, 0xAA7C, 0xAAB0, 0xAAB0,
            0xAAB2, 0xAAB4, 0xAAB7, 0xAAB8, 0xAABE, 0xAABF, 0xAAC1, 0xAAC1, 0xAAEC, 0xAAED, 0xAAF6, 0xAAF6,
            0xABE5, 0xABE5, 0xABE8, 0xABE8, 0xABED, 0xABED, 0xFB1E, 0xFB1E, 0xFE00, 0xFE0F, 0xFE20, 0xFE2F,
            0xFEFF, 0xFEFF, 0xFFF9, 0xFFFB, 0x101FD, 0x101FD, 0x102E0, 0x102E0, 0x10376, 0x1037A, 0x10A01, 0x10A03,
            0x10A05, 0x10A06, 0x10A0C, 0x10A0F, 0x10A38, 0x10A3A, 0x10A3F, 0x10A3F, 0x10AE5, 0x10AE6, 0x10D24, 0x10D27,
            0x10EAB, 0x10EAC, 0x10EFD, 0x10EFF, 0x10F46, 0x10F50, 0x10F82, 0x10F85, 0x11001, 0x11001, 0x11038, 0x11046,
            0x11070, 0x11070, 0x11073, 0x11074, 0x1107F, 0x11081, 0x110B3, 0x110B6, 0x110B9, 0x110BA, 0x110BD, 0x110BD,
            0x110C2, 0x110C2, 0x110CD, 0x110CD, 0x11100, 0x11102, 0x11127, 0x1112B, 0x1112D, 0x11134, 0x11173, 0x11173,
            0x11180, 0x11181, 0x111B6, 0x111BE, 0x111C9, 0x111CC, 0x111CF, 0x111CF, 0x1122F, 0x11231, 0x11234, 0x11234,
            0x11236, 0x11237, 0x1123E, 0x1123E, 0x11241, 0x11241, 0x112DF, 0x112DF, 0x112E3, 0x112EA, 0x11300, 0x11301,
            0x1133B, 0x1133C, 0x11340, 0x11340, 0x11366, 0x1136C, 0x11370, 0x11374, 0x11438, 0x1143F, 0x11442, 0x11444,
            0x11446, 0x11446, 0x1145E, 0x1145E, 0x114B3, 0x114B8, 0x114BA, 0x114BA, 0x114BF, 0x114C0, 0x114C2, 0x114C3,
            0x115B2, 0x115B5, 0x115BC, 0x115BD, 0x115BF, 0x115C0, 0x115DC, 0x115DD, 0x11633, 0x1163A, 0x1163D, 0x1163D,
            0x1163F, 0x11640, 0x116AB, 0x116AB, 0x116AD, 0x116AD, 0x116B0, 0x116B5, 0x116B7, 0x116B7, 0x1171D, 0x1171F,
            0x11722, 0x11725, 0x11727, 0x1172B, 0x1182F, 0x11837, 0x11839, 0x1183A, 0x1193B, 0x1193C, 0x1193E, 0x1193E,
            0x11943, 0x11943, 0x119D4, 0x119D7, 0x119DA, 0x119DB, 0x119E0, 0x119E0, 0x11A01, 0x11A0A, 0x11A33, 0x11A38,
            0x11A3B, 0x11A3E, 0x11A47, 0x11A47, 0x11A51, 0x11A56, 0x11A59, 0x11A5B, 0x11A8A, 0x11A96, 0x11A98, 0x11A99,
            0x11C30, 0x11C36, 0x11C38, 0x11C3D, 0x11C3F, 0x11C3F, 0x11C92, 0x11CA7, 0x11CAA, 0x11CB0, 0x11CB2, 0x11CB3,
            0x11CB5, 0x11CB6, 0x11D31, 0x11D36, 0x11D3A, 0x11D3A, 0x11D3C, 0x11D3D, 0x11D3F, 0x11D45, 0x11D47, 0x11D47,
            0x11D90, 0x11D91, 0x11D95, 0x11D95, 0x11D97, 0x11D97, 0x11EF3, 0x11EF4, 0x11F00, 0x11F01, 0x11F36, 0x11F3A,
            0x11F40, 0x11F40, 0x11F42, 0x11F42, 0x13430, 0x13440, 0x13447, 0x13455, 0x16AF0, 0x16AF4, 0x16B30, 0x16B36,
            0x16F4F, 0x16F4F, 0x16F8F, 0x16F92, 0x16FE4, 0x16FE4, 0x1BC9D, 0x1BC9E, 0x1BCA0, 0x1BCA3, 0x1CF00, 0x1CF2D,
            0x1CF30, 0x1CF46, 0x1D167, 0x1D169, 0x1D173, 0x1D182, 0x1D185, 0x1D18B, 0x1D1AA, 0x1D1AD, 0x1D242, 0x1D244,
            0x1DA00, 0x1DA36, 0x1DA3B, 0x1DA6C, 0x1DA75, 0x1DA75, 0x1DA84, 0x1DA84, 0x1DA9B, 0x1DA9F, 0x1DAA1, 0x1DAAF,
            0x1E000, 0x1E006, 0x1E008, 0x1E018, 0x1E01B, 0x1E021, 0x1E023, 0x1E024, 0x1E026, 0x1E02A, 0x1E08F, 0x1E08F,
            0x1E130, 0x1E136, 0x1E2AE, 0x1E2AE, 0x1E2EC, 0x1E2EF, 0x1E4EC, 0x1E4EF, 0x1E8D0, 0x1E8D6, 0x1E944, 0x1E94A,
            0xE0001, 0xE0001, 0xE0020, 0xE007F, 0xE0100, 0xE01EF,
        };

        // Unicode 15.0 category So, Basic Multilingual Plane only.
        private static readonly int[] OtherSymbolRanges =
        {
            0xA6, 0xA6, 0xA9, 0xA9, 0xAE, 0xAE, 0xB0, 0xB0, 0x482, 0x482, 0x58D, 0x58E,
            0x60E, 0x60F, 0x6DE, 0x6DE, 0x6E9, 0x6E9, 0x6FD, 0x6FE, 0x7F6, 0x7F6, 0x9FA, 0x9FA,
            0xB70, 0xB70, 0xBF3, 0xBF8, 0xBFA, 0xBFA, 0xC7F, 0xC7F, 0xD4F, 0xD4F, 0xD79, 0xD79,
            0xF01, 0xF03, 0xF13, 0xF13, 0xF15, 0xF17, 0xF1A, 0xF1F, 0xF34, 0xF34, 0xF36, 0xF36,
            0xF38, 0xF38, 0xFBE, 0xFC5, 0xFC7, 0xFCC, 0xFCE, 0xFCF, 0xFD5, 0xFD8, 0x109E, 0x109F,
            0x1390, 0x1399, 0x166D, 0x166D, 0x1940, 0x1940, 0x19DE, 0x19FF, 0x1B61, 0x1B6A, 0x1B74, 0x1B7C,
            0x2100, 0x2101, 0x2103, 0x2106, 0x2108, 0x2109, 0x2114, 0x2114, 0x2116, 0x2117, 0x211E, 0x2123,
            0x2125, 0x2125, 0x2127, 0x2127, 0x2129, 0x2129, 0x212E, 0x212E, 0x213A, 0x213B, 0x214A, 0x214A,
            0x214C, 0x214D, 0x214F, 0x214F, 0x218A, 0x218B, 0x2195, 0x2199, 0x219C, 0x219F, 0x21A1, 0x21A2,
            0x21A4, 0x21A5, 0x21A7, 0x21AD, 0x21AF, 0x21CD, 0x21D0, 0x21D1, 0x21D3, 0x21D3, 0x21D5, 0x21F3,
            0x2300, 0x2307, 0x230C, 0x231F, 0x2322, 0x2328, 0x232B, 0x237B, 0x237D, 0x239A, 0x23B4, 0x23DB,
            0x23E2, 0x2426, 0x2440, 0x244A, 0x249C, 0x24E9, 0x2500, 0x25B6, 0x25B8, 0x25C0, 0x25C2, 0x25F7,
            0x2600, 0x266E, 0x2670, 0x2767, 0x2794, 0x27BF, 0x2800, 0x28FF, 0x2B00, 0x2B2F, 0x2B45, 0x2B46,
            0x2B4D, 0x2B73, 0x2B76, 0x2B95, 0x2B97, 0x2BFF, 0x2CE5, 0x2CEA, 0x2E50, 0x2E51, 0x2E80, 0x2E99,
            0x2E9B, 0x2EF3, 0x2F00, 0x2FD5, 0x2FF0, 0x2FFB, 0x3004, 0x3004, 0x3012, 0x3013, 0x3020, 0x3020,
            0x3036, 0x3037, 0x303E, 0x303F, 0x3190, 0x3191, 0x3196, 0x319F, 0x31C0, 0x31E3, 0x3200, 0x321E,
            0x322A, 0x3247, 0x3250, 0x3250, 0x3260, 0x327F, 0x328A, 0x32B0, 0x32C0, 0x33FF, 0x4DC0, 0x4DFF,
            0xA490, 0xA4C6, 0xA828, 0xA82B, 0xA836, 0xA837, 0xA839, 0xA839, 0xAA77, 0xAA79, 0xFD40, 0xFD4F,
            0xFDCF, 0xFDCF, 0xFDFD, 0xFDFF, 0xFFE4, 0xFFE4, 0xFFE8, 0xFFE8, 0xFFED, 0xFFEE, 0xFFFC, 0xFFFD,
        };

        // East Asian wide outside the Basic Multilingual Plane, by block: Tangut, Khitan, kana supplements and Nushu; the
        // emoji and pictograph planes from Mahjong tiles to Symbols and Pictographs Extended-A; CJK extensions B onward.
        private static bool IsWideAstral(int cp)
            => (cp >= 0x16FE0 && cp <= 0x1B2FF) || (cp >= 0x1F000 && cp <= 0x1FAFF) || (cp >= 0x20000 && cp <= 0x3FFFD);

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
                    // A parser turns these into spaces in an attribute, and CR (or CRLF) into LF in text (XML 1.0 3.3.3, 2.11).
                    '\t' when attribute => "&#9;",
                    '\n' when attribute => "&#10;",
                    '\r' => "&#13;",
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

        public PathBuilder MoveTo(double x, double y) => Cmd("M", N(x, nameof(x)), N(y, nameof(y)));
        public PathBuilder LineTo(double x, double y) => Cmd("L", N(x, nameof(x)), N(y, nameof(y)));
        public PathBuilder HorizontalTo(double x) => Cmd("H", N(x, nameof(x)));
        public PathBuilder VerticalTo(double y) => Cmd("V", N(y, nameof(y)));

        /// <summary>Cubic Bézier to (x, y) with control points (x1, y1) and (x2, y2).</summary>
        public PathBuilder CubicTo(double x1, double y1, double x2, double y2, double x, double y)
            => Cmd("C", N(x1, nameof(x1)), N(y1, nameof(y1)), N(x2, nameof(x2)), N(y2, nameof(y2)), N(x, nameof(x)), N(y, nameof(y)));

        /// <summary>Quadratic Bézier to (x, y) with control point (x1, y1).</summary>
        public PathBuilder QuadraticTo(double x1, double y1, double x, double y)
            => Cmd("Q", N(x1, nameof(x1)), N(y1, nameof(y1)), N(x, nameof(x)), N(y, nameof(y)));

        /// <summary>Elliptical arc to (x, y).</summary>
        public PathBuilder ArcTo(double rx, double ry, double rotationDegrees, bool largeArc, bool sweep, double x, double y)
            => Cmd("A", N(rx, nameof(rx)), N(ry, nameof(ry)), N(rotationDegrees, nameof(rotationDegrees)),
                   largeArc ? "1" : "0", sweep ? "1" : "0", N(x, nameof(x)), N(y, nameof(y)));

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

        // The arguments arrive already formatted, so a non-finite one throws (naming itself) before anything is written.
        private static string N(double value, string name) => SvgUtils.Number(value, name);

        private PathBuilder Cmd(string op, params string[] args)
        {
            Separator();
            _d.Append(op).Append(string.Join(" ", args));
            return this;
        }

        private void Separator()
        {
            if (_d.Length > 0) _d.Append(' ');
        }
    }
}
