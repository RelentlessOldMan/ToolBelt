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
    /// </summary>
    public static class LineReader
    {
        public static IEnumerable<TextLine> ReadLines(Stream stream, Encoding? encoding = null)
        {
            if (stream is null) throw new ArgumentNullException(nameof(stream));
            return Iterate(stream, encoding ?? Encoding.UTF8);
        }

        private static IEnumerable<TextLine> Iterate(Stream stream, Encoding encoding)
        {
            var buffer = new byte[4096];
            var line = new List<byte>();
            long position = 0;   // absolute byte index of the next byte to process
            long lineStart = 0;  // byte offset where the current line began
            bool afterCarriageReturn = false;
            int read;

            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                for (int k = 0; k < read; k++)
                {
                    byte b = buffer[k];
                    position++;

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
