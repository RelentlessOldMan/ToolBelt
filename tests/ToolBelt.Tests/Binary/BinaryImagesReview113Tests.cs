using System;
using System.IO;
using System.Linq;
using System.Text;
using ToolBelt.Binary;
using ToolBelt.Tests.Framework;
using ToolBelt.Visualization;

namespace ToolBelt.Tests.Binary
{
    // Round 113 review regressions (binary formats & images, seventh pass): each failed on the code before its fix.
    public sealed class BinaryImagesReview113Tests
    {
        // ---- PngReader.Decode(Stream): empty ancillary chunks ----

        // IHDR, then empty tEXt chunks without end, made as they are read. Empty chunks counted nothing toward the
        // ancillary limit, so they were kept until memory ran out; they count their 12 bytes of framing now.
        public void Png_EndlessEmptyChunksAreRefused()
        {
            var stream = new EmptyChunks();
            var ex = Check.Throws<InvalidDataException>(() => PngReader.Decode(stream));
            Check.True(ex.Message.Contains("ancillary"), ex.Message);
            Check.True(stream.Served <= PngReader.MaxAncillaryBytes + (1 << 20), stream.Served.ToString());
        }

        private sealed class EmptyChunks : Stream
        {
            private readonly byte[] _head;
            private readonly byte[] _chunk;
            public long Served { get; private set; }

            public EmptyChunks()
            {
                var ms = new MemoryStream();
                ms.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, 0, 8);
                var ihdr = new byte[13];
                EndianConverter.WriteUInt32BigEndian(ihdr, 0, 1);
                EndianConverter.WriteUInt32BigEndian(ihdr, 4, 1);
                ihdr[8] = 8; ihdr[9] = 2;
                Chunk(ms, "IHDR", ihdr);
                _head = ms.ToArray();
                ms = new MemoryStream();
                Chunk(ms, "tEXt", Array.Empty<byte>());
                _chunk = ms.ToArray();
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                // A stop well past the limit, so the old code fails here instead of eating the machine's memory.
                if (Served > PngReader.MaxAncillaryBytes + (16 << 20)) throw new InvalidOperationException("read far past the ancillary limit");
                for (int i = 0; i < count; i++, Served++)
                    buffer[offset + i] = Served < _head.Length ? _head[Served] : _chunk[(Served - _head.Length) % _chunk.Length];
                return count;
            }

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => Served; set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }

        private static void Chunk(Stream ms, string type, byte[] body)
        {
            var len = new byte[4];
            EndianConverter.WriteUInt32BigEndian(len, 0, (uint)body.Length);
            ms.Write(len, 0, 4);
            var td = Encoding.ASCII.GetBytes(type).Concat(body).ToArray();
            ms.Write(td, 0, td.Length);
            var crc = new byte[4];
            EndianConverter.WriteUInt32BigEndian(crc, 0, Crc32.Compute(td));
            ms.Write(crc, 0, 4);
        }

#if NET8_0_OR_GREATER
        // ---- PngReader.Decode(Stream): a stream that returns one byte per read ----

        // Each read was kept as its own array, so a byte-at-a-time stream took about 50 times the file. The decode matches
        // Decode(byte[]) and allocates at most a few copies of the file more than it.
        public void Png_OneByteReadsStayNearTheFileSize()
        {
            var rng = new Random(113);
            var image = new ImageBuffer(400, 400);
            rng.NextBytes(image.Pixels);
            byte[] png = PngWriter.Encode(image);
            Check.True(png.Length > 500_000, png.Length.ToString());

            PngReader.Decode(png);
            PngReader.Decode(new OneByte(png));                              // warm both paths
            long a0 = GC.GetAllocatedBytesForCurrentThread();
            ImageBuffer fromBytes = PngReader.Decode(png);
            long a1 = GC.GetAllocatedBytesForCurrentThread();
            ImageBuffer fromStream = PngReader.Decode(new OneByte(png));
            long a2 = GC.GetAllocatedBytesForCurrentThread();

            Check.True(fromBytes.Pixels.SequenceEqual(fromStream.Pixels), "stream and byte[] decodes differ");
            Check.True(fromStream.Pixels.SequenceEqual(image.Pixels), "round trip differs");
            long extra = (a2 - a1) - (a1 - a0);
            Check.True(extra < 4L * png.Length + (1 << 20), $"extra {extra} bytes for a {png.Length}-byte file");
        }
#endif

        private sealed class OneByte : Stream
        {
            private readonly byte[] _data;
            private int _pos;
            public OneByte(byte[] data) { _data = data; }
            public override int Read(byte[] buffer, int offset, int count)
            {
                if (count == 0 || _pos >= _data.Length) return 0;
                buffer[offset] = _data[_pos++];
                return 1;
            }
            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => _pos; set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }

        // Guard for the block store: random images of many sizes, read a byte at a time and in random pieces, decode as
        // Decode(byte[]) does.
        public void Png_StreamPiecesMatchTheByteDecode()
        {
            var rng = new Random(1131);
            for (int trial = 0; trial < 40; trial++)
            {
                var image = new ImageBuffer(rng.Next(1, 300), rng.Next(1, 300));
                rng.NextBytes(image.Pixels);
                byte[] png = PngWriter.Encode(image);
                var want = PngReader.Decode(png).Pixels;
                Check.True(want.SequenceEqual(PngReader.Decode(new OneByte(png)).Pixels), $"trial {trial} one byte");
                Check.True(want.SequenceEqual(PngReader.Decode(new Pieces(png, rng.Next())).Pixels), $"trial {trial} pieces");
            }
        }

        private sealed class Pieces : Stream
        {
            private readonly byte[] _data;
            private readonly Random _rng;
            private int _pos;
            public Pieces(byte[] data, int seed) { _data = data; _rng = new Random(seed); }
            public override int Read(byte[] buffer, int offset, int count)
            {
                int n = Math.Min(Math.Min(count, _data.Length - _pos), _rng.Next(1, 200_000));
                Array.Copy(_data, _pos, buffer, offset, n);
                _pos += n;
                return n;
            }
            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => _pos; set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }

        // ---- Bmp.Decode: sides over 32768 ----

        // Encode writes 40000 x 1 and 1 x 40000, which Decode refused as "implausible" past 32768 a side.
        public void Bmp_LongSidesRoundTrip()
        {
            var rng = new Random(1132);
            foreach (var (w, h) in new[] { (40000, 1), (1, 40000), (70000, 3), (3, 70000) })
                foreach (bool alpha in new[] { true, false })
                {
                    var image = new ImageBuffer(w, h);
                    rng.NextBytes(image.Pixels);
                    if (!alpha) for (int i = 3; i < image.Pixels.Length; i += 4) image.Pixels[i] = 255;
                    var back = Bmp.Decode(Bmp.Encode(image, alpha));
                    Check.Equal(w, back.Width);
                    Check.Equal(h, back.Height);
                    Check.True(back.Pixels.SequenceEqual(image.Pixels), $"{w}x{h} alpha={alpha}");
                }
        }

        // ---- Bmp.Decode: BITFIELDS masks inside the pixel data ----

        // A 40-byte header with BI_BITFIELDS keeps its three masks at 54-65; a pixel offset of 54 read the masks as the
        // first pixels. It is refused, and a file with the offset after the masks still reads.
        public void Bmp_BitfieldsMasksOverlappingThePixelsAreRefused()
        {
            var ex = Check.Throws<InvalidDataException>(() => Bmp.Decode(Bitfields40(54, 3)));
            Check.True(ex.Message.Contains("overlap"), ex.Message);
            var ok = Bmp.Decode(Bitfields40(66, 3));
            Check.Equal(new Rgba(0x30, 0x20, 0x10, 255), ok.GetPixel(0, 0));
            Check.Equal(new Rgba(0x31, 0x21, 0x11, 255), ok.GetPixel(2, 0));
        }

        // A 32-bit BI_BITFIELDS BMP with a BITMAPINFOHEADER, the standard masks at 54, and `w` x 1 pixels at `offset`.
        private static byte[] Bitfields40(int offset, int w)
        {
            var data = new byte[Math.Max(offset, 66) + w * 4];
            data[0] = (byte)'B'; data[1] = (byte)'M';
            BitConverter.GetBytes(data.Length).CopyTo(data, 2);
            BitConverter.GetBytes(offset).CopyTo(data, 10);
            BitConverter.GetBytes(40).CopyTo(data, 14);
            BitConverter.GetBytes(w).CopyTo(data, 18);
            BitConverter.GetBytes(1).CopyTo(data, 22);
            BitConverter.GetBytes((short)1).CopyTo(data, 26);
            BitConverter.GetBytes((short)32).CopyTo(data, 28);
            BitConverter.GetBytes(3).CopyTo(data, 30);
            BitConverter.GetBytes(0x00FF0000).CopyTo(data, 54);
            BitConverter.GetBytes(0x0000FF00).CopyTo(data, 58);
            BitConverter.GetBytes(0x000000FF).CopyTo(data, 62);
            if (offset >= 66)
                for (int x = 0; x < w; x++)
                {
                    int at = offset + x * 4, last = x == w - 1 ? 1 : 0;
                    data[at] = (byte)(0x10 + last);
                    data[at + 1] = (byte)(0x20 + last);
                    data[at + 2] = (byte)(0x30 + last);
                }
            return data;
        }

        // ---- SvgDocument.Polyline: a null stroke ----

        public void Svg_PolylineRefusesANullStroke()
        {
            // A null stroke was drawn black, where Line and Text throw.
            var svg = new SvgDocument(10, 10);
            var ex = Check.Throws<ArgumentNullException>(() => svg.Polyline(new[] { (0.0, 0.0), (5.0, 5.0) }, null!));
            Check.Equal("stroke", ex.ParamName);
            svg.Polyline(new[] { (0.0, 0.0), (5.0, 5.0) });
            Check.True(svg.ToString().Contains("stroke=\"black\""), svg.ToString());
        }
    }
}
