using System;
using System.IO;
using System.Linq;
using ToolBelt.Tests.Framework;
using ToolBelt.Visualization;

namespace ToolBelt.Tests.Binary
{
    /// <summary>Round 84 review regressions (binary formats and images, fifth pass).</summary>
    public sealed class BinaryImagesReview84Tests
    {
        private static int CountRed(ImageBuffer image)
        {
            int n = 0;
            for (int y = 0; y < image.Height; y++)
                for (int x = 0; x < image.Width; x++)
                    if (image.GetPixel(x, y).Equals(Rgba.Red)) n++;
            return n;
        }

        // ---------- ImageBuffer.DrawLineClipped ----------

        // The edge crossings (inexact below 2^40) were compared with the exact ends: a segment reaching the rectangle only
        // at an endpoint on an edge drew nothing.
        public void ClippedSegmentTouchingAnEdgeAtItsEndpointDrawsIt()
        {
            var image = new ImageBuffer(1, 14);
            image.DrawLineClipped(0.512680403079223, 2.2230085702487306, 0, -0.24299620347702697, Rgba.Red, -2, -2, 0, 8);
            Check.Equal(Rgba.Red, image.GetPixel(0, 0));

            image = new ImageBuffer(8, 10);
            image.DrawLineClipped(52.09870829810328, -9, 1.4043588663471684, 6, Rgba.Red, -1, 6, 5, 13);
            Check.Equal(Rgba.Red, image.GetPixel(1, 6));

            image = new ImageBuffer(10, 9);
            image.DrawLineClipped(58.73390406311206, -10.245150397645846, 4.334349457330231, 3, Rgba.Red, 0, 3, 9, 3);
            Check.Equal(Rgba.Red, image.GetPixel(4, 3));
        }

        // Randomized: a segment from outside to a point on an edge draws exactly that point's pixel; the same segment
        // stopped an ulp short of the edge draws nothing.
        public void ClippedSegmentsDecideTouchAndMissExactly()
        {
            var rng = new Random(8401);
            int touchFailures = 0, missFailures = 0;
            for (int trial = 0; trial < 4000; trial++)
            {
                int left = rng.Next(0, 10), top = rng.Next(0, 10), right = left + rng.Next(0, 10), bottom = top + rng.Next(0, 10);
                var image = new ImageBuffer(24, 24);
                int edge = rng.Next(4);
                double along = (edge < 2 ? left + (right - left) * rng.NextDouble() : top + (bottom - top) * rng.NextDouble());
                double far = 0.25 + 60 * rng.NextDouble(), drift = (rng.NextDouble() - 0.5) * 120;
                // (px, py) on the edge; (qx, qy) outside, beyond that edge.
                double px, py, qx, qy;
                switch (edge)
                {
                    case 0: px = along; py = top; qx = px + drift; qy = top - far; break;
                    case 1: px = along; py = bottom; qx = px + drift; qy = bottom + far; break;
                    case 2: px = left; py = along; qx = left - far; qy = py + drift; break;
                    default: px = right; py = along; qx = right + far; qy = py + drift; break;
                }
                image.DrawLineClipped(qx, qy, px, py, Rgba.Red, left, top, right, bottom);
                int ex = (int)Math.Max(left, Math.Min(right, Math.Round(px))), ey = (int)Math.Max(top, Math.Min(bottom, Math.Round(py)));
                if (!(CountRed(image) == 1 && image.GetPixel(ex, ey).Equals(Rgba.Red))) touchFailures++;

                // An ulp outside the edge.
                double sx = px, sy = py;
                switch (edge)
                {
                    case 0: sy = BitDecrement(py); break;
                    case 1: sy = BitIncrement(py); break;
                    case 2: sx = BitDecrement(px); break;
                    default: sx = BitIncrement(px); break;
                }
                var missed = new ImageBuffer(24, 24);
                missed.DrawLineClipped(qx, qy, sx, sy, Rgba.Red, left, top, right, bottom);
                if (CountRed(missed) != 0) missFailures++;
            }
            Check.Equal(0, touchFailures, "touching segments not drawn as their point");
            Check.Equal(0, missFailures, "missing segments drawn");
        }

        // A segment through the rectangle is still drawn end to end, and one parallel to an edge outside is not.
        public void ClippedSegmentsThroughAndBesideTheRectangle()
        {
            var image = new ImageBuffer(10, 10);
            image.DrawLineClipped(-100, 5, 100, 5, Rgba.Red, 2, 2, 7, 7);
            Check.Equal(6, CountRed(image));
            image = new ImageBuffer(10, 10);
            image.DrawLineClipped(-100, 1.5, 100, 1.5, Rgba.Red, 2, 2, 7, 7);
            Check.Equal(0, CountRed(image));
            image = new ImageBuffer(10, 10);
            image.DrawLineClipped(-100, -100, 100, 100, Rgba.Red, 2, 2, 7, 7);
            Check.Equal(6, CountRed(image));
            image = new ImageBuffer(10, 10);
            image.DrawLineClipped(0, 9, 9, 18, Rgba.Red, 2, 2, 7, 7);    // passes the corner (7, 16)… far below: a miss
            Check.Equal(0, CountRed(image));
        }

        private static double BitIncrement(double x)
        {
            long bits = BitConverter.DoubleToInt64Bits(x);
            if (x == 0) return double.Epsilon;
            return BitConverter.Int64BitsToDouble(x > 0 ? bits + 1 : bits - 1);
        }

        private static double BitDecrement(double x) => -BitIncrement(-x);

        // ---------- PngReader.Decode(Stream) ----------

        // Endless bytes after a given prefix; counts what was read.
        private sealed class EndlessStream : Stream
        {
            private readonly byte[] _prefix;
            public long Read_;
            public EndlessStream(byte[] prefix) { _prefix = prefix; }
            public override int Read(byte[] buffer, int offset, int count)
            {
                for (int i = 0; i < count; i++) buffer[offset + i] = Read_ + i < _prefix.Length ? _prefix[Read_ + i] : (byte)0;
                Read_ += count;
                return count;
            }
            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => Read_; set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }

        // Nothing was checked until IEND or 2 GB: a GIF followed by endless zeros was read for 2 GB before being rejected.
        public void PngStreamRejectsNonPngInputAtOnce()
        {
            var png = PngWriter.Encode(new ImageBuffer(3, 2, Rgba.Red));
            var cases = new[]
            {
                System.Text.Encoding.ASCII.GetBytes("GIF89a"),
                png.Take(8).ToArray(),                                                 // signature, then zero chunks
                png.Take(33).ToArray(),                                                // IHDR, then zero chunks
            };
            var withBadCrc = png.Take(33).ToArray();
            withBadCrc[30] ^= 1;
            cases = cases.Concat(new[] { withBadCrc }).ToArray();
            foreach (var prefix in cases)
            {
                var stream = new EndlessStream(prefix);
                Check.Throws<InvalidDataException>(() => PngReader.Decode(stream));
                Check.True(stream.Read_ < 1 << 20, "read " + stream.Read_ + " bytes");
            }
            // A valid stream still decodes.
            Check.Equal(Rgba.Red, PngReader.Decode(new MemoryStream(png)).GetPixel(2, 1));
        }

        // ---------- SvgUtils.EstimateTextWidth ----------

        // A flag (two regional indicators) renders as one glyph but counted two em.
        public void FlagEmojiCountsOneEm()
        {
            const string us = "🇺🇸", fr = "🇫🇷";
            Check.Equal(1000.0, SvgUtils.EstimateTextWidth(us, 1000));
            Check.Equal(2000.0, SvgUtils.EstimateTextWidth(us + fr, 1000));
            Check.Equal(2000.0, SvgUtils.EstimateTextWidth(us + "🇺", 1000), "a lone third indicator is its own em");
            double a = SvgUtils.EstimateTextWidth("a", 1000);
            Check.Equal(2000.0 + a, SvgUtils.EstimateTextWidth("🇺" + "a" + us, 1000), "a letter breaks the pairing");
        }
    }
}
