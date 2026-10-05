using System;
using System.IO;
using System.Linq;
using ToolBelt.Numerics;
using ToolBelt.Tests.Framework;
using ToolBelt.Visualization;

namespace ToolBelt.Tests.Visualization
{
    /// <summary>PngReader, Bmp and Palettes.</summary>
    public sealed class RasterFormatsTests
    {
        private static ImageBuffer RandomImage(int w, int h, int seed)
        {
            var rng = new DeterministicRandom(seed);
            var img = new ImageBuffer(w, h);
            rng.NextBytes(img.Pixels);
            return img;
        }

        // ---------- PNG ----------

        public void Png_DecodesEveryFixture()
        {
            // Hand-encoded by scripts/gen-png-fixtures.py: every colour type and bit depth, random filters per row,
            // Adam7, tRNS, split IDAT and an ancillary chunk; expected pixels cross-checked against Pillow.
            Check.True(PngFixtureData.Cases.Length >= 40, "fixture set looks incomplete");
            foreach (var (name, w, h, png, rgba) in PngFixtureData.Cases)
            {
                ImageBuffer img = PngReader.Decode(Convert.FromBase64String(png));
                Check.Equal(w, img.Width, name);
                Check.Equal(h, img.Height, name);
                byte[] expected = Convert.FromBase64String(rgba);
                int bad = Enumerable.Range(0, expected.Length).FirstOrDefault(i => expected[i] != img.Pixels[i], -1);
                Check.Equal(-1, bad, $"{name}: first differing byte");
            }
        }

        public void Png_RoundTripsWithTheWriter()
        {
            foreach (var (w, h) in new[] { (1, 1), (7, 3), (64, 33) })
            {
                ImageBuffer img = RandomImage(w, h, w * 31 + h);
                ImageBuffer back = PngReader.Decode(PngWriter.Encode(img));
                Check.True(img.Pixels.SequenceEqual(back.Pixels), $"{w}x{h}");
            }
        }

        public void Png_RealWorldFileFromWindows()
        {
            // Any PNG shipped with Windows (produced by real encoders with real filter heuristics) must decode.
            string? dir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string[] files = Directory.Exists(Path.Combine(dir, "SystemApps"))
                ? Directory.EnumerateFiles(Path.Combine(dir, "SystemApps"), "*.png", SearchOption.AllDirectories).Take(25).ToArray()
                : Array.Empty<string>();
            if (files.Length == 0) return;                                      // not on Windows: nothing to try
            foreach (string f in files)
            {
                ImageBuffer img = PngReader.Load(f);
                Check.True(img.Width > 0 && img.Height > 0, f);
            }
        }

        public void Png_RejectsCorruptInput()
        {
            byte[] good = PngWriter.Encode(RandomImage(8, 8, 1));
            Check.Throws<InvalidDataException>(() => PngReader.Decode(new byte[] { 1, 2, 3 }));
            byte[] crc = (byte[])good.Clone();
            crc[20] ^= 1;                                                       // inside IHDR
            Check.Throws<InvalidDataException>(() => PngReader.Decode(crc));
            Check.Throws<InvalidDataException>(() => PngReader.Decode(good.Take(good.Length - 20).ToArray()));
            // A header claiming a giant image is rejected before any allocation.
            byte[] huge = (byte[])good.Clone();
            huge[16] = 0x7F;
            FixCrc(huge, 12, 17);
            Check.Throws<InvalidDataException>(() => PngReader.Decode(huge));
        }

        private static void FixCrc(byte[] png, int typeStart, int length)
        {
            uint crc = 0xFFFFFFFF;
            for (int i = typeStart; i < typeStart + length; i++)
            {
                crc ^= png[i];
                for (int k = 0; k < 8; k++) crc = (crc & 1) != 0 ? 0xEDB88320 ^ (crc >> 1) : crc >> 1;
            }
            crc ^= 0xFFFFFFFF;
            int at = typeStart + length;
            png[at] = (byte)(crc >> 24); png[at + 1] = (byte)(crc >> 16); png[at + 2] = (byte)(crc >> 8); png[at + 3] = (byte)crc;
        }

        // ---------- BMP ----------

        public void Bmp_RoundTripsWithAndWithoutAlpha()
        {
            foreach (var (w, h) in new[] { (1, 1), (3, 2), (17, 9) })                 // odd widths exercise row padding
            {
                ImageBuffer img = RandomImage(w, h, w + h);
                Check.True(img.Pixels.SequenceEqual(Bmp.Decode(Bmp.Encode(img)).Pixels), $"32-bit {w}x{h}");
                ImageBuffer opaque = Bmp.Decode(Bmp.Encode(img, includeAlpha: false));
                for (int i = 0; i < img.Pixels.Length; i += 4)
                {
                    Check.Equal(img.Pixels[i], opaque.Pixels[i]);
                    Check.Equal((byte)255, opaque.Pixels[i + 3]);
                }
            }
        }

        public void Bmp_LayoutMatchesTheSpec()
        {
            var img = new ImageBuffer(3, 2);
            img.SetPixel(0, 0, new Rgba(10, 20, 30));                              // top-left
            img.SetPixel(2, 1, new Rgba(40, 50, 60));                              // bottom-right
            byte[] b = Bmp.Encode(img, includeAlpha: false);
            Check.Equal(54 + 12 * 2, b.Length);                                     // 3 px × 3 B = 9 → padded to 12
            Check.Equal(BitConverter.ToInt32(b, 2), b.Length);
            // Bottom-up: the first stored row is the image's last row; BGR order.
            Check.Equal((byte)60, b[54 + 6]);
            Check.Equal((byte)50, b[54 + 7]);
            Check.Equal((byte)40, b[54 + 8]);
            Check.Equal((byte)30, b[54 + 12]);
        }

        public void Bmp_ReadsTopDownAndPalettised()
        {
            // Hand-built 2x1 8-bit palettised, top-down BMP.
            var b = new byte[14 + 40 + 2 * 4 + 4];
            b[0] = (byte)'B'; b[1] = (byte)'M';
            BitConverter.GetBytes(b.Length).CopyTo(b, 2);
            BitConverter.GetBytes(14 + 40 + 8).CopyTo(b, 10);
            BitConverter.GetBytes(40).CopyTo(b, 14);
            BitConverter.GetBytes(2).CopyTo(b, 18);
            BitConverter.GetBytes(-1).CopyTo(b, 22);
            BitConverter.GetBytes((short)1).CopyTo(b, 26);
            BitConverter.GetBytes((short)8).CopyTo(b, 28);
            BitConverter.GetBytes(2).CopyTo(b, 46);                                 // 2 palette entries
            b[54] = 255; b[55] = 0; b[56] = 0;                                      // entry 0: blue (BGR0)
            b[58] = 0; b[59] = 0; b[60] = 255;                                      // entry 1: red
            b[62] = 1; b[63] = 0;
            ImageBuffer img = Bmp.Decode(b);
            Check.Equal(new Rgba(255, 0, 0), img.GetPixel(0, 0));
            Check.Equal(new Rgba(0, 0, 255), img.GetPixel(1, 0));
            Check.Throws<InvalidDataException>(() => Bmp.Decode(new byte[60]));
        }

        // ---------- palettes ----------

        public void Contrast_MatchesWcagReferenceValues()
        {
            var black = new Rgba(0, 0, 0);
            var white = new Rgba(255, 255, 255);
            Check.Close(21, Palettes.ContrastRatio(black, white), 1e-12);
            Check.Close(1, Palettes.ContrastRatio(white, white), 1e-12);
            Check.Close(4.48, Palettes.ContrastRatio(new Rgba(0x77, 0x77, 0x77), white), 0.01);   // #777 on white: just fails AA
            Check.Close(0.2126, Palettes.RelativeLuminance(new Rgba(255, 0, 0)), 1e-12);
            Check.Equal(white, Palettes.ContrastingText(new Rgba(0, 0, 128)));
            Check.Equal(black, Palettes.ContrastingText(new Rgba(255, 255, 0)));
            foreach (Rgba c in Palettes.Tableau10.Concat(Palettes.OkabeIto))
                Check.True(Palettes.ContrastRatio(c, Palettes.ContrastingText(c)) >= 4.5, $"label on {Palettes.ToHex(c)} must reach WCAG AA");
        }

        public void Palette_CycleAndHex()
        {
            Check.Equal(8, Palettes.OkabeIto.Count);
            Check.Equal(Palettes.OkabeIto[1], Palettes.Cycle(Palettes.OkabeIto, 9));
            Check.Equal(Palettes.OkabeIto[7], Palettes.Cycle(Palettes.OkabeIto, -1));
            Check.Equal("#E69F00", Palettes.ToHex(Palettes.OkabeIto[1]));
            Check.Equal(new Rgba(0xE6, 0x9F, 0x00), Palettes.FromHex("e69f00"));
            Check.Equal(new Rgba(0xAA, 0xBB, 0xCC), Palettes.FromHex("#abc"));
            Check.Equal("#11223344", Palettes.ToHex(Palettes.FromHex("#11223344")));
            Check.Throws<FormatException>(() => Palettes.FromHex("#12345"));
        }
    }
}
