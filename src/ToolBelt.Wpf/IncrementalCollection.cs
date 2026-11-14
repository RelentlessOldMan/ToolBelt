// ToolBelt.Wpf drop-in — Windows-only (net8.0-windows), self-contained (BCL only).
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

namespace ToolBelt.Wpf
{
    /// <summary>
    /// An <see cref="ObservableCollection{T}"/> that loads a very large result set a page at a time: call
    /// <see cref="LoadMoreAsync"/> when the user nears the end of the list (from a scroll handler, a "load more"
    /// button, or a command) and the next page is fetched by the page loader and appended. A page shorter than
    /// <see cref="PageSize"/> marks the end (<see cref="HasMoreItems"/> becomes false).
    /// <para>
    /// Concurrent calls while a page is loading share that one load instead of fetching twice; each caller's token
    /// cancels only that caller's wait, and the load itself is cancelled once every waiting caller has cancelled.
    /// <see cref="ResetAsync"/>
    /// clears the list and starts over; a page still in flight from before the reset is discarded when it arrives, so
    /// stale results can never be appended to the new list. <see cref="IsLoading"/>, <see cref="HasMoreItems"/> and
    /// <see cref="LastError"/> raise <c>PropertyChanged</c> for binding (a busy indicator, a retry prompt). A failed
    /// page leaves <see cref="HasMoreItems"/> true so the load can be retried, records the exception in
    /// <see cref="LastError"/>, and is rethrown to the caller; cancellation is rethrown without being recorded (an
    /// <see cref="OperationCanceledException"/> the loader raised on its own, such as an HTTP timeout, while its token was
    /// not cancelled is a failure like any other: recorded, and the task faults).
    /// Intended for use on the UI thread: items are added after awaiting the loader, on the captured context.
    /// </para>
    /// </summary>
    public sealed class IncrementalCollection<T> : ObservableCollection<T>
    {
        private readonly Func<int, int, CancellationToken, Task<IReadOnlyList<T>>> _loadPage;
        private LoadState? _load;
        private int _generation;
        private bool _hasMoreItems = true;
        private bool _isLoading;
        private Exception? _lastError;

        /// <param name="loadPage">Fetches up to <c>take</c> items starting at index <c>skip</c>.</param>
        /// <param name="pageSize">Items requested per page.</param>
        public IncrementalCollection(Func<int, int, CancellationToken, Task<IReadOnlyList<T>>> loadPage, int pageSize = 50)
        {
            _loadPage = loadPage ?? throw new ArgumentNullException(nameof(loadPage));
            if (pageSize < 1) throw new ArgumentOutOfRangeException(nameof(pageSize), pageSize, "Page size must be positive.");
            PageSize = pageSize;
        }

        public int PageSize { get; }

        /// <summary>False once a short page has been received.</summary>
        public bool HasMoreItems
        {
            get => _hasMoreItems;
            private set => Set(ref _hasMoreItems, value, nameof(HasMoreItems));
        }

        /// <summary>True while a page is being fetched.</summary>
        public bool IsLoading
        {
            get => _isLoading;
            private set => Set(ref _isLoading, value, nameof(IsLoading));
        }

        /// <summary>The exception from the most recent failed page load, cleared when a load succeeds.</summary>
        public Exception? LastError
        {
            get => _lastError;
            private set => Set(ref _lastError, value, nameof(LastError));
        }

        /// <summary>
        /// Loads and appends the next page, returning how many items were added (0 when there is nothing more). If a
        /// page is already loading, waits for that load instead. A token already cancelled starts nothing.
        /// </summary>
        public Task<int> LoadMoreAsync(CancellationToken cancellationToken = default)
        {
            // A cancelled token started a fetch anyway, and a loader that ignored its token appended the page.
            if (cancellationToken.IsCancellationRequested) return Task.FromCanceled<int>(cancellationToken);
            LoadState? load = _load;
            if (load != null && !load.Task.IsCompleted)
                return load.TryJoin() ? Follow(load, cancellationToken) : LoadAfterAsync(load.Task, cancellationToken);
            if (!HasMoreItems) return Task.FromResult(0);
            // Publish the in-flight load before the loader runs: a loader that completes synchronously adds its items (and
            // raises CollectionChanged) before LoadCoreAsync returns, and a handler calling LoadMoreAsync from there must get
            // this load, not start a second one over the same offset.
            var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            load = new LoadState(tcs.Task);
            _load = load;
            load.TryJoin();
            Task<int> mine = Follow(load, cancellationToken);
            _ = Complete(LoadCoreAsync(_generation, load), load, tcs);
            return mine;
        }

        // One load, shared by every caller waiting on it. A caller's token used to cancel the load under all of them.
        // Joining and the last waiter leaving are decided under one lock: a caller that joined between the last waiter's
        // count reaching zero and its cancel was cancelled without asking.
        private sealed class LoadState
        {
            public LoadState(Task<int> task) => Task = task;
            public readonly Task<int> Task;
            public readonly CancellationTokenSource Cts = new CancellationTokenSource();
            private readonly object _gate = new object();
            private int _waiters;
            private bool _cancelling;

            /// <summary>Adds a waiter; false once the load is being cancelled (wait for it to end, then load afresh).</summary>
            public bool TryJoin()
            {
                lock (_gate)
                {
                    if (_cancelling) return false;
                    _waiters++;
                    return true;
                }
            }

            /// <summary>Removes a waiter; true when it was the last, which marks the load cancelling (the caller cancels it).</summary>
            public bool Leave()
            {
                lock (_gate)
                {
                    if (--_waiters != 0 || _cancelling) return false;
                    _cancelling = true;
                    return true;
                }
            }

            public void Cancel()
            {
                lock (_gate) _cancelling = true;
                Cts.Cancel();
            }
        }

        // Follows a load the caller has joined.
        private static Task<int> Follow(LoadState load, CancellationToken cancellationToken)
        {
            if (!cancellationToken.CanBeCanceled) return load.Task;   // this waiter never leaves, so the load is never cancelled
            var mine = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            CancellationTokenRegistration registration = cancellationToken.Register(() =>
            {
                // The last waiter cancels the load and keeps following it (it ends cancelled, unless the loader ignores
                // the token); the others just stop waiting.
                if (load.Leave()) load.Cts.Cancel();
                else mine.TrySetCanceled(cancellationToken);
            });
            load.Task.ContinueWith(t =>
            {
                registration.Dispose();
                if (t.IsFaulted) mine.TrySetException(t.Exception!.InnerExceptions);
                else if (t.IsCanceled)
                {
                    // The last waiter cancelled the load through its own token: it sees that token, not the load's.
                    if (cancellationToken.IsCancellationRequested) { mine.TrySetCanceled(cancellationToken); return; }
                    try { t.GetAwaiter().GetResult(); }
                    catch (OperationCanceledException oce) { mine.TrySetCanceled(oce.CancellationToken); }
                }
                else mine.TrySetResult(t.Result);
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return mine.Task;
        }

        // A load every waiter has abandoned is winding down: a new caller waits for it, then loads afresh.
        private async Task<int> LoadAfterAsync(Task<int> cancelling, CancellationToken cancellationToken)
        {
            try { await cancelling.ConfigureAwait(true); }
            catch (Exception) { }   // its own waiters see its outcome
            cancellationToken.ThrowIfCancellationRequested();
            return await LoadMoreAsync(cancellationToken).ConfigureAwait(true);
        }

        // Only a cancellation of the load's own token ends it cancelled: a loader's own OperationCanceledException (an HTTP
        // timeout) was recorded in LastError yet ended the task Canceled, not Faulted.
        private static async Task Complete(Task<int> work, LoadState load, TaskCompletionSource<int> tcs)
        {
            try { tcs.TrySetResult(await work.ConfigureAwait(false)); }
            catch (OperationCanceledException oce) when (load.Cts.IsCancellationRequested) { tcs.TrySetCanceled(oce.CancellationToken); }
            catch (Exception ex) { tcs.TrySetException(ex); }
        }

        /// <summary>
        /// Discards everything (including any page in flight) and loads the first page again. If cancelling the old load
        /// throws (a loader's cancellation callback threw), or a CollectionChanged or PropertyChanged handler does, every
        /// step of the reset still happens and the exception propagates (several together as an
        /// <see cref="AggregateException"/>); no new page is loaded then.
        /// </summary>
        public Task<int> ResetAsync(CancellationToken cancellationToken = default)
        {
            _generation = unchecked(_generation + 1);   // checked builds threw after int.MaxValue resets
            LoadState? old = _load;
            _load = null;
            // Each step runs even if an earlier one throws: a CollectionChanged handler that threw from ClearItems left
            // IsLoading true for good, HasMoreItems stale and the old load running.
            var failures = new List<Exception>();
            Step(failures, ClearItems);
            Step(failures, () => HasMoreItems = true);
            Step(failures, () => IsLoading = false);
            Step(failures, () => LastError = null);
            Step(failures, () => old?.Cancel());   // last: a throwing token callback left the list uncleared and IsLoading true for good
            if (failures.Count == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
            if (failures.Count > 1) throw new AggregateException(failures);
            return LoadMoreAsync(cancellationToken);
        }

        private static void Step(List<Exception> failures, Action step)
        {
            try { step(); }
            catch (Exception ex) { failures.Add(ex); }
        }

        private async Task<int> LoadCoreAsync(int generation, LoadState load)
        {
            CancellationTokenSource cts = load.Cts;
            Exception? failure = null;
            try
            {
                // Inside the try: a PropertyChanged handler that threw here left IsLoading true for good.
                IsLoading = true;
                IReadOnlyList<T> page = await _loadPage(Count, PageSize, cts.Token).ConfigureAwait(true)
                    ?? throw new InvalidOperationException("The page loader returned null.");
                if (generation != _generation) return 0; // reset while we were loading: drop the stale page

                foreach (T item in page)
                {
                    Add(item);
                    if (generation != _generation) return 0; // a CollectionChanged handler reset the list: drop the rest
                }
                LastError = null;
                if (page.Count < PageSize) HasMoreItems = false;
                return page.Count;
            }
            catch (OperationCanceledException oce) when (generation != _generation || cts.IsCancellationRequested)
            {
                if (generation != _generation) return 0; // superseded by a reset
                failure = oce;
                throw;
            }
            catch (Exception ex) when (generation == _generation)
            {
                failure = ex;
                LastError = ex;
                throw;
            }
            catch (Exception) when (generation != _generation)
            {
                return 0;                                            // a page nobody wants any more failed: drop it like its data
            }
            finally
            {
                // The token source is not disposed: late cancels must not throw. A handler that threw here replaced the load's
                // own exception.
                if (generation == _generation)
                {
                    try { IsLoading = false; }
                    catch (Exception handlerError) when (failure != null) { throw new AggregateException(failure, handlerError); }
                }
            }
        }

        private void Set<TValue>(ref TValue field, TValue value, string name)
        {
            if (EqualityComparer<TValue>.Default.Equals(field, value)) return;
            field = value;
            OnPropertyChanged(new PropertyChangedEventArgs(name));
        }
    }
}
