// ToolBelt.Wpf drop-in — Windows-only (net8.0-windows), self-contained (WPF only).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
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
            Path = Match(message, @"BindingExpression:Path=([^;]*);");
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
            var m = Regex.Match(s, pattern);
            return m.Success ? m.Groups[1].Value : null;
        }
    }

    /// <summary>
    /// Makes WPF's silent data-binding failures visible. WPF only writes "System.Windows.Data Error: 40 :
    /// BindingExpression path error…" to the debugger output; a typo in a binding path otherwise just shows an empty
    /// field. Install a listener and every failure raises <see cref="BindingFailed"/> and is kept in <see cref="Failures"/>;
    /// with <see cref="ThrowOnFailure"/> the binding throws instead — the right setting in UI tests and debug builds so a
    /// broken binding fails loudly. Dispose to detach. Warnings (e.g. fallback values) are ignored unless
    /// <see cref="IncludeWarnings"/>.
    /// </summary>
    public sealed class BindingFailureListener : TraceListener
    {
        private readonly StringBuilder _pending = new StringBuilder();
        private readonly List<BindingFailure> _failures = new List<BindingFailure>();
        private readonly object _gate = new object();
        private readonly SourceLevels _previousLevel;
        private bool _disposed;

        public BindingFailureListener()
        {
            _previousLevel = PresentationTraceSources.DataBindingSource.Switch.Level;
            PresentationTraceSources.Refresh();
            var source = PresentationTraceSources.DataBindingSource;
            if (source.Switch.Level < SourceLevels.Warning || source.Switch.Level == SourceLevels.Off) source.Switch.Level = SourceLevels.Warning;
            source.Listeners.Add(this);
        }

        /// <summary>Raised for each failure, on the thread that evaluated the binding (usually the UI thread).</summary>
        public event EventHandler<BindingFailure>? BindingFailed;

        /// <summary>Throw <see cref="BindingFailureException"/> from the failing binding instead of only recording it.</summary>
        public bool ThrowOnFailure { get; set; }

        public bool IncludeWarnings { get; set; }

        public IReadOnlyList<BindingFailure> Failures { get { lock (_gate) return _failures.ToArray(); } }

        public void Clear() { lock (_gate) _failures.Clear(); }

        public override void TraceEvent(TraceEventCache? eventCache, string source, TraceEventType eventType, int id, string? message)
        {
            if (eventType > TraceEventType.Warning || (eventType == TraceEventType.Warning && !IncludeWarnings)) return;
            Report(id, message ?? "");
        }

        public override void TraceEvent(TraceEventCache? eventCache, string source, TraceEventType eventType, int id, string? format, params object?[]? args)
        {
            if (eventType > TraceEventType.Warning || (eventType == TraceEventType.Warning && !IncludeWarnings)) return;
            Report(id, args is { Length: > 0 } && format != null ? string.Format(CultureInfo.InvariantCulture, format, args) : format ?? "");
        }

        // Plain Write/WriteLine (used by some WPF paths): accumulate until a line ends.
        public override void Write(string? message) { lock (_gate) _pending.Append(message); }

        public override void WriteLine(string? message)
        {
            string line;
            lock (_gate)
            {
                _pending.Append(message);
                line = _pending.ToString();
                _pending.Clear();
            }
            var m = Regex.Match(line, @"Error: (\d+) : (.*)", RegexOptions.Singleline);
            if (m.Success) Report(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), m.Groups[2].Value);
        }

        private void Report(int id, string message)
        {
            var failure = new BindingFailure(id, message.Trim());
            lock (_gate) _failures.Add(failure);
            BindingFailed?.Invoke(this, failure);
            if (ThrowOnFailure) throw new BindingFailureException(failure);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_disposed)
            {
                _disposed = true;
                PresentationTraceSources.DataBindingSource.Listeners.Remove(this);
                PresentationTraceSources.DataBindingSource.Switch.Level = _previousLevel;
            }
            base.Dispose(disposing);
        }
    }

    /// <summary>Thrown by a <see cref="BindingFailureListener"/> with <c>ThrowOnFailure</c> set.</summary>
    public sealed class BindingFailureException : InvalidOperationException
    {
        public BindingFailureException(BindingFailure failure) : base(failure?.ToString()) => Failure = failure!;

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
        public Action<string> Log { get; set; } = s => Debug.WriteLine(s);

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
