// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;
using System.Text;

namespace ToolBelt.Cli
{
    /// <summary>The 16 standard terminal colours.</summary>
    public enum AnsiColor
    {
        Black, Red, Green, Yellow, Blue, Magenta, Cyan, White,
        BrightBlack, BrightRed, BrightGreen, BrightYellow, BrightBlue, BrightMagenta, BrightCyan, BrightWhite,
    }

    /// <summary>
    /// Builds ANSI SGR-styled strings — colours (16, 256 or 24-bit), bold, dim, italic, underline — and decides whether
    /// colour should be emitted at all, following the conventions: <c>NO_COLOR</c> (any non-empty value) disables colour,
    /// <c>FORCE_COLOR</c> / <c>CLICOLOR_FORCE</c> enables it, <c>TERM=dumb</c> disables it, and otherwise colour is used only
    /// when output is not redirected. The environment probe is injectable for tests. When disabled every method returns
    /// the plain text, so call sites never branch.
    /// </summary>
    public sealed class AnsiStyle
    {
        private const string Reset = "\u001b[0m";

        public AnsiStyle(bool enabled) => Enabled = enabled;

        /// <summary>Whether escape sequences are emitted.</summary>
        public bool Enabled { get; }

        /// <summary>A styler for standard output, enabled per <see cref="ShouldUseColor"/>.</summary>
        public static AnsiStyle ForConsole() => new AnsiStyle(ShouldUseColor(Environment.GetEnvironmentVariable, Console.IsOutputRedirected));

        /// <summary>The colour decision: NO_COLOR wins, then FORCE_COLOR/CLICOLOR_FORCE, then TERM=dumb, then "is a terminal".</summary>
        public static bool ShouldUseColor(Func<string, string?> getEnvironmentVariable, bool outputRedirected)
        {
            if (getEnvironmentVariable is null) throw new ArgumentNullException(nameof(getEnvironmentVariable));
            if (!string.IsNullOrEmpty(getEnvironmentVariable("NO_COLOR"))) return false;
            string? force = getEnvironmentVariable("FORCE_COLOR");
            if (!string.IsNullOrEmpty(force)) return force != "0" && !string.Equals(force, "false", StringComparison.OrdinalIgnoreCase);
            string? cliForce = getEnvironmentVariable("CLICOLOR_FORCE");
            if (!string.IsNullOrEmpty(cliForce) && cliForce != "0") return true;
            if (string.Equals(getEnvironmentVariable("TERM"), "dumb", StringComparison.Ordinal)) return false;
            return !outputRedirected;
        }

        public string Foreground(string text, AnsiColor color) => Wrap(text, ForegroundCode(color).ToString());
        public string Background(string text, AnsiColor color) => Wrap(text, (ForegroundCode(color) + 10).ToString());

        /// <summary>A colour from the 256-colour palette.</summary>
        public string Foreground256(string text, int index) => Wrap(text, "38;5;" + Check256(index));
        public string Background256(string text, int index) => Wrap(text, "48;5;" + Check256(index));

        /// <summary>A 24-bit colour.</summary>
        public string Rgb(string text, byte r, byte g, byte b) => Wrap(text, $"38;2;{r};{g};{b}");
        public string BackgroundRgb(string text, byte r, byte g, byte b) => Wrap(text, $"48;2;{r};{g};{b}");

        public string Bold(string text) => Wrap(text, "1");
        public string Dim(string text) => Wrap(text, "2");
        public string Italic(string text) => Wrap(text, "3");
        public string Underline(string text) => Wrap(text, "4");
        public string Inverse(string text) => Wrap(text, "7");

        // Semantic shortcuts.
        public string Success(string text) => Foreground(text, AnsiColor.Green);
        public string Warning(string text) => Foreground(text, AnsiColor.Yellow);
        public string Error(string text) => Foreground(text, AnsiColor.Red);
        public string Muted(string text) => Foreground(text, AnsiColor.BrightBlack);

        /// <summary>Several attributes at once in one sequence, e.g. <c>Combine(text, bold: true, foreground: AnsiColor.Red)</c>.</summary>
        public string Combine(string text, AnsiColor? foreground = null, AnsiColor? background = null, bool bold = false, bool underline = false)
        {
            var codes = new List<string>();
            if (bold) codes.Add("1");
            if (underline) codes.Add("4");
            if (foreground is AnsiColor f) codes.Add(ForegroundCode(f).ToString());
            if (background is AnsiColor b) codes.Add((ForegroundCode(b) + 10).ToString());
            return codes.Count == 0 ? text ?? throw new ArgumentNullException(nameof(text)) : Wrap(text, string.Join(";", codes));
        }

        private string Wrap(string text, string code)
        {
            if (text is null) throw new ArgumentNullException(nameof(text));
            if (!Enabled || text.Length == 0) return text;
            // Re-apply our style after any reset inside the text, so nesting styles doesn't cut the outer one short.
            string open = "\u001b[" + code + "m";
            return new StringBuilder(open).Append(text.Replace(Reset, Reset + open)).Append(Reset).ToString();
        }

        private static int ForegroundCode(AnsiColor c) => (int)c < 8 ? 30 + (int)c : 90 + (int)c - 8;

        private static int Check256(int index)
            => index >= 0 && index <= 255 ? index : throw new ArgumentOutOfRangeException(nameof(index), index, "Palette index must be 0–255.");
    }
}
