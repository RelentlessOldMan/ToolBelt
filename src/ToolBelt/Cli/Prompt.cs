// ToolBelt drop-in — fully self-contained (BCL only).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace ToolBelt.Cli
{
    /// <summary>Thrown when a prompt needs an answer but there is no one to ask (input redirected or exhausted, no default).</summary>
    public sealed class NonInteractiveException : InvalidOperationException
    {
        public NonInteractiveException(string message) : base(message) { }
    }

    /// <summary>
    /// Console questions that behave in scripts: yes/no confirmation, free text with a default, validated parsing, and a
    /// numbered choice list. Invalid answers re-ask (up to <see cref="MaxAttempts"/>). When the session is
    /// non-interactive — input redirected, or end of input reached — a question with a default silently takes it, and one
    /// without throws <see cref="NonInteractiveException"/> naming the question, instead of hanging a CI job forever or
    /// reading garbage. Reader, writer and interactivity are injectable for tests.
    /// </summary>
    public sealed class Prompt
    {
        private readonly TextReader _in;
        private readonly TextWriter _out;

        public Prompt(TextReader? input = null, TextWriter? output = null, bool? interactive = null)
        {
            _in = input ?? Console.In;
            _out = output ?? Console.Out;
            IsInteractive = interactive ?? (input is null && !Console.IsInputRedirected);
        }

        /// <summary>Whether questions are actually asked (otherwise defaults are used).</summary>
        public bool IsInteractive { get; }

        /// <summary>Re-asks after an invalid answer at most this many times in total (default 5) before throwing.</summary>
        public int MaxAttempts { get; set; } = 5;

        /// <summary>Yes/no. Accepts y/yes/n/no (any case); an empty answer takes the default.</summary>
        public bool Confirm(string question, bool? defaultValue = null)
        {
            string hint = defaultValue switch { true => "[Y/n]", false => "[y/N]", _ => "[y/n]" };
            return Ask(question + " " + hint, defaultValue, s =>
            {
                switch (s.Trim().ToLowerInvariant())
                {
                    case "y": case "yes": return (true, true, null);
                    case "n": case "no": return (true, false, null);
                    default: return (false, false, "Please answer y or n.");
                }
            }, showDefault: false);
        }

        /// <summary>Free text; empty input takes <paramref name="defaultValue"/> if there is one, else re-asks.</summary>
        public string Text(string question, string? defaultValue = null)
            => Ask(question, defaultValue, s => s.Length == 0 ? (false, "", "A value is required.") : (true, s, null), showDefault: true);

        /// <summary>A value parsed (and optionally validated) by <paramref name="parse"/>, which returns null for invalid input.</summary>
        public T Value<T>(string question, Func<string, T?> parse, T? defaultValue = null, string invalidMessage = "That isn't a valid value.") where T : struct
        {
            if (parse is null) throw new ArgumentNullException(nameof(parse));
            return Ask(question, defaultValue, s => parse(s) is T v ? (true, v, null) : (false, default, invalidMessage), showDefault: true);
        }

        /// <summary>An integer within [<paramref name="min"/>, <paramref name="max"/>] (invariant culture).</summary>
        public int Integer(string question, int min = int.MinValue, int max = int.MaxValue, int? defaultValue = null)
            => Value(question, s => int.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) && v >= min && v <= max ? v : (int?)null,
                defaultValue, min == int.MinValue && max == int.MaxValue ? "Please enter a whole number." : $"Please enter a whole number from {min} to {max}.");

        /// <summary>Lists the options numbered from 1 and returns the chosen index (a number or an option's exact text, any case).</summary>
        public int Choose(string question, IReadOnlyList<string> options, int? defaultIndex = null)
        {
            if (options is null || options.Count == 0) throw new ArgumentException("Need at least one option.", nameof(options));
            if (defaultIndex is int d && (d < 0 || d >= options.Count)) throw new ArgumentOutOfRangeException(nameof(defaultIndex));
            if (IsInteractive)
            {
                _out.WriteLine(question);
                for (int i = 0; i < options.Count; i++) _out.WriteLine($"  {i + 1}) {options[i]}");
            }
            return Ask("Choice", defaultIndex is int di ? di : (int?)null, s =>
            {
                string t = s.Trim();
                if (int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) && n >= 1 && n <= options.Count) return (true, n - 1, null);
                for (int i = 0; i < options.Count; i++)
                    if (string.Equals(options[i], t, StringComparison.OrdinalIgnoreCase)) return (true, i, null);
                return (false, 0, $"Please enter 1–{options.Count}.");
            }, showDefault: false, defaultLabel: defaultIndex is int dl ? (dl + 1).ToString(CultureInfo.InvariantCulture) : null, context: question);
        }

        private T Ask<T>(string question, T? defaultValue, Func<string, (bool Ok, T Value, string? Error)> parse, bool showDefault,
            string? defaultLabel = null, string? context = null) where T : struct
            => AskCore(question, defaultValue.HasValue, defaultValue.GetValueOrDefault(), parse, showDefault, defaultLabel ?? defaultValue?.ToString(), context);

        private string Ask(string question, string? defaultValue, Func<string, (bool Ok, string Value, string? Error)> parse, bool showDefault)
            => AskCore(question, defaultValue != null, defaultValue!, parse, showDefault, defaultValue, null);

        private T AskCore<T>(string question, bool hasDefault, T defaultValue, Func<string, (bool Ok, T Value, string? Error)> parse,
            bool showDefault, string? defaultLabel, string? context)
        {
            if (question is null) throw new ArgumentNullException(nameof(question));
            string what = context ?? question;
            if (!IsInteractive)
            {
                if (hasDefault) return defaultValue;
                throw new NonInteractiveException($"No answer available for \"{what}\": input is not interactive and there is no default.");
            }
            string label = question + (showDefault && hasDefault && defaultLabel is { Length: > 0 } ? $" [{defaultLabel}]" : "")
                + (!showDefault && hasDefault && defaultLabel != null && context != null ? $" [{defaultLabel}]" : "") + ": ";
            for (int attempt = 0; attempt < Math.Max(1, MaxAttempts); attempt++)
            {
                _out.Write(label);
                _out.Flush();
                string? line = _in.ReadLine();
                if (line is null)
                {
                    _out.WriteLine();
                    if (hasDefault) return defaultValue;
                    throw new NonInteractiveException($"Input ended before \"{what}\" was answered.");
                }
                if (line.Trim().Length == 0 && hasDefault) return defaultValue;
                var (ok, value, error) = parse(line);
                if (ok) return value;
                _out.WriteLine(error);
            }
            throw new InvalidOperationException($"No valid answer to \"{what}\" after {MaxAttempts} attempts.");
        }
    }
}
