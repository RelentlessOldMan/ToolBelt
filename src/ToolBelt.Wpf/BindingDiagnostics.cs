// ToolBelt.Wpf drop-in — Windows-only (net8.0-windows), self-contained (WPF only).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Data;

namespace ToolBelt.Wpf
{
    /// <summary>One WPF data-binding failure.</summary>
    public sealed class BindingFailure
    {
        internal BindingFailure(int code, string message)
        {
            Code = code;
            Message = message;
            // WPF follows the path with "; DataItem=": a ';' inside an indexer ("Map[a;b].Name") cut the path at "Map[a".
            Path = Match(message, @"BindingExpression:Path=(.*?); DataItem=") ?? Match(message, @"BindingExpression:Path=((?:\[[^\]]*\]|[^;\[])*);");
            TargetProperty = Match(message, @"target property is '([^']*)'");
        }

        /// <summary>The WPF trace id (40 = path error, 4 = source not found, 23 = conversion failure, …).</summary>
        public int Code { get; }

        public string Message { get; }

        /// <summary>The binding path, when the message names one.</summary>
        public string? Path { get; }

        /// <summary>The target dependency property, when the message names one.</summary>
        public string? TargetProperty { get; }

        public override string ToString() => $"Binding error {Code}: {Message}";

        private static string? Match(string s, string pattern)
        {
            var m = Regex.Match(s, pattern, RegexOptions.Singleline);
            return m.Success ? m.Groups[1].Value : null;
        }
    }

    /// <summary>
    /// Makes WPF's silent data-binding failures visible. WPF only writes "System.Windows.Data Error: 40 :
    /// BindingExpression path error…" to the debugger output; a typo in a binding path otherwise just shows an empty
    /// field. Install a listener and every failure raises <see cref="BindingFailed"/> and is kept in <see cref="Failures"/>;
    /// with <see cref="ThrowOnFailure"/> the binding throws instead — the right setting in UI tests and debug builds so a
    /// broken binding fails loudly. Dispose to detach. Warnings (e.g. fallback values) are ignored unless
    /// <see cref="IncludeWarnings"/>. At most <see cref="MaxFailures"/> are kept (the oldest go first; every one is still
    /// raised).
    /// </summary>
    public sealed class BindingFailureListener : TraceListener
    {
        private readonly StringBuilder _pending = new StringBuilder();
        // A queue: at the cap, RemoveRange(0, 1) shifted the whole list on every failure.
        private readonly Queue<BindingFailure> _failures = new Queue<BindingFailure>();
        private readonly object _gate = new object();
        private int _disposed;
        private int _maxFailures = 10000;
        private const int MaxPendingChars = 1 << 16;

        // The trace level is process-wide: raise it once for the first listener and restore it after the last, so listeners
        // disposed in any order never switch tracing off under one that is still attached.
        private static readonly object LevelGate = new object();
        private static int _activeListeners;
        private static SourceLevels _originalLevel;

        private static readonly PropertyInfo[] OtherSources = typeof(PresentationTraceSources)
            .GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Where(p => p.PropertyType == typeof(TraceSource) && p.Name != nameof(PresentationTraceSources.DataBindingSource))
            .ToArray();

        public BindingFailureListener()
        {
            lock (LevelGate)
            {
                var source = PresentationTraceSources.DataBindingSource;
                // Counted only once set up: a setup that threw left the count raised, so every later listener skipped the
                // setup and left DataBindingSource Off.
                if (_activeListeners == 0)
                {
                    // Refresh() turns binding tracing on when no debugger is attached, but re-reads config and resets the level:
                    // remember the app's level first and re-apply it, adding Warning if it lacks it. (Flags, not ordering:
                    // SourceLevels.All is -1, which a "< Warning" test would wrongly "raise" to Warning.)
                    _originalLevel = source.Switch.Level;
                    // Refresh() resets every WPF source, so the app's RoutedEventSource (and the rest) went Off for good.
                    SourceLevels[] others = OtherSources.Select(p => ((TraceSource)p.GetValue(null)!).Switch.Level).ToArray();
                    // Refresh() also runs Trace.Refresh(), which re-reads config for every Switch and TraceSource in the
                    // process and rebuilds Trace.Listeners: the app's own sources and switches fell back to their defaults
                    // and listeners it added in code were gone. Put them all back.
                    var snapshot = new TraceSnapshot();
                    PresentationTraceSources.Refresh();
                    snapshot.Restore();
                    for (int i = 0; i < OtherSources.Length; i++)
                        ((TraceSource)OtherSources[i].GetValue(null)!).Switch.Level = others[i];
                    source = PresentationTraceSources.DataBindingSource;
                    source.Switch.Level = (_originalLevel & SourceLevels.Warning) == SourceLevels.Warning ? _originalLevel : _originalLevel | SourceLevels.Warning;
                }
                source.Listeners.Add(this);
                _activeListeners++;
            }
        }

        /// <summary>Raised for each failure, on the thread that evaluated the binding (usually the UI thread).</summary>
        public event EventHandler<BindingFailure>? BindingFailed;

        /// <summary>Throw <see cref="BindingFailureException"/> from the failing binding instead of only recording it.</summary>
        public bool ThrowOnFailure { get; set; }

        public bool IncludeWarnings { get; set; }

        public IReadOnlyList<BindingFailure> Failures { get { lock (_gate) return _failures.ToArray(); } }

        /// <summary>How many failures <see cref="Failures"/> keeps (default 10,000): a listener left on for a long session grew without limit.</summary>
        public int MaxFailures
        {
            get => _maxFailures;
            set
            {
                if (value < 0) throw new ArgumentOutOfRangeException(nameof(value), value, "Must not be negative.");
                lock (_gate)
                {
                    _maxFailures = value;
                    Trim();
                }
            }
        }

        /// <summary>How many failures were dropped from <see cref="Failures"/> to stay within <see cref="MaxFailures"/>.</summary>
        public long DroppedFailures { get { lock (_gate) return _dropped; } }

        private long _dropped;

        private void Trim()
        {
            int excess = _failures.Count - _maxFailures;
            if (excess <= 0) return;
            for (int i = 0; i < excess; i++) _failures.Dequeue();
            _dropped += excess;
        }

        // Every live Switch's setting, every live TraceSource's switch and listeners, and Trace.Listeners. The runtime keeps
        // the live switches and sources in private lists of weak references; if a runtime names them differently, only
        // Trace.Listeners and WPF's own sources are restored.
        private sealed class TraceSnapshot
        {
            private static readonly PropertyInfo? SwitchSetting =
                typeof(Switch).GetProperty("SwitchSetting", BindingFlags.Instance | BindingFlags.NonPublic);

            private readonly TraceListener[] _listeners = Copy(Trace.Listeners);
            private readonly List<KeyValuePair<Switch, object>> _switches = new List<KeyValuePair<Switch, object>>();
            private readonly List<(TraceSource Source, SourceSwitch Switch, TraceListener[] Listeners)> _sources =
                new List<(TraceSource, SourceSwitch, TraceListener[])>();

            public TraceSnapshot()
            {
                foreach (TraceSource source in Live<TraceSource>("s_tracesources"))
                    _sources.Add((source, source.Switch, Copy(source.Listeners)));
                if (SwitchSetting != null && SwitchSetting.CanRead && SwitchSetting.CanWrite)
                    foreach (Switch s in Live<Switch>("s_switches"))
                    {
                        // A switch whose configured value doesn't parse throws on first read: one such switch anywhere in
                        // the app made every listener's constructor throw. It is left as it is.
                        try { _switches.Add(new KeyValuePair<Switch, object>(s, SwitchSetting.GetValue(s)!)); }
                        catch (TargetInvocationException) { }
                    }
            }

            public void Restore()
            {
                Put(Trace.Listeners, _listeners);
                foreach (var (source, sw, listeners) in _sources)
                {
                    if (!ReferenceEquals(source.Switch, sw)) source.Switch = sw;
                    Put(source.Listeners, listeners);
                }
                foreach (var pair in _switches)
                {
                    try { if (!Equals(SwitchSetting!.GetValue(pair.Key), pair.Value)) SwitchSetting.SetValue(pair.Key, pair.Value); }
                    catch (TargetInvocationException) { }   // as above: re-read after Refresh, its config may not parse
                }
            }

            private static List<T> Live<T>(string field) where T : class
            {
                var live = new List<T>();
                if (typeof(T).GetField(field, BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null) is List<WeakReference<T>> list)
                    lock (list)
                        foreach (WeakReference<T> w in list)
                            if (w.TryGetTarget(out T? item)) live.Add(item);
                return live;
            }

            private static TraceListener[] Copy(TraceListenerCollection listeners)
            {
                lock (listeners)
                {
                    var copy = new TraceListener[listeners.Count];
                    listeners.CopyTo(copy, 0);
                    return copy;
                }
            }

            private static void Put(TraceListenerCollection listeners, TraceListener[] saved)
            {
                if (Copy(listeners).SequenceEqual(saved)) return;
                listeners.Clear();
                listeners.AddRange(saved);
            }
        }

        public void Clear() { lock (_gate) _failures.Clear(); }

        public override void TraceEvent(TraceEventCache? eventCache, string source, TraceEventType eventType, int id, string? message)
        {
            if (eventType > TraceEventType.Warning || (eventType == TraceEventType.Warning && !IncludeWarnings)) return;
            Report(id, message ?? "");
        }

        public override void TraceEvent(TraceEventCache? eventCache, string source, TraceEventType eventType, int id, string? format, params object?[]? args)
        {
            if (eventType > TraceEventType.Warning || (eventType == TraceEventType.Warning && !IncludeWarnings)) return;
            Report(id, args is { Length: > 0 } && format != null ? Format(format, args) : format ?? "");
        }

        // A malformed format threw FormatException out of the traced call, which may be inside WPF's binding engine.
        private static string Format(string format, object?[] args)
        {
            try { return string.Format(CultureInfo.InvariantCulture, format, args); }
            catch (FormatException) { return format + " " + string.Join(" ", args); }
        }

        // TraceSource.TraceData went to the base class, which wrote a "Critical: " or "Warning: " header the line parser
        // never matched: critical failures were always dropped, and warnings even with IncludeWarnings.
        public override void TraceData(TraceEventCache? eventCache, string source, TraceEventType eventType, int id, object? data)
        {
            if (eventType > TraceEventType.Warning || (eventType == TraceEventType.Warning && !IncludeWarnings)) return;
            Report(id, data?.ToString() ?? "");
        }

        public override void TraceData(TraceEventCache? eventCache, string source, TraceEventType eventType, int id, params object?[]? data)
        {
            if (eventType > TraceEventType.Warning || (eventType == TraceEventType.Warning && !IncludeWarnings)) return;
            Report(id, data is null ? "" : string.Join(", ", data.Select(o => o?.ToString())));
        }

        // Plain Write/WriteLine (used by some WPF paths): accumulate until a line ends. Text that never ends a line is
        // capped (it grew for the life of the listener).
        public override void Write(string? message)
        {
            lock (_gate)
            {
                _pending.Append(message);
                if (_pending.Length > MaxPendingChars) _pending.Remove(0, _pending.Length - MaxPendingChars);
            }
        }

        public override void WriteLine(string? message)
        {
            string line;
            lock (_gate)
            {
                _pending.Append(message);
                line = _pending.ToString();
                _pending.Clear();
            }
            // Only "Error:" lines matched: a critical line was dropped, and a warning even with IncludeWarnings, as on the
            // TraceData path before.
            var m = Regex.Match(line, @"(Critical|Error|Warning): (\d+) : (.*)", RegexOptions.Singleline);
            if (!m.Success || (m.Groups[1].Value == "Warning" && !IncludeWarnings)) return;
            if (int.TryParse(m.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int id))   // int.Parse threw on a huge id
                Report(id, m.Groups[3].Value);
        }

        private void Report(int id, string message)
        {
            var failure = new BindingFailure(id, message.Trim());
            lock (_gate)
            {
                _failures.Enqueue(failure);
                Trim();
            }
            BindingFailed?.Invoke(this, failure);
            if (ThrowOnFailure) throw new BindingFailureException(failure);
        }

        protected override void Dispose(bool disposing)
        {
            // Claimed atomically: two threads disposing at once both passed a plain flag and took the listener count down twice.
            if (disposing && System.Threading.Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                lock (LevelGate)
                {
                    PresentationTraceSources.DataBindingSource.Listeners.Remove(this);
                    if (--_activeListeners == 0) PresentationTraceSources.DataBindingSource.Switch.Level = _originalLevel;
                }
            }
            base.Dispose(disposing);
        }
    }

    /// <summary>Thrown by a <see cref="BindingFailureListener"/> with <c>ThrowOnFailure</c> set.</summary>
    public sealed class BindingFailureException : InvalidOperationException
    {
        public BindingFailureException(BindingFailure failure)
            : base((failure ?? throw new ArgumentNullException(nameof(failure))).ToString()) => Failure = failure;

        public BindingFailure Failure { get; }
    }

    /// <summary>
    /// A pass-through converter for debugging bindings: logs every Convert/ConvertBack call (value, its type, target type,
    /// parameter) and can break into the debugger. Drop it into a misbehaving binding —
    /// <c>{Binding Total, Converter={StaticResource Debug}}</c> — to see exactly what flows through.
    /// </summary>
    public sealed class DebugConverter : IValueConverter
    {
        /// <summary>Where log lines go (default: <see cref="Debug.WriteLine(string)"/>).</summary>
        public Action<string> Log
        {
            get => _log;
            set => _log = value ?? throw new ArgumentNullException(nameof(value));   // null threw NullReferenceException from every conversion
        }

        private Action<string> _log = s => Debug.WriteLine(s);

        /// <summary>Break into an attached debugger on each conversion.</summary>
        public bool BreakOnConvert { get; set; }

        /// <summary>A label prefixed to log lines, to tell several debug converters apart.</summary>
        public string? Name { get; set; }

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            Trace("Convert", value, targetType, parameter);
            return value;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            Trace("ConvertBack", value, targetType, parameter);
            return value;
        }

        private void Trace(string direction, object? value, Type targetType, object? parameter)
        {
            string shown = value is null ? "null" : value == DependencyProperty.UnsetValue ? "UnsetValue" : $"'{value}' ({value.GetType().Name})";
            Log($"[{Name ?? "DebugConverter"}] {direction}: value={shown} target={targetType?.Name ?? "?"}" + (parameter != null ? $" parameter='{parameter}'" : ""));
            if (BreakOnConvert && Debugger.IsAttached) Debugger.Break();
        }
    }
}
