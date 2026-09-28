using System;
using System.Threading;
using System.Threading.Tasks;
using ToolBelt.Threading;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Threading
{
    public sealed class TaskExtensionsTests
    {
        public async Task WithTimeout_Completes()
        {
            int result = await Task.FromResult(42).WithTimeout(TimeSpan.FromSeconds(5));
            Check.Equal(42, result);
        }

        public async Task WithTimeout_Fires()
        {
            var never = new TaskCompletionSource<int>().Task;
            await Check.ThrowsAsync<TimeoutException>(() => never.WithTimeout(TimeSpan.FromMilliseconds(20)));
        }

        public async Task WithCancellation_Throws()
        {
            var never = new TaskCompletionSource<int>().Task;
            using var cts = new CancellationTokenSource();
            var task = never.WithCancellation(cts.Token);
            cts.Cancel();
            await Check.ThrowsAsync<OperationCanceledException>(() => task);
        }

        public async Task FireAndForget_RoutesError()
        {
            var signal = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task.Run(() => throw new InvalidOperationException("boom"))
                .FireAndForget(ex => signal.TrySetResult(ex));
            Exception captured = await signal.Task.WithTimeout(TimeSpan.FromSeconds(5));
            Check.True(captured is InvalidOperationException);
        }

        public async Task WhenAllOrFirstException_ReturnsResults()
        {
            var results = await ToolBelt.Threading.TaskExtensions.WhenAllOrFirstException(new[]
            {
                Task.FromResult(1), Task.FromResult(2), Task.FromResult(3),
            });
            Check.Equal(6, results[0] + results[1] + results[2]);
        }

        public async Task WhenAllOrFirstException_ThrowsFirst()
        {
            var faulted = Task.FromException<int>(new InvalidOperationException("nope"));
            await Check.ThrowsAsync<InvalidOperationException>(
                () => ToolBelt.Threading.TaskExtensions.WhenAllOrFirstException(new[] { Task.FromResult(1), faulted }));
        }

        public void NullArguments_Throw()
        {
            Check.Throws<ArgumentNullException>(() => ((Task)null!).FireAndForget(_ => { }));
            Check.Throws<ArgumentNullException>(() => Task.CompletedTask.FireAndForget(null!));
        }
    }
}
