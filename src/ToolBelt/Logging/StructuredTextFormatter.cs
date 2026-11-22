// ToolBelt drop-in — also copy Logging/Log.cs.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ToolBelt.Logging
{
    /// <summary>A field of a log event, for building a structured line format.</summary>
    public enum LogField
    {
        Timestamp,
        Level,
        Category,
        Message,
        Exception,
    }

    /// <summary>
    /// A configurable line formatter: choose which fields appear, in what order, with which delimiter and
    /// timestamp format. This gives log files a machine-parseable shape (split each line on the delimiter) without
    /// abandoning the plain-string event model. Use <see cref="Format"/> as the sink's formatter. Newlines and the
    /// delimiter inside a value become spaces; nothing else is escaped, so a quoting CSV reader may misread a value
    /// that contains a <c>"</c>, and with the default space delimiter a value containing spaces is not separable —
    /// put free text such as the message last, or pick a delimiter like a tab.
    /// </summary>
    public sealed class StructuredTextFormatter
    {
        private readonly IReadOnlyList<LogField> _fields;
        private readonly string _delimiter;
        private readonly string _timestampFormat;
        // The delimiter's KMP failure links, and for each matched-prefix length s whether some k in s's border chain is a
        // tail that runs into the delimiter (see TrimOverlap).
        private readonly int[] _fail;
        private readonly bool[] _overlap;

        public StructuredTextFormatter(IReadOnlyList<LogField> fields, string delimiter = " ", string timestampFormat = "o")
        {
            if (fields is null) throw new ArgumentNullException(nameof(fields));
            if (fields.Count == 0) throw new ArgumentException("At least one field is required.", nameof(fields));
            _fields = new List<LogField>(fields); // a copy: clearing the caller's list later emptied every line
            // An undefined field printed an empty column without complaint.
            foreach (LogField f in _fields)
                if (f < LogField.Timestamp || f > LogField.Exception)
                    throw new ArgumentOutOfRangeException(nameof(fields), f, "Unknown log field.");
            _delimiter = delimiter ?? throw new ArgumentNullException(nameof(delimiter));
            _timestampFormat = timestampFormat ?? throw new ArgumentNullException(nameof(timestampFormat));
            int n = delimiter.Length;
            _fail = new int[n + 1];
            for (int i = 1, t = 0; i < n; i++)
            {
                while (t > 0 && delimiter[i] != delimiter[t]) t = _fail[t];
                if (delimiter[i] == delimiter[t]) t++;
                _fail[i + 1] = t;
            }
            // k (1 to n - 1) runs into the delimiter when the delimiter's last n - k characters are its first: n - k is a
            // border, one of the failure chain from n.
            var period = new bool[n + 1];
            for (int b = n > 0 ? _fail[n] : 0; b > 0; b = _fail[b]) period[n - b] = true;
            _overlap = new bool[n + 1];
            for (int s = 1; s < n; s++) _overlap[s] = period[s] || _overlap[_fail[s]];
            try { new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero).ToString(timestampFormat, CultureInfo.InvariantCulture); }
            catch (FormatException ex) { throw new ArgumentException("Invalid timestamp format: " + ex.Message, nameof(timestampFormat), ex); }
        }

        /// <summary>A tab-delimited timestamp/level/category/message formatter (a delimited preset).</summary>
        public static StructuredTextFormatter Delimited(string delimiter = "\t")
            => new StructuredTextFormatter(new[] { LogField.Timestamp, LogField.Level, LogField.Category, LogField.Message }, delimiter);

        /// <summary>Formats an event into a single line. Delimiters inside a field value are replaced with a space.</summary>
        public string Format(LogEvent e)
        {
            if (e is null) throw new ArgumentNullException(nameof(e));
            var sb = new StringBuilder();
            for (int i = 0; i < _fields.Count; i++)
            {
                if (i > 0) sb.Append(_delimiter);
                sb.Append(Sanitize(FieldValue(e, _fields[i]), last: i == _fields.Count - 1));
            }
            return sb.ToString();
        }

        private string FieldValue(LogEvent e, LogField field)
        {
            switch (field)
            {
                case LogField.Timestamp: return e.Timestamp.ToString(_timestampFormat, CultureInfo.InvariantCulture);
                case LogField.Level: return e.Level.ToString();
                case LogField.Category: return e.Category;
                case LogField.Message: return e.Message;
                case LogField.Exception: return e.Exception == null ? string.Empty : Describe(e.Exception);
                default: return string.Empty;
            }
        }

        // Keep each record on one line and keep fields separable: newlines and the delimiter become spaces.
        // An exception whose ToString throws (a broken override) took the log call down. ToString recurses once per
        // level of inner exceptions and copies the inner text at each: a chain 20,000 deep overflowed the stack (which
        // no catch stops) and one 10,000 deep took 12 s, and a cycle made by reflection overflowed too. Up to 64 levels
        // (and 10,000 exceptions) keep the runtime's own text; past that, each of the first 256 exceptions gets its type,
        // message and stack, walked without recursion.
        private static string Describe(Exception ex)
        {
            var walk = new System.Collections.Generic.Stack<(Exception Exception, int Depth)>();
            walk.Push((ex, 0));
            bool deep = false;
            for (int visits = 0; walk.Count > 0 && !deep; visits++)
            {
                var (e, depth) = walk.Pop();
                if (depth > 64 || visits == 10_000) deep = true;
                else if (e is AggregateException agg) foreach (Exception inner in agg.InnerExceptions) walk.Push((inner, depth + 1));
                else if (e.InnerException != null) walk.Push((e.InnerException, depth + 1));
            }
            if (!deep)
            {
                try { return ex.ToString(); }
                catch (Exception inner) { return ex.GetType().FullName + " (its ToString threw " + inner.GetType().FullName + ")"; }
            }
            var seen = new System.Collections.Generic.List<Exception>();
            var todo = new System.Collections.Generic.Stack<Exception>();
            todo.Push(ex);
            bool again = false, more = false;
            while (todo.Count > 0)
            {
                Exception e = todo.Pop();
                if (seen.Exists(s => ReferenceEquals(s, e))) { again = true; continue; }
                if (seen.Count == 256) { more = true; break; }
                seen.Add(e);
                if (e is AggregateException agg)
                {
                    var inner = agg.InnerExceptions;
                    for (int i = inner.Count - 1; i >= 0; i--) todo.Push(inner[i]);
                }
                else if (e.InnerException != null) todo.Push(e.InnerException);
            }
            var sb = new StringBuilder();
            for (int i = 0; i < seen.Count; i++)
            {
                Exception e = seen[i];
                if (i > 0) sb.Append(System.Environment.NewLine).Append(" ---> ");
                sb.Append(e.GetType().FullName).Append(": ");
                try { sb.Append(e.Message); }
                catch (Exception inner) { sb.Append("(its Message threw ").Append(inner.GetType().FullName).Append(')'); }
                string? trace;
                try { trace = e.StackTrace; }
                catch (Exception) { trace = null; }
                if (!string.IsNullOrEmpty(trace)) sb.Append(System.Environment.NewLine).Append(trace);
            }
            if (again) sb.Append(System.Environment.NewLine).Append(" ---> (an exception shown above appears again)");
            if (more) sb.Append(System.Environment.NewLine).Append(" ---> (more inner exceptions not shown)");
            return sb.ToString();
        }

        private string Sanitize(string value, bool last)
        {
            // The Unicode line breaks too (NEL, LS, PS): a viewer that honours them split the record.
            value = value.Replace("\r", " ").Replace("\n", " ").Replace("\u0085", " ").Replace("\u2028", " ").Replace("\u2029", " ");
            // Until none is left: one pass of Replace can make a new delimiter when the delimiter has a space in it
            // (" | | " became " |  | ", and ",, " became ", , " for ", "). Repeating Replace took quadratic time (a
            // 100,000-character value took 3 s), so one pass keeps the output free of the delimiter as it goes: a
            // delimiter can only appear at its end, and becomes a space there, which is checked again.
            if (_delimiter.Length > 0 && _delimiter != " " && value.IndexOf(_delimiter, StringComparison.Ordinal) >= 0)
                value = _delimiter.Length == 1 ? value.Replace(_delimiter, " ") : RemoveDelimiters(value);
            if (!last) value = TrimOverlap(value);
            return value;
        }

        // The output's matched-prefix length after each character is kept, so a delimiter at the end is found in constant
        // time and, once it is replaced, matching resumes from the state before it. Comparing the whole delimiter at
        // every character made this O(value × delimiter): 4.8 s for 100,000 characters and a 16,384-character delimiter.
        private string RemoveDelimiters(string value)
        {
            int n = _delimiter.Length;
            var sb = new StringBuilder(value.Length);
            var states = new List<int>(value.Length + 1) { 0 };
            var memo = new Dictionary<long, int>();
            foreach (char c in value)
            {
                sb.Append(c);
                states.Add(Step(states[states.Count - 1], c, memo));
                while (states[states.Count - 1] == n)
                {
                    sb.Length -= n;
                    states.RemoveRange(states.Count - n, n);
                    sb.Append(' ');
                    states.Add(Step(states[states.Count - 1], ' ', memo));
                }
            }
            return sb.ToString();
        }

        // The matched-prefix length after state s reads c. Transitions are memoized per call, filled along the failure
        // chain without recursion, so each (state, character) pair is worked out once.
        private int Step(int s, char c, Dictionary<long, int> memo)
        {
            int n = _delimiter.Length;
            List<int>? pending = null;
            int t = s, result;
            while (true)
            {
                if (t < n && _delimiter[t] == c) { result = t + 1; break; }
                if (t == 0) { result = 0; break; }
                if (memo.TryGetValue(((long)t << 16) | c, out result)) break;
                (pending ??= new List<int>()).Add(t);
                t = _fail[t];
            }
            if (pending != null)
                foreach (int p in pending) memo[((long)p << 16) | c] = result;
            return result;
        }

        // A value whose end runs into the delimiter that follows it ("x-" then "--", "a |" then " | ") made a delimiter
        // inside the value, and the line split one place early. Such a tail becomes spaces.
        // A tail of k characters runs into the delimiter when it is the delimiter's first k and those are followed in the
        // delimiter by its own start again; the tails of the value that are prefixes of the delimiter are the matched
        // state's failure chain, so _overlap answers for all of them at once. Trying every k with a comparison of each
        // was O(value × delimiter²).
        private string TrimOverlap(string value)
        {
            int n = _delimiter.Length;
            if (n < 2 || value.Length == 0 || _delimiter.IndexOf(value[value.Length - 1], 0, n - 1) < 0) return value;
            var sb = new StringBuilder(value);
            var states = new int[value.Length + 1];
            var memo = new Dictionary<long, int>();
            for (int i = 0; i < value.Length; i++) states[i + 1] = Step(states[i], value[i], memo);
            int length = value.Length;
            // Each step blanks or drops a character, so it ends (a space dropped: the delimiter starts with one).
            while (length > 0 && _overlap[states[length]])
            {
                if (sb[length - 1] != ' ')
                {
                    sb[length - 1] = ' ';
                    states[length] = Step(states[length - 1], ' ', memo);
                }
                else sb.Length = --length;
            }
            return sb.ToString();
        }
    }
}
