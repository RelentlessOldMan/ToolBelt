// ToolBelt drop-in — also copy Visualization/ImageBuffer.cs (for Rgba).
using System;
using System.Collections.Generic;
using System.Globalization;

namespace ToolBelt.Visualization
{
    /// <summary>
    /// Categorical colour palettes for series and groups, and the colour-contrast arithmetic behind readable labels.
    /// <see cref="OkabeIto"/> is the colour-blind-safe default (distinguishable under protanopia, deuteranopia and
    /// tritanopia); <see cref="Tableau10"/> is the familiar alternative. <see cref="ContrastingText"/> picks black or white
    /// text for a background by WCAG 2 relative luminance, and <see cref="ContrastRatio"/> reports the WCAG ratio
    /// (≥ 4.5 is the AA threshold for body text).
    /// </summary>
    public static class Palettes
    {
        /// <summary>Okabe &amp; Ito (2008): black, orange, sky blue, bluish green, yellow, blue, vermillion, reddish purple.</summary>
        public static IReadOnlyList<Rgba> OkabeIto { get; } = new[]
        {
            new Rgba(0, 0, 0), new Rgba(230, 159, 0), new Rgba(86, 180, 233), new Rgba(0, 158, 115),
            new Rgba(240, 228, 66), new Rgba(0, 114, 178), new Rgba(213, 94, 0), new Rgba(204, 121, 167),
        };

        /// <summary>Okabe–Ito without black — for series on a white plot where black reads as an axis.</summary>
        public static IReadOnlyList<Rgba> OkabeItoNoBlack { get; } = new[]
        {
            new Rgba(230, 159, 0), new Rgba(86, 180, 233), new Rgba(0, 158, 115), new Rgba(240, 228, 66),
            new Rgba(0, 114, 178), new Rgba(213, 94, 0), new Rgba(204, 121, 167),
        };

        /// <summary>Tableau 10 (2016 revision).</summary>
        public static IReadOnlyList<Rgba> Tableau10 { get; } = new[]
        {
            new Rgba(78, 121, 167), new Rgba(242, 142, 43), new Rgba(225, 87, 89), new Rgba(118, 183, 178), new Rgba(89, 161, 79),
            new Rgba(237, 201, 72), new Rgba(176, 122, 161), new Rgba(255, 157, 167), new Rgba(156, 117, 95), new Rgba(186, 176, 172),
        };

        /// <summary>The <paramref name="index"/>-th colour, cycling when there are more series than colours.</summary>
        public static Rgba Cycle(IReadOnlyList<Rgba> palette, int index)
        {
            if (palette is null) throw new ArgumentNullException(nameof(palette));
            if (palette.Count == 0) throw new ArgumentException("Palette is empty.", nameof(palette));
            int i = index % palette.Count;
            return palette[i < 0 ? i + palette.Count : i];
        }

        /// <summary>WCAG 2 relative luminance in [0, 1] (sRGB, alpha ignored).</summary>
        public static double RelativeLuminance(Rgba c)
            => 0.2126 * Linear(c.R) + 0.7152 * Linear(c.G) + 0.0722 * Linear(c.B);

        /// <summary>WCAG contrast ratio between two colours, from 1 (identical) to 21 (black on white).</summary>
        public static double ContrastRatio(Rgba a, Rgba b)
        {
            double la = RelativeLuminance(a), lb = RelativeLuminance(b);
            return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
        }

        /// <summary>Black or white, whichever contrasts more with <paramref name="background"/> — for labels on heat-map cells and bars.</summary>
        public static Rgba ContrastingText(Rgba background)
        {
            var black = new Rgba(0, 0, 0);
            var white = new Rgba(255, 255, 255);
            return ContrastRatio(background, black) >= ContrastRatio(background, white) ? black : white;
        }

        /// <summary><c>#RRGGBB</c> (alpha omitted when opaque, else <c>#RRGGBBAA</c>).</summary>
        public static string ToHex(Rgba c)
            => c.A == 255 ? string.Format(CultureInfo.InvariantCulture, "#{0:X2}{1:X2}{2:X2}", c.R, c.G, c.B)
                          : string.Format(CultureInfo.InvariantCulture, "#{0:X2}{1:X2}{2:X2}{3:X2}", c.R, c.G, c.B, c.A);

        /// <summary>Parses <c>#RGB</c>, <c>#RRGGBB</c> or <c>#RRGGBBAA</c> (the '#' is optional).</summary>
        public static Rgba FromHex(string hex)
        {
            if (hex is null) throw new ArgumentNullException(nameof(hex));
            string h = hex.StartsWith("#", StringComparison.Ordinal) ? hex.Substring(1) : hex;
            if (h.Length == 3) h = new string(new[] { h[0], h[0], h[1], h[1], h[2], h[2] });
            if ((h.Length != 6 && h.Length != 8) || !uint.TryParse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint v))
                throw new FormatException($"'{hex}' is not a #RGB, #RRGGBB or #RRGGBBAA colour.");
            if (h.Length == 6) v = (v << 8) | 0xFF;
            return new Rgba((byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v);
        }

        private static double Linear(byte channel)
        {
            double s = channel / 255.0;
            return s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
    }
}
