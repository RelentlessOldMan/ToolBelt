// ToolBelt.Wpf drop-in — Windows-only (net8.0-windows), self-contained (BCL only).
using System;
using System.Threading.Tasks;
using System.Windows.Input;

namespace ToolBelt.Wpf
{
    /// <summary>
    /// An asynchronous <see cref="ICommand"/>: its execute delegate returns a <see cref="Task"/>, and while
    /// that task is running the command reports <see cref="CanExecute"/> = false so the bound control is
    /// disabled and the operation cannot re-enter. Await <see cref="ExecuteAsync"/> directly in tests or
    /// callers; the <see cref="ICommand.Execute(object)"/> entry point is fire-and-forget (async void), the
    /// standard shape for binding. As with <see cref="RelayCommand"/>, raise
    /// <see cref="RaiseCanExecuteChanged"/> yourself — no dispatcher dependency.
    ///
    /// <para>Intended for use on a single UI thread: the re-entrancy guard is not synchronised, so concurrent
    /// invocations from different threads are not protected. An unhandled exception from the task is not
    /// swallowed — via the async-void <see cref="ICommand.Execute(object)"/> it surfaces on the captured
    /// synchronization context (typically crashing the app); prefer <see cref="ExecuteAsync"/> and await it
    /// when you need to observe failures.</para>
    /// </summary>
    public sealed class AsyncRelayCommand : ICommand
    {
        private readonly Func<Task> _execute;
        private readonly Func<bool>? _canExecute;
        private bool _isExecuting;

        public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        /// <summary>True while the command's task is in flight.</summary>
        public bool IsExecuting => _isExecuting;

        public bool CanExecute(object? parameter) => !_isExecuting && (_canExecute?.Invoke() ?? true);

        async void ICommand.Execute(object? parameter) => await ExecuteAsync().ConfigureAwait(false);

        /// <summary>Runs the command if it can execute; no-ops if it is already running or disabled.</summary>
        public async Task ExecuteAsync()
        {
            if (!CanExecute(null))
                return;
            _isExecuting = true;
            RaiseCanExecuteChanged();
            try
            {
                await _execute().ConfigureAwait(false);
            }
            finally
            {
                _isExecuting = false;
                RaiseCanExecuteChanged();
            }
        }

        public event EventHandler? CanExecuteChanged;

        /// <summary>Raises <see cref="CanExecuteChanged"/> so bound controls re-query <see cref="CanExecute"/>.</summary>
        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
