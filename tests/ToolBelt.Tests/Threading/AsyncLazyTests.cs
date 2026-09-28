using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ToolBelt.Threading;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Threading
{
    public sealed class AsyncLazyTests
    {
        public async Task RunsFactoryExactlyOnce_UnderConcurrency()
        {
            int calls = 0;
            var lazy = new AsyncLazy<int>(async () =>
            {
                Interlocked.Increment(ref calls);
                await Task.Yield();
                return 42;
            });

            Check.False(lazy.IsStarted);

            var results = await Task.WhenAll(Enumerable.Range(0, 50).Select(_ => lazy.Value));

            Check.True(lazy.IsStarted);
            Check.Equal(1, calls);
            Check.True(results.All(r => r == 42));
        }

        public async Task SupportsAwaitSyntax()
        {
            var lazy = new AsyncLazy<string>(() => Task.FromResult("hello"));
            string value = await lazy;
            Check.Equal("hello", value);
        }

        public async Task CachesFaultedResult_WithoutRetrying()
        {
            int calls = 0;
            var lazy = new AsyncLazy<int>(async () =>
            {
                Interlocked.Increment(ref calls);
                await Task.Yield();
                throw new InvalidOperationException("boom");
            });

            await Check.ThrowsAsync<InvalidOperationException>(() => lazy.Value);
            await Check.ThrowsAsync<InvalidOperationException>(() => lazy.Value);

            Check.Equal(1, calls); // factory not retried after failure
        }

        public void NullFactory_Throws()
        {
            Check.Throws<ArgumentNullException>(() => new AsyncLazy<int>(null!));
        }
    }
}
