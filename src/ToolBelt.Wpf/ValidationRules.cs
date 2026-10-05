// ToolBelt.Wpf drop-in — Windows-only (net8.0-windows); RuleValidatedObject also needs ValidationObservableObject.cs.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows.Controls;

namespace ToolBelt.Wpf
{
    /// <summary>
    /// Declarative per-property validation rules for a view-model:
    /// <code>
    /// Rules.For(nameof(Name), () => Name).Required().MaxLength(40);
    /// Rules.For(nameof(Port), () => Port).Range(1, 65535);
    /// Rules.For(nameof(End), () => End).Must(e => e > Start, "End must be after Start").DependsOn(nameof(Start));
    /// </code>
    /// <see cref="Validate"/> returns a property's error messages (rules run in declaration order; a failed
    /// <c>Required</c> stops that property's later rules). <see cref="Dependents"/> lists properties to re-check when one
    /// changes. Used by <see cref="RuleValidatedObject"/>, or standalone.
    /// </summary>
    public sealed class ValidationRuleSet
    {
        private readonly Dictionary<string, List<Func<string?>>> _rules = new Dictionary<string, List<Func<string?>>>(StringComparer.Ordinal);
        private readonly Dictionary<string, HashSet<string>> _dependents = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        private readonly HashSet<string> _stopping = new HashSet<string>();

        /// <summary>Starts (or continues) the rules for <paramref name="propertyName"/>, reading its value through <paramref name="getter"/>.</summary>
        public RuleBuilder<T> For<T>(string propertyName, Func<T> getter)
        {
            if (string.IsNullOrEmpty(propertyName)) throw new ArgumentException("Property name is required.", nameof(propertyName));
            if (getter is null) throw new ArgumentNullException(nameof(getter));
            if (!_rules.ContainsKey(propertyName)) _rules[propertyName] = new List<Func<string?>>();
            return new RuleBuilder<T>(this, propertyName, getter);
        }

        /// <summary>The properties that have rules.</summary>
        public IEnumerable<string> Properties => _rules.Keys;

        /// <summary>Error messages for one property (empty when valid or when it has no rules).</summary>
        public IReadOnlyList<string> Validate(string propertyName)
        {
            if (!_rules.TryGetValue(propertyName ?? throw new ArgumentNullException(nameof(propertyName)), out var rules)) return Array.Empty<string>();
            var errors = new List<string>();
            for (int i = 0; i < rules.Count; i++)
            {
                string? error = rules[i]();
                if (error == null) continue;
                errors.Add(error);
                if (_stopping.Contains(propertyName + "#" + i.ToString(CultureInfo.InvariantCulture))) break;
            }
            return errors;
        }

        /// <summary>Every property's errors (only properties that have some).</summary>
        public IReadOnlyDictionary<string, IReadOnlyList<string>> ValidateAll()
        {
            var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
            foreach (string p in _rules.Keys)
            {
                var e = Validate(p);
                if (e.Count > 0) result[p] = e;
            }
            return result;
        }

        /// <summary>Properties whose rules read <paramref name="propertyName"/> (declared with <c>DependsOn</c>).</summary>
        public IEnumerable<string> Dependents(string propertyName)
            => _dependents.TryGetValue(propertyName, out var set) ? set : Enumerable.Empty<string>();

        internal void Add(string property, Func<string?> rule, bool stopOnFailure)
        {
            var list = _rules[property];
            if (stopOnFailure) _stopping.Add(property + "#" + list.Count.ToString(CultureInfo.InvariantCulture));
            list.Add(rule);
        }

        internal void AddDependency(string property, string dependsOn)
        {
            if (!_dependents.TryGetValue(dependsOn, out var set)) _dependents[dependsOn] = set = new HashSet<string>(StringComparer.Ordinal);
            set.Add(property);
        }
    }

    /// <summary>Fluent rule builder returned by <see cref="ValidationRuleSet.For{T}"/>.</summary>
    public sealed class RuleBuilder<T>
    {
        private readonly ValidationRuleSet _set;
        private readonly string _property;
        private readonly Func<T> _get;

        internal RuleBuilder(ValidationRuleSet set, string property, Func<T> get)
        {
            _set = set;
            _property = property;
            _get = get;
        }

        /// <summary>Not null, and not empty/whitespace for strings. Later rules for the property are skipped when this fails.</summary>
        public RuleBuilder<T> Required(string? message = null)
        {
            _set.Add(_property, () =>
            {
                object? v = _get();
                bool missing = v is null || (v is string s && s.Trim().Length == 0);
                return missing ? message ?? $"{_property} is required." : null;
            }, stopOnFailure: true);
            return this;
        }

        /// <summary>A custom check: <paramref name="predicate"/> returns true when the value is valid.</summary>
        public RuleBuilder<T> Must(Func<T, bool> predicate, string message)
        {
            if (predicate is null) throw new ArgumentNullException(nameof(predicate));
            if (message is null) throw new ArgumentNullException(nameof(message));
            _set.Add(_property, () => predicate(_get()) ? null : message, stopOnFailure: false);
            return this;
        }

        /// <summary>Inclusive range for comparable values (null passes — combine with <see cref="Required"/>).</summary>
        public RuleBuilder<T> Range(T min, T max, string? message = null)
        {
            var cmp = Comparer<T>.Default;
            if (cmp.Compare(min, max) > 0) throw new ArgumentException("min must not exceed max.");
            _set.Add(_property, () =>
            {
                T v = _get();
                if (v is null) return null;
                return cmp.Compare(v, min) < 0 || cmp.Compare(v, max) > 0
                    ? message ?? string.Format(CultureInfo.CurrentCulture, "{0} must be between {1} and {2}.", _property, min, max) : null;
            }, stopOnFailure: false);
            return this;
        }

        /// <summary>Minimum string length (null passes).</summary>
        public RuleBuilder<T> MinLength(int length, string? message = null)
            => Must(v => v is not string s || s.Length >= length, message ?? $"{_property} must be at least {length} characters.");

        /// <summary>Maximum string length (null passes).</summary>
        public RuleBuilder<T> MaxLength(int length, string? message = null)
            => Must(v => v is not string s || s.Length <= length, message ?? $"{_property} must be at most {length} characters.");

        /// <summary>The string matches <paramref name="pattern"/> (whole value; null/empty passes).</summary>
        public RuleBuilder<T> Matches(string pattern, string? message = null)
        {
            var regex = new Regex("^(?:" + pattern + ")$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            return Must(v => v is not string s || s.Length == 0 || regex.IsMatch(s), message ?? $"{_property} has an invalid format.");
        }

        /// <summary>Re-validate this property whenever <paramref name="otherProperties"/> change (cross-field rules).</summary>
        public RuleBuilder<T> DependsOn(params string[] otherProperties)
        {
            foreach (string other in otherProperties ?? throw new ArgumentNullException(nameof(otherProperties)))
                _set.AddDependency(_property, other);
            return this;
        }
    }

    /// <summary>
    /// A <see cref="ValidationObservableObject"/> whose errors come from a <see cref="ValidationRuleSet"/>: declare rules
    /// in the constructor through <see cref="Rules"/>, write setters with <see cref="SetAndValidate"/>, and the property
    /// (plus any properties that depend on it) is re-validated on every change. <see cref="ValidateAll"/> checks
    /// everything — call it before saving so untouched fields show their errors too.
    /// </summary>
    public abstract class RuleValidatedObject : ValidationObservableObject
    {
        protected ValidationRuleSet Rules { get; } = new ValidationRuleSet();

        /// <summary>Sets the field, raises PropertyChanged, and re-validates this property and its dependents.</summary>
        protected bool SetAndValidate<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (propertyName is null) throw new ArgumentNullException(nameof(propertyName));
            bool changed = SetProperty(ref field, value, propertyName);
            ValidateProperty(propertyName);
            foreach (string dependent in Rules.Dependents(propertyName).ToList()) ValidateProperty(dependent);
            return changed;
        }

        /// <summary>Validates every property with rules; returns true when there are no errors.</summary>
        public bool ValidateAll()
        {
            foreach (string p in Rules.Properties.ToList()) ValidateProperty(p);
            return !HasErrors;
        }

        protected void ValidateProperty(string propertyName) => SetErrors(propertyName, Rules.Validate(propertyName));
    }

    // ---------- XAML ValidationRules (for bindings without a validating view-model) ----------

    /// <summary>Binding rule: the text must not be empty or whitespace. <c>&lt;tb:RequiredValidationRule Message="Name is required"/&gt;</c></summary>
    public sealed class RequiredValidationRule : ValidationRule
    {
        public string Message { get; set; } = "A value is required.";

        public override ValidationResult Validate(object? value, CultureInfo cultureInfo)
            => value is null || (value is string s && s.Trim().Length == 0) ? new ValidationResult(false, Message) : ValidationResult.ValidResult;
    }

    /// <summary>Binding rule: the text parses as a number (in the binding culture) within [Minimum, Maximum].</summary>
    public sealed class NumericRangeValidationRule : ValidationRule
    {
        public double Minimum { get; set; } = double.MinValue;
        public double Maximum { get; set; } = double.MaxValue;
        public bool AllowEmpty { get; set; }
        public string? Message { get; set; }

        public override ValidationResult Validate(object? value, CultureInfo cultureInfo)
        {
            string text = Convert.ToString(value, cultureInfo)?.Trim() ?? "";
            if (text.Length == 0) return AllowEmpty ? ValidationResult.ValidResult : new ValidationResult(false, Message ?? "A number is required.");
            if (!double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, cultureInfo, out double v) || double.IsNaN(v))
                return new ValidationResult(false, Message ?? "Not a number.");
            if (v < Minimum || v > Maximum)
                return new ValidationResult(false, Message ?? string.Format(cultureInfo, "Must be between {0} and {1}.", Minimum, Maximum));
            return ValidationResult.ValidResult;
        }
    }

    /// <summary>Binding rule: the text matches a regular expression (whole value).</summary>
    public sealed class RegexValidationRule : ValidationRule
    {
        private Regex? _regex;
        private string _pattern = ".*";

        public string Pattern
        {
            get => _pattern;
            set { _pattern = value ?? throw new ArgumentNullException(nameof(value)); _regex = null; }
        }

        public string Message { get; set; } = "Invalid format.";
        public bool AllowEmpty { get; set; } = true;

        public override ValidationResult Validate(object? value, CultureInfo cultureInfo)
        {
            string text = Convert.ToString(value, cultureInfo) ?? "";
            if (text.Length == 0 && AllowEmpty) return ValidationResult.ValidResult;
            _regex ??= new Regex("^(?:" + _pattern + ")$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            return _regex.IsMatch(text) ? ValidationResult.ValidResult : new ValidationResult(false, Message);
        }
    }
}
