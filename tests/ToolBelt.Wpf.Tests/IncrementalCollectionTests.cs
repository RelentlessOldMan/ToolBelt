using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ToolBelt.Tests.Framework;
using ToolBelt.Wpf;

namespace ToolBelt.Wpf.Tests
{
    public sealed class IncrementalCollectionTests
    {
        // A data source of `total` integers that records each page request.
        private static Func<int, int, CancellationToken, Task<IReadOnlyList<int>>> Source(int total, List<(int Skip, int Take)> log)
            => (skip, take, _) =>
            {
                log.Add((skip, take));
                IReadOnlyList<int> page = Enumerable.Range(skip, Math.Max(0, Math.Min(take, total - skip))).ToArray();
                return Task.FromResult(page);
            };

        public async Task LoadsPagesUntilAShortPage()
        {
            var log = new List<(int, int)>();
            var c = new IncrementalCollection<int>(Source(25, log), pageSize: 10);
            Check.Equal(10, await c.LoadMoreAsync());
            Check.Equal(10, await c.LoadMoreAsync());
            Check.True(c.HasMoreItems);
            Check.Equal(5, await c.LoadMoreAsync());
            Check.False(c.HasMoreItems);
            Check.Equal(0, await c.LoadMoreAsync());                       // no further request
            Check.Equal("0-10,10-10,20-10", string.Join(",", log.Select(l => l.Item1 + "-" + l.Item2)));
            Check.True(c.SequenceEqual(Enumerable.Range(0, 25)));
        }

        public async Task ExactMultipleNeedsOneEmptyPageToFinish()
        {
            var log = new List<(int, int)>();
            var c = new IncrementalCollection<int>(Source(20, log), pageSize: 10);
            await c.LoadMoreAsync();
            await c.LoadMoreAsync();
            Check.True(c.HasMoreItems, "a full page cannot prove the end");
            Check.Equal(0, await c.LoadMoreAsync());
            Check.False(c.HasMoreItems);
        }

        public async Task ConcurrentCallsShareOneLoad()
        {
            int calls = 0;
            var gate = new TaskCompletionSource<IReadOnlyList<int>>(TaskCreationOptions.RunContinuationsAsynchronously);
            var c = new IncrementalCollection<int>((s, t, ct) => { calls++; return gate.Task; }, 3);
            Task<int> a = c.LoadMoreAsync();
            Task<int> b = c.LoadMoreAsync();
            Check.True(ReferenceEquals(a, b), "same in-flight task");
            Check.True(c.IsLoading);
            gate.SetResult(new[] { 1, 2, 3 });
            Check.Equal(3, await a);
            Check.Equal(1, calls);
            Check.False(c.IsLoading);
        }

        public async Task RaisesPropertyChangedForBindings()
        {
            var changed = new List<string>();
            var c = new IncrementalCollection<int>(Source(2, new List<(int, int)>()), 5);
            ((System.ComponentModel.INotifyPropertyChanged)c).PropertyChanged += (_, e) => changed.Add(e.PropertyName!);
            await c.LoadMoreAsync();
            Check.True(changed.Contains(nameof(IncrementalCollection<int>.IsLoading)));
            Check.True(changed.Contains(nameof(IncrementalCollection<int>.HasMoreItems)));
            Check.True(changed.Contains("Count"));
        }

        public async Task FailedPageIsRecordedRethrownAndRetryable()
        {
            bool fail = true;
            var c = new IncrementalCollection<int>((s, t, ct) =>
                fail ? Task.FromException<IReadOnlyList<int>>(new InvalidOperationException("db down"))
                     : Task.FromResult<IReadOnlyList<int>>(new[] { 7 }), 5);
            await Check.ThrowsAsync<InvalidOperationException>(() => c.LoadMoreAsync());
            Check.Equal("db down", c.LastError!.Message);
            Check.True(c.HasMoreItems, "still retryable");
            Check.False(c.IsLoading);

            fail = false;
            Check.Equal(1, await c.LoadMoreAsync());
            Check.Null(c.LastError);
        }

        public async Task ResetDiscardsAStalePageInFlight()
        {
            var first = new TaskCompletionSource<IReadOnlyList<int>>(TaskCreationOptions.RunContinuationsAsynchronously);
            int call = 0;
            var c = new IncrementalCollection<int>((s, t, ct) =>
                ++call == 1 ? first.Task : Task.FromResult<IReadOnlyList<int>>(new[] { 100, 101 }), 5);

            Task<int> stale = c.LoadMoreAsync();
            Check.Equal(2, await c.ResetAsync());
            first.SetResult(new[] { 1, 2, 3 });                            // arrives after the reset
            Check.Equal(0, await stale);
            Check.Equal("100,101", string.Join(",", c));
        }

        public async Task CancellationIsRethrownNotRecorded()
        {
            using var cts = new CancellationTokenSource();
            var c = new IncrementalCollection<int>(async (s, t, ct) =>
            {
                await Task.Delay(Timeout.Infinite, ct);
                return Array.Empty<int>();
            }, 5);
            Task<int> load = c.LoadMoreAsync(cts.Token);
            cts.Cancel();
            await Check.ThrowsAsync<OperationCanceledException>(() => load);
            Check.Null(c.LastError);
            Check.False(c.IsLoading);
        }

        public void Validation()
        {
            Check.Throws<ArgumentNullException>(() => new IncrementalCollection<int>(null!));
            Check.Throws<ArgumentOutOfRangeException>(() =>
                new IncrementalCollection<int>((s, t, c) => Task.FromResult<IReadOnlyList<int>>(Array.Empty<int>()), 0));
        }
    }
}
