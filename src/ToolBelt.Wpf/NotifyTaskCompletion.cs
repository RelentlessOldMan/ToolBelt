// ToolBelt.Wpf drop-in — Windows-only (net8.0-windows), self-contained (BCL only).
using System;
using System.ComponentModel;
using System.Threading.Tasks;

namespace ToolBelt.Wpf
{
    /// <summary>
    /// Wraps a <see cref="Task{TResult}"/> in bindable properties so a view can show an async operation's
    /// progress and result without code-behind: bind to <see cref="IsSuccessfullyCompleted"/>,
    /// <see cref="Result"/>, <see cref="IsFaulted"/>, <see cref="ErrorMessage"/>, etc. When the wrapped task
    /// finishes, <see cref="INotifyPropertyChanged.PropertyChanged"/> is raised for every computed property.
    /// Faults are surfaced through the properties (never rethrown), so a failed task does not crash the UI.
    /// Await <see cref="Completion"/> to know when the notifications have been raised.
    /// </summary>
    public sealed class NotifyTaskCompletion<TResult> : INotifyPropertyChanged
    {
        public NotifyTaskCompletion(Task<TResult> task)
        {
            Task = task ?? throw new ArgumentNullException(nameof(task));
            Completion = task.IsCompleted ? System.Threading.Tasks.Task.CompletedTask : WatchAsync(task);
        }

        /// <summary>The wrapped task.</summary>
        public Task<TResult> Task { get; }

        /// <summary>Completes after the wrapped task finishes and the property-change notifications have fired.</summary>
        public Task Completion { get; }

        public TaskStatus Status => Task.Status;
        public bool IsCompleted => Task.IsCompleted;
        public bool IsNotCompleted => !Task.IsCompleted;
        public bool IsSuccessfullyCompleted => Task.Status == TaskStatus.RanToCompletion;
        public bool IsCanceled => Task.IsCanceled;
        public bool IsFaulted => Task.IsFaulted;

        /// <summary>The task's result once it has completed successfully; otherwise <c>default</c>.</summary>
        public TResult? Result => IsSuccessfullyCompleted ? Task.Result : default;

        public AggregateException? Exception => Task.Exception;
        public Exception? InnerException => Exception?.InnerException;
        public string? ErrorMessage => InnerException?.Message;

        public event PropertyChangedEventHandler? PropertyChanged;

        private async Task WatchAsync(Task<TResult> task)
        {
            try
            {
                await task.ConfigureAwait(false);
            }
            catch
            {
                // Observed via IsFaulted/ErrorMessage below — never rethrown.
            }

            var handler = PropertyChanged;
            if (handler is null)
                return;
            foreach (var name in ChangedProperties)
                handler(this, new PropertyChangedEventArgs(name));
        }

        private static readonly string[] ChangedProperties =
        {
            nameof(Status), nameof(IsCompleted), nameof(IsNotCompleted), nameof(IsSuccessfullyCompleted),
            nameof(IsCanceled), nameof(IsFaulted), nameof(Result), nameof(Exception),
            nameof(InnerException), nameof(ErrorMessage),
        };
    }
}
