// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ToolBelt.IO
{
    /// <summary>A line of text together with the byte offset at which it began in the stream.</summary>
    public readonly struct TextLine
    {
        public TextLine(long offset, string text)
        {
            Offset = offset;
            Text = text;
        }

        /// <summary>Byte offset of the first character of the line within the stream.</summary>
        public long Offset { get; }
        public string Text { get; }

        public override string ToString() => $"@{Offset}: {Text}";
    }

    /// <summary>
    /// Enumerates lines from a stream together with each line's starting byte offset, tolerant of mixed
    /// line endings (LF, CRLF and lone CR). The byte offset lets a log tool seek back to a position — the
    /// streaming primitive the document readers each solve privately. Lines are decoded with the given
    /// encoding (UTF-8 by default); offsets are byte positions, so they stay correct for multi-byte text.
    /// Lines are split on the raw CR and LF bytes, so the encoding must write those as single bytes (UTF-8, ASCII,
    /// Latin-1 and other single-byte code pages do; UTF-16 and UTF-32 throw <see cref="ArgumentException"/>).
    /// A leading UTF-8 byte order mark is skipped (the first line's offset is then 3).
    /// </summary>
    public static class LineReader
    {
        private static readonly byte[] Bom = { 0xEF, 0xBB, 0xBF };

        public static IEnumerable<TextLine> ReadLines(Stream stream, Encoding? encoding = null)
        {
            if (stream is null) throw new ArgumentNullException(nameof(stream));
            encoding ??= Encoding.UTF8;
            byte[] lineBreak = encoding.GetBytes("\r\n");
            if (lineBreak.Length != 2 || lineBreak[0] != (byte)'\r' || lineBreak[1] != (byte)'\n')
                throw new ArgumentException(
                    $"LineReader splits on single CR/LF bytes, which {encoding.WebName} does not use; use a StreamReader instead.",
                    nameof(encoding));
            return Iterate(stream, encoding);
        }

        private static IEnumerable<TextLine> Iterate(Stream stream, Encoding encoding)
        {
            var buffer = new byte[4096];
            var line = new List<byte>();
            long position = 0;   // absolute byte index of the next byte to process
            long lineStart = 0;  // byte offset where the current line began
            bool afterCarriageReturn = false;
            int bomMatched = encoding.CodePage == 65001 ? 0 : -1;   // bytes of a UTF-8 BOM seen so far; -1 = not looking
            int read;

            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                for (int k = 0; k < read; k++)
                {
                    byte b = buffer[k];
                    position++;

                    if (bomMatched >= 0)
                    {
                        if (b == Bom[bomMatched])
                        {
                            line.Add(b);
                            if (++bomMatched == Bom.Length) { line.Clear(); lineStart = position; bomMatched = -1; }
                            continue;
                        }
                        bomMatched = -1;   // not a BOM: the bytes already buffered are ordinary text
                    }

                    if (afterCarriageReturn)
                    {
                        afterCarriageReturn = false;
                        if (b == (byte)'\n')
                        {
                            lineStart = position; // CRLF: swallow the LF, next line starts after it
                            continue;
                        }
                        // lone CR already ended the previous line; b starts the new one
                    }

                    if (b == (byte)'\n')
                    {
                        yield return new TextLine(lineStart, encoding.GetString(line.ToArray()));
                        line.Clear();
                        lineStart = position;
                    }
                    else if (b == (byte)'\r')
                    {
                        yield return new TextLine(lineStart, encoding.GetString(line.ToArray()));
                        line.Clear();
                        afterCarriageReturn = true;
                        lineStart = position;
                    }
                    else
                    {
                        line.Add(b);
                    }
                }
            }

            if (line.Count > 0)
                yield return new TextLine(lineStart, encoding.GetString(line.ToArray()));
        }
    }
}
