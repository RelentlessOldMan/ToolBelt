// ToolBelt.Wpf drop-in — Windows-only (net8.0-windows), self-contained (BCL only).
using System;
using System.Windows.Input;

namespace ToolBelt.Wpf
{
    /// <summary>
    /// An <see cref="ICommand"/> that forwards to delegates — the standard MVVM "relay"/"delegate" command.
    /// Raise <see cref="RaiseCanExecuteChanged"/> yourself when the result of the can-execute predicate
    /// changes (this deliberately avoids <c>CommandManager.RequerySuggested</c>, so it needs no dispatcher
    /// and is unit-testable).
    /// </summary>
    public sealed class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool>? _canExecute;

        public RelayCommand(Action execute, Func<bool>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

        public void Execute(object? parameter) => _execute();

        public event EventHandler? CanExecuteChanged;

        /// <summary>Raises <see cref="CanExecuteChanged"/> so bound controls re-query <see cref="CanExecute"/>.</summary>
        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// A strongly-typed <see cref="ICommand"/> whose delegates receive the command parameter as
    /// <typeparamref name="T"/>. A parameter that is not a <typeparamref name="T"/> is passed as
    /// <c>default</c> (so a null parameter maps to <c>default(T)</c>).
    /// </summary>
    public sealed class RelayCommand<T> : ICommand
    {
        private readonly Action<T?> _execute;
        private readonly Predicate<T?>? _canExecute;

        public RelayCommand(Action<T?> execute, Predicate<T?>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object? parameter) => _canExecute?.Invoke(Convert(parameter)) ?? true;

        public void Execute(object? parameter) => _execute(Convert(parameter));

        public event EventHandler? CanExecuteChanged;

        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);

        private static T? Convert(object? parameter) => parameter is T value ? value : default;
    }
}
