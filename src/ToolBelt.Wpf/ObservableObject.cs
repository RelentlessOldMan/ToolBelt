// ToolBelt.Wpf drop-in — Windows-only (net8.0-windows), self-contained (BCL only).
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ToolBelt.Wpf
{
    /// <summary>
    /// A base class for view models (or any bindable object) implementing <see cref="INotifyPropertyChanged"/>.
    /// Use <see cref="SetProperty{T}"/> in setters: it assigns the backing field, raises
    /// <see cref="PropertyChanged"/>, and returns whether the value actually changed — no change notification
    /// is raised when the value is equal. The property name is captured automatically from the caller.
    /// </summary>
    public abstract class ObservableObject : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>Raises <see cref="PropertyChanged"/> for <paramref name="propertyName"/> (the caller member by default).</summary>
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        /// <summary>
        /// Assigns <paramref name="value"/> to <paramref name="field"/> if different, raising
        /// <see cref="PropertyChanged"/> and returning true; returns false (and does nothing) when equal.
        /// </summary>
        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }
}
