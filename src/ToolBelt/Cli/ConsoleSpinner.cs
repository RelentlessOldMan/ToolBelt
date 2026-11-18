// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ToolBelt.Cli
{
    /// <summary>
    /// An "I'm still working" spinner for console tools. While running it redraws <c>frame message</c> in place with a
    /// carriage return; on dispose it erases the line and optionally writes a final status line. When the output isn't an
    /// interactive terminal (redirected to a file or a CI log) it draws nothing animated — it writes the message once —
    /// so logs aren't filled with carriage-return garbage. On the real console the line is cut to the window width so
    /// it cannot wrap (a wrapped line can't be redrawn in place), counting wide characters and emoji as two columns. If the output breaks (a closed pipe), stopping still
    /// ends the animation, and neither drawing nor <see cref="Dispose"/> throws. The writer and interactivity are injectable for tests.
    /// </summary>
    public sealed class ConsoleSpinner : IDisposable
    {
        /// <summary>Braille dots — smooth on modern terminals.</summary>
        public static readonly string[] Dots = { "⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏" };

        /// <summary>Pure ASCII, for legacy consoles.</summary>
        public static readonly string[] Ascii = { "|", "/", "-", "\\" };

        private readonly TextWriter _writer;
        private readonly bool _interactive;
        private readonly string[] _frames;
        private readonly object _gate = new object();
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly Task? _loop;
        private readonly bool _isConsole;
        private string _message;
        private int _frame, _lastWidth;
        private bool _disposed;

        /// <param name="message">Text shown beside the spinner.</param>
        /// <param name="writer">Where to draw (default: standard error, so piped stdout stays clean).</param>
        /// <param name="interactive">Animate? Default: the writer is the console's and stderr isn't redirected.</param>
        /// <param name="frames">Animation frames (default <see cref="Dots"/>).</param>
        /// <param name="interval">Time between frames (default 80 ms).</param>
        public ConsoleSpinner(string message, TextWriter? writer = null, bool? interactive = null, string[]? frames = null, TimeSpan? interval = null)
        {
            _message = message ?? throw new ArgumentNullException(nameof(message));
            _writer = writer ?? Console.Error;
            _isConsole = writer is null;
            _interactive = interactive ?? (writer is null && !Console.IsErrorRedirected);
            _frames = frames is { Length: > 0 } ? frames : Dots;
            TimeSpan period = interval ?? TimeSpan.FromMilliseconds(80);
            // Past int.MaxValue ms Task.Delay threw on the background loop and the spinner silently stopped.
            if (period <= TimeSpan.Zero || period.TotalMilliseconds > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(interval), interval, "Interval must be positive and at most int.MaxValue ms.");

            if (_interactive)
            {
                Quietly(Draw);
                _loop = Task.Run(async () =>
                {
                    try
                    {
                        // Tick no longer throws on a broken writer, so the loop stops on the flag Quietly sets: it spun on
                        // a closed pipe until Dispose.
                        while (!_cts.IsCancellationRequested && !_broken)
                        {
                            await Task.Delay(period, _cts.Token).ConfigureAwait(false);
                            Tick();
                        }
                    }
                    catch (OperationCanceledException) { }
                    // The output broke (a closed pipe): the animation ends. Stop and Dispose deal with the writer; escaping
                    // here left a faulted task nobody observed when Stop was never called.
                    catch (Exception) { }
                });
            }
            else
            {
                Quietly(() => _writer.WriteLine(_message));
            }
        }

        /// <summary>The text beside the spinner; changing it redraws immediately (non-interactive: writes a new line).</summary>
        public string Message
        {
            get { lock (_gate) return _message; }
            set
            {
                if (value is null) throw new ArgumentNullException(nameof(value));
                lock (_gate)
                {
                    if (_disposed || value == _message) return;
                    _message = value;
                    if (_interactive) Quietly(Draw);
                    else Quietly(() => _writer.WriteLine(value));
                }
            }
        }

        /// <summary>Advances one frame and redraws (the background loop calls this; exposed for deterministic tests).</summary>
        public void Tick()
        {
            lock (_gate)
            {
                if (_disposed || !_interactive) return;
                _frame = (_frame + 1) % _frames.Length;
                Quietly(Draw);   // a broken writer threw out of Tick, though the class promises drawing never throws
            }
        }

        /// <summary>Stops, erases the spinner line and writes <paramref name="finalLine"/> (if given) in its place.</summary>
        public void Stop(string? finalLine = null)
        {
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                _cts.Cancel();
            }
            try
            {
                lock (_gate)
                {
                    if (_interactive) _writer.Write("\r" + new string(' ', _lastWidth) + "\r");
                    if (finalLine != null) _writer.WriteLine(finalLine);
                    _writer.Flush();
                }
            }
            finally
            {
                try { _loop?.Wait(); } catch (AggregateException) { }
                _cts.Dispose();
            }
        }

        /// <summary>Stops and erases the spinner; an output failure (e.g. a closed pipe) is ignored.</summary>
        public void Dispose()
        {
            // Any writer failure: a writer that threw NotSupportedException escaped (only IOException and
            // ObjectDisposedException were caught).
            try { Stop(); }
            catch (Exception) { }
        }

        private void Draw()
        {
            // One line: a line break in the message ('\r' returns only to the start of the last line) scrolled a new
            // line on every frame, and tabs threw the padding out.
            string line = OneLine(_frames[_frame] + " " + _message);
            if (_isConsole)
            {
                int width;
                try { width = Console.WindowWidth; } catch (Exception) { width = 0; } // no console window (e.g. a service)
                if (width > 1) line = Fit(line, width - 1);
            }
            int columns = Columns(line);
            int pad = Math.Max(0, _lastWidth - columns);
            _writer.Write("\r" + line + new string(' ', pad));
            _writer.Flush();
            _lastWidth = columns;
        }

        // Control characters become spaces, except an SGR colour sequence (ESC [ digits and semicolons m), which is kept:
        // its ESC became a space, so an AnsiStyle message showed "[32m" as text.
        internal static string OneLine(string s)
        {
            char[]? chars = null;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                int sgr = Sgr(s, i);
                if (sgr > 0) i += sgr - 1;
                else if (char.IsControl(c) || c == '\u2028' || c == '\u2029')
                    (chars ??= s.ToCharArray())[i] = ' ';
            }
            return chars == null ? s : new string(chars);
        }

        // The length of the SGR sequence at i, or 0.
        private static int Sgr(string s, int i)
        {
            if (s[i] != '\u001B' || i + 1 >= s.Length || s[i + 1] != '[') return 0;
            for (int j = i + 2; j < s.Length; j++)
            {
                char c = s[j];
                if (c == 'm') return j - i + 1;
                if ((c < '0' || c > '9') && c != ';') return 0;
            }
            return 0;
        }

        // The line cut to a number of screen columns. It was cut in UTF-16 units: a cut inside a surrogate pair left half
        // an emoji, and wide characters (two columns each) still wrapped the line.
        internal static string Fit(string line, int maxColumns)
        {
            int used = 0;
            bool styled = false;
            for (int i = 0; i < line.Length;)
            {
                int n = Units(line, i);
                int w = Width(line, i, n);
                if (used + w > maxColumns) return styled ? line.Substring(0, i) + "\u001B[0m" : line.Substring(0, i);   // no colour left on
                if (line[i] == '\u001B') styled = true;
                used += w;
                i += n;
            }
            return line;
        }

        internal static int Columns(string line)
        {
            int used = 0;
            for (int i = 0; i < line.Length; i += Units(line, i)) used += Width(line, i, Units(line, i));
            return used;
        }

        private static int Units(string s, int i)
        {
            if (s[i] == '\u001B') return Math.Max(1, Sgr(s, i));   // a colour sequence is one unit of no width
            // A zero-width joiner and the character it joins are one unit of no width, and a pair of regional
            // indicators (a flag) one unit of two: the family U+1F468 ZWJ U+1F469 ZWJ U+1F467 counted 6, a flag 4.
            if (s[i] == '\u200D') return i + 1 < s.Length ? 1 + Single(s, i + 1) : 1;
            int n = Single(s, i);
            if (n == 2 && IsRegional(s, i) && i + 3 < s.Length && Single(s, i + 2) == 2 && IsRegional(s, i + 2)) return 4;
            return n;
        }

        private static int Single(string s, int i) => char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]) ? 2 : 1;

        private static bool IsRegional(string s, int i) => s[i] == '\uD83C' && s[i + 1] >= '\uDDE6' && s[i + 1] <= '\uDDFF';

        // Columns for one character: none for marks, format characters, Hangul medial and final jamo (they join the
        // initial one) and emoji skin-tone modifiers; two for an East Asian wide character, a BMP emoji shown as an emoji
        // (U+2705, U+26A1, U+2B50), an astral emoji or symbol (U+1F000-1FAFF) or a CJK extension (planes 2 and 3); else
        // one. Only the wide characters of U+1F000-1FAFF count two (the whole block did: U+1F700, U+1F800, U+1F10B and
        // U+1F0A1 are narrow), and U+303F and the Yijing hexagrams (U+4DC0) are narrow inside the CJK range; Hangul
        // Jamo Extended-B (U+D7B0) is medial and final jamo, of no width. A flag (two regional indicators) is two and a
        // ZWJ with the character it joins none (Units groups them). Every astral character counted two (U+1D400, a
        // narrow letter, too), and BMP emoji counted one. A symbol
        // followed by U+FE0F (emoji presentation, as in U+2764 U+FE0F) is two; the selector itself is none. Missing wide
        // ranges counted one: vertical and small forms (U+FE10, U+FE50), Hangul Jamo Extended-A (U+A960), Tangut,
        // Khitan and the ideographic symbols (U+16FE0-18DFF), and the kana supplements (U+1AFF0-1B2FF).
        private static int Width(string s, int i, int n)
        {
            if (s[i] == '\u001B' || s[i] == '\u200D') return 0;   // an SGR sequence, or a joined character (Units measured it)
            if (n == 4) return 2;   // a flag
            UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(s, i);
            if (category == UnicodeCategory.NonSpacingMark || category == UnicodeCategory.EnclosingMark || category == UnicodeCategory.Format)
                return 0;
            if (n == 2)
            {
                int cp = char.ConvertToUtf32(s[i], s[i + 1]);
                if (cp >= 0x1F3FB && cp <= 0x1F3FF) return 0;
                return WideAstral(cp) || (cp >= 0x20000 && cp <= 0x3FFFD)
                    || (cp >= 0x16FE0 && cp <= 0x18DFF) || (cp >= 0x1AFF0 && cp <= 0x1B2FF) || EmojiStyle(s, i + 2, category) ? 2 : 1;
            }
            char ch = s[i];
            if ((ch >= '\u1160' && ch <= '\u11FF') || (ch >= '\uD7B0' && ch <= '\uD7FF')) return 0;
            bool cjk = ch >= '\u2E80' && ch <= '\uA4CF' && ch != '\u303F' && (ch < '\u4DC0' || ch > '\u4DFF');
            if ((ch >= '\u1100' && ch <= '\u115F') || cjk || (ch >= '\uA960' && ch <= '\uA97F')
                || (ch >= '\uAC00' && ch <= '\uD7A3') || (ch >= '\uF900' && ch <= '\uFAFF') || (ch >= '\uFE10' && ch <= '\uFE19')
                || (ch >= '\uFE30' && ch <= '\uFE6F') || (ch >= '\uFF00' && ch <= '\uFF60') || (ch >= '\uFFE0' && ch <= '\uFFE6')) return 2;
            for (int k = 0; k < WideEmoji.Length; k += 2)
                if (ch >= WideEmoji[k] && ch <= WideEmoji[k + 1]) return 2;
            return EmojiStyle(s, i + 1, category) ? 2 : 1;
        }

        // The East Asian Width W ranges of U+1F000-1FAFF (Unicode 16), plus the regional indicators (a lone one is
        // drawn as a letter in a box, two columns on the terminals that draw flags).
        private static readonly int[] WideAstralRanges =
        {
            0x1F004, 0x1F004, 0x1F0CF, 0x1F0CF, 0x1F18E, 0x1F18E, 0x1F191, 0x1F19A, 0x1F1E6, 0x1F1FF, 0x1F200, 0x1F202,
            0x1F210, 0x1F23B, 0x1F240, 0x1F248, 0x1F250, 0x1F251, 0x1F260, 0x1F265, 0x1F300, 0x1F320, 0x1F32D, 0x1F335,
            0x1F337, 0x1F37C, 0x1F37E, 0x1F393, 0x1F3A0, 0x1F3CA, 0x1F3CF, 0x1F3D3, 0x1F3E0, 0x1F3F0, 0x1F3F4, 0x1F3F4,
            0x1F3F8, 0x1F43E, 0x1F440, 0x1F440, 0x1F442, 0x1F4FC, 0x1F4FF, 0x1F53D, 0x1F54B, 0x1F54E, 0x1F550, 0x1F567,
            0x1F57A, 0x1F57A, 0x1F595, 0x1F596, 0x1F5A4, 0x1F5A4, 0x1F5FB, 0x1F64F, 0x1F680, 0x1F6C5, 0x1F6CC, 0x1F6CC,
            0x1F6D0, 0x1F6D2, 0x1F6D5, 0x1F6D7, 0x1F6DC, 0x1F6DF, 0x1F6EB, 0x1F6EC, 0x1F6F4, 0x1F6FC, 0x1F7E0, 0x1F7EB,
            0x1F7F0, 0x1F7F0, 0x1F90C, 0x1F93A, 0x1F93C, 0x1F945, 0x1F947, 0x1F9FF, 0x1FA70, 0x1FA7C, 0x1FA80, 0x1FA89,
            0x1FA8F, 0x1FAC6, 0x1FACE, 0x1FADC, 0x1FADF, 0x1FAE9, 0x1FAF0, 0x1FAF8,
        };

        private static bool WideAstral(int cp)
        {
            if (cp < 0x1F004 || cp > 0x1FAF8) return false;
            for (int k = 0; k < WideAstralRanges.Length; k += 2)
                if (cp >= WideAstralRanges[k] && cp <= WideAstralRanges[k + 1]) return true;
            return false;
        }

        private static bool EmojiStyle(string s, int next, UnicodeCategory category)
            => next < s.Length && s[next] == '\uFE0F' && category == UnicodeCategory.OtherSymbol;

        // The BMP characters with Emoji_Presentation (East Asian Width W), as inclusive ranges.
        private static readonly char[] WideEmoji =
        {
            '\u231A', '\u231B', '\u2329', '\u232A', '\u23E9', '\u23EC', '\u23F0', '\u23F0', '\u23F3', '\u23F3',
            '\u25FD', '\u25FE', '\u2614', '\u2615', '\u2648', '\u2653', '\u267F', '\u267F', '\u2693', '\u2693',
            '\u26A1', '\u26A1', '\u26AA', '\u26AB', '\u26BD', '\u26BE', '\u26C4', '\u26C5', '\u26CE', '\u26CE',
            '\u26D4', '\u26D4', '\u26EA', '\u26EA', '\u26F2', '\u26F3', '\u26F5', '\u26F5', '\u26FA', '\u26FA',
            '\u26FD', '\u26FD', '\u2705', '\u2705', '\u270A', '\u270B', '\u2728', '\u2728', '\u274C', '\u274C',
            '\u274E', '\u274E', '\u2753', '\u2755', '\u2757', '\u2757', '\u2795', '\u2797', '\u27B0', '\u27B0',
            '\u27BF', '\u27BF', '\u2B1B', '\u2B1C', '\u2B50', '\u2B50', '\u2B55', '\u2B55',
        };

        // A broken writer (a closed pipe) threw out of the constructor and the Message setter, though Dispose ignored the
        // same failure; the spinner is cosmetic, so drawing fails quietly everywhere.
        private void Quietly(Action write)
        {
            // Any failure, not only IOException and ObjectDisposedException: a writer that threw NotSupportedException
            // made the constructor throw.
            try { write(); }
            catch (Exception) { _broken = true; }
        }

        private volatile bool _broken; // a write failed: the background loop stops animating
    }
}
