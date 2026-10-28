// ToolBelt.Wpf drop-in — Windows-only (net8.0-windows), self-contained (BCL only).
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
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
    /// <see cref="LastError"/>, and is rethrown to the caller; cancellation is rethrown without being recorded.
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
        /// page is already loading, waits for that load instead.
        /// </summary>
        public Task<int> LoadMoreAsync(CancellationToken cancellationToken = default)
        {
            LoadState? load = _load;
            if (load != null && !load.Task.IsCompleted)
                return load.Cancelling ? LoadAfterAsync(load.Task, cancellationToken) : Join(load, cancellationToken);
            if (!HasMoreItems) return Task.FromResult(0);
            // Publish the in-flight load before the loader runs: a loader that completes synchronously adds its items (and
            // raises CollectionChanged) before LoadCoreAsync returns, and a handler calling LoadMoreAsync from there must get
            // this load, not start a second one over the same offset.
            var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            load = new LoadState(tcs.Task);
            _load = load;
            Task<int> mine = Join(load, cancellationToken);
            _ = Complete(LoadCoreAsync(_generation, load), tcs);
            return mine;
        }

        // One load, shared by every caller waiting on it. A caller's token used to cancel the load under all of them.
        private sealed class LoadState
        {
            public LoadState(Task<int> task) => Task = task;
            public readonly Task<int> Task;
            public readonly CancellationTokenSource Cts = new CancellationTokenSource();
            public int Waiters;
            private int _cancelling;
            public bool Cancelling => Volatile.Read(ref _cancelling) != 0;
            public void Cancel() { Volatile.Write(ref _cancelling, 1); Cts.Cancel(); }
        }

        private static Task<int> Join(LoadState load, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref load.Waiters);
            if (!cancellationToken.CanBeCanceled) return load.Task;   // this waiter never leaves, so the load is never cancelled
            var mine = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            CancellationTokenRegistration registration = cancellationToken.Register(() =>
            {
                // The last waiter cancels the load and keeps following it (it ends cancelled, unless the loader ignores
                // the token); the others just stop waiting.
                if (Interlocked.Decrement(ref load.Waiters) == 0) load.Cancel();
                else mine.TrySetCanceled(cancellationToken);
            });
            load.Task.ContinueWith(t =>
            {
                registration.Dispose();
                if (t.IsFaulted) mine.TrySetException(t.Exception!.InnerExceptions);
                else if (t.IsCanceled)
                {
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

        private static async Task Complete(Task<int> load, TaskCompletionSource<int> tcs)
        {
            try { tcs.TrySetResult(await load.ConfigureAwait(false)); }
            catch (OperationCanceledException oce) { tcs.TrySetCanceled(oce.CancellationToken); }
            catch (Exception ex) { tcs.TrySetException(ex); }
        }

        /// <summary>
        /// Discards everything (including any page in flight) and loads the first page again. If cancelling the old load
        /// throws (a loader's cancellation callback threw), the list is still reset and the exception propagates.
        /// </summary>
        public Task<int> ResetAsync(CancellationToken cancellationToken = default)
        {
            _generation++;
            LoadState? old = _load;
            _load = null;
            ClearItems();
            HasMoreItems = true;
            IsLoading = false;
            LastError = null;
            old?.Cancel();   // last: a throwing token callback left the list uncleared and IsLoading true for good
            return LoadMoreAsync(cancellationToken);
        }

        private async Task<int> LoadCoreAsync(int generation, LoadState load)
        {
            CancellationTokenSource cts = load.Cts;
            IsLoading = true;
            try
            {
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
            catch (OperationCanceledException) when (generation != _generation || cts.IsCancellationRequested)
            {
                if (generation != _generation) return 0; // superseded by a reset
                throw;
            }
            catch (Exception ex) when (generation == _generation)
            {
                LastError = ex;
                throw;
            }
            catch (Exception) when (generation != _generation)
            {
                return 0;                                            // a page nobody wants any more failed: drop it like its data
            }
            finally
            {
                if (generation == _generation) IsLoading = false;   // the token source is not disposed: late cancels must not throw
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
