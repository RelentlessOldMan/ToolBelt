using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ToolBelt.Tests.Framework;
using ToolBelt.Wpf;

namespace ToolBelt.Wpf.Tests
{
    /// <summary>
    /// Round 55 review regressions (cross-cutting: cancellation and dispose races), WPF satellite: a cancelled
    /// IncrementalCollection load reports the caller's own token, and a token cancelled up front starts nothing.
    /// </summary>
    public sealed class CancelDisposeReview55Tests
    {
        public async Task IncrementalCollection_PreCancelledToken_StartsNoFetch()
        {
            int calls = 0;
            var c = new IncrementalCollection<int>((skip, take, _) =>
            {
                calls++;
                return Task.FromResult<IReadOnlyList<int>>(new[] { 1, 2 });   // ignores its token
            }, pageSize: 10);
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var ex = await Check.ThrowsAsync<OperationCanceledException>(() => c.LoadMoreAsync(cts.Token));
            Check.True(ex.CancellationToken == cts.Token);
            Check.Equal(0, calls);   // it fetched, and appended the page, then reported success
            Check.Equal(0, c.Count);
        }

        public async Task IncrementalCollection_SoleWaiterCancels_CarriesItsOwnToken()
        {
            var c = new IncrementalCollection<int>(async (skip, take, ct) =>
            {
                await Task.Delay(Timeout.Infinite, ct);
                return new int[0];
            }, pageSize: 10);
            using var cts = new CancellationTokenSource();
            Task<int> load = c.LoadMoreAsync(cts.Token);
            cts.Cancel();
            var ex = await Check.ThrowsAsync<OperationCanceledException>(() => load);
            Check.True(ex.CancellationToken == cts.Token, "the caller's token, not the load's internal one");
        }

        public async Task IncrementalCollection_LastOfTwoWaitersCancels_CarriesItsOwnToken()
        {
            var c = new IncrementalCollection<int>(async (skip, take, ct) =>
            {
                await Task.Delay(Timeout.Infinite, ct);
                return new int[0];
            }, pageSize: 10);
            using var first = new CancellationTokenSource();
            using var second = new CancellationTokenSource();
            Task<int> a = c.LoadMoreAsync(first.Token);
            Task<int> b = c.LoadMoreAsync(second.Token);
            first.Cancel();
            var exA = await Check.ThrowsAsync<OperationCanceledException>(() => a);
            Check.True(exA.CancellationToken == first.Token);
            second.Cancel();
            var exB = await Check.ThrowsAsync<OperationCanceledException>(() => b);
            Check.True(exB.CancellationToken == second.Token, "the last waiter sees its own token");
        }
    }
}
