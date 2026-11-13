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
                        while (!_cts.IsCancellationRequested)
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
                Draw();
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
            try { Stop(); }
            catch (IOException) { }
            catch (ObjectDisposedException) { }
        }

        private void Draw()
        {
            string line = _frames[_frame] + " " + _message;
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

        // The line cut to a number of screen columns. It was cut in UTF-16 units: a cut inside a surrogate pair left half
        // an emoji, and wide characters (two columns each) still wrapped the line.
        internal static string Fit(string line, int maxColumns)
        {
            int used = 0;
            for (int i = 0; i < line.Length;)
            {
                int n = Units(line, i);
                int w = Width(line, i, n);
                if (used + w > maxColumns) return line.Substring(0, i);
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
            => char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]) ? 2 : 1;

        // Columns for one character: none for marks and format characters, two for an astral character (mostly emoji
        // and CJK extensions) or an East Asian wide one, else one.
        private static int Width(string s, int i, int n)
        {
            UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(s, i);
            if (category == UnicodeCategory.NonSpacingMark || category == UnicodeCategory.EnclosingMark || category == UnicodeCategory.Format)
                return 0;
            if (n == 2) return 2;
            char ch = s[i];
            return (ch >= '\u1100' && ch <= '\u115F') || (ch >= '\u2E80' && ch <= '\uA4CF') || (ch >= '\uAC00' && ch <= '\uD7A3')
                || (ch >= '\uF900' && ch <= '\uFAFF') || (ch >= '\uFE30' && ch <= '\uFE4F') || (ch >= '\uFF00' && ch <= '\uFF60')
                || (ch >= '\uFFE0' && ch <= '\uFFE6') ? 2 : 1;
        }

        // A broken writer (a closed pipe) threw out of the constructor and the Message setter, though Dispose ignored the
        // same failure; the spinner is cosmetic, so drawing fails quietly everywhere.
        private void Quietly(Action write)
        {
            try { write(); }
            catch (IOException) { }
            catch (ObjectDisposedException) { }
        }
    }
}
