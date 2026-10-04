// ToolBelt.Wpf drop-in — Windows-only (net8.0-windows), self-contained (BCL only).
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ToolBelt.Wpf
{
    /// <summary>
    /// A bindable base class with per-property validation, implementing both
    /// <see cref="INotifyPropertyChanged"/> and <see cref="INotifyDataErrorInfo"/> (the standard WPF
    /// validation contract). Call <see cref="SetErrors"/> / <see cref="AddError"/> / <see cref="ClearErrors"/>
    /// from your validation logic; the control binding reflects the errors via <see cref="GetErrors"/> and
    /// <see cref="HasErrors"/>. Self-contained: it re-implements the property-change plumbing rather than
    /// depending on <see cref="ObservableObject"/>, so this file stands alone.
    /// </summary>
    public abstract class ValidationObservableObject : INotifyPropertyChanged, INotifyDataErrorInfo
    {
        private readonly Dictionary<string, List<string>> _errors = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        public event PropertyChangedEventHandler? PropertyChanged;
        public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        /// <summary>True if any property currently has validation errors.</summary>
        public bool HasErrors => _errors.Count > 0;

        /// <summary>
        /// Returns the errors for <paramref name="propertyName"/>, or all errors when it is null/empty
        /// (per the <see cref="INotifyDataErrorInfo"/> contract).
        /// </summary>
        public IEnumerable GetErrors(string? propertyName)
        {
            if (string.IsNullOrEmpty(propertyName))
            {
                var all = new List<string>();
                foreach (var list in _errors.Values)
                    all.AddRange(list);
                return all;
            }
            return _errors.TryGetValue(propertyName!, out var errors) ? errors.ToArray() : Array.Empty<string>();
        }

        /// <summary>Replaces the error list for a property (empty clears it).</summary>
        protected void SetErrors(string propertyName, IEnumerable<string> errors)
        {
            if (propertyName is null) throw new ArgumentNullException(nameof(propertyName));
            if (errors is null) throw new ArgumentNullException(nameof(errors));
            var list = new List<string>(errors);
            if (list.Count == 0)
                _errors.Remove(propertyName);
            else
                _errors[propertyName] = list;
            RaiseErrorsChanged(propertyName);
        }

        /// <summary>Adds a single error for a property.</summary>
        protected void AddError(string propertyName, string error)
        {
            if (propertyName is null) throw new ArgumentNullException(nameof(propertyName));
            if (error is null) throw new ArgumentNullException(nameof(error));
            if (!_errors.TryGetValue(propertyName, out var list))
            {
                list = new List<string>();
                _errors[propertyName] = list;
            }
            list.Add(error);
            RaiseErrorsChanged(propertyName);
        }

        /// <summary>Clears the errors for one property. Returns true if any were removed.</summary>
        protected bool ClearErrors(string propertyName)
        {
            if (propertyName is null) throw new ArgumentNullException(nameof(propertyName));
            if (!_errors.Remove(propertyName))
                return false;
            RaiseErrorsChanged(propertyName);
            return true;
        }

        /// <summary>Clears every property's errors.</summary>
        protected void ClearAllErrors()
        {
            if (_errors.Count == 0)
                return;
            var names = new List<string>(_errors.Keys);
            _errors.Clear();
            foreach (var name in names)
                RaiseErrorsChanged(name);
        }

        private void RaiseErrorsChanged(string propertyName)
        {
            ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(propertyName));
            OnPropertyChanged(nameof(HasErrors));
        }
    }
}
