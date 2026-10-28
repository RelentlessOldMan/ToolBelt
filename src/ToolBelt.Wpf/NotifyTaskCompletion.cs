// ToolBelt.Wpf drop-in — Windows-only (net8.0-windows), self-contained (BCL only).
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

namespace ToolBelt.Wpf
{
    /// <summary>
    /// Wraps a <see cref="Task{TResult}"/> in bindable properties so a view can show an async operation's
    /// progress and result without code-behind: bind to <see cref="IsSuccessfullyCompleted"/>,
    /// <see cref="Result"/>, <see cref="IsFaulted"/>, <see cref="ErrorMessage"/>, etc. When the wrapped task
    /// finishes, <see cref="INotifyPropertyChanged.PropertyChanged"/> is raised for every computed property.
    /// Faults are surfaced through the properties (never rethrown), so a failed task does not crash the UI.
    /// The notifications are raised on the synchronization context that created the wrapper (the UI thread), so
    /// handlers may touch UI objects. Await <see cref="Completion"/> to know when they have been raised. A handler that
    /// throws does not stop the other notifications or handlers; its exception is rethrown afterwards on that context
    /// (an unhandled UI exception, like any handler's), or faults <see cref="Completion"/> when there is no context.
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
                await task;      // back on the creating context (ConfigureAwait(false) raised them on the thread pool)
            }
            catch
            {
                // Observed via IsFaulted/ErrorMessage below — never rethrown.
            }

            var handler = PropertyChanged;
            if (handler is null)
                return;
            // One throwing handler stopped the rest, leaving a bound UI showing "in progress" for good.
            List<Exception>? errors = null;
            Delegate[] handlers = handler.GetInvocationList();
            foreach (var name in ChangedProperties)
            {
                var args = new PropertyChangedEventArgs(name);
                foreach (PropertyChangedEventHandler h in handlers)
                {
                    try { h(this, args); }
                    catch (Exception ex) { (errors ??= new List<Exception>()).Add(ex); }
                }
            }
            if (errors is null)
                return;
            var error = ExceptionDispatchInfo.Capture(errors.Count == 1 ? errors[0] : new AggregateException(errors));
            SynchronizationContext? context = SynchronizationContext.Current;
            if (context is null) error.Throw();
            context.Post(_ => error.Throw(), null);   // Completion's fault went unobserved
        }

        private static readonly string[] ChangedProperties =
        {
            nameof(Status), nameof(IsCompleted), nameof(IsNotCompleted), nameof(IsSuccessfullyCompleted),
            nameof(IsCanceled), nameof(IsFaulted), nameof(Result), nameof(Exception),
            nameof(InnerException), nameof(ErrorMessage),
        };
    }
}
