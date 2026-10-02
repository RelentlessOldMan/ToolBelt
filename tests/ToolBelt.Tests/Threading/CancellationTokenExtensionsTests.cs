using System;
using System.Threading;
using System.Threading.Tasks;
using ToolBelt.Threading;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Threading
{
    public sealed class CancellationTokenExtensionsTests
    {
        public async Task WhenCanceled_CompletesOnCancel()
        {
            using var cts = new CancellationTokenSource();
            Task t = cts.Token.WhenCanceled();
            Check.False(t.IsCompleted);
            cts.Cancel();
            await t.WithTimeout(TimeSpan.FromSeconds(5));
            Check.True(t.IsCompleted);
        }

        public void WhenCanceled_AlreadyCanceled_IsCompleted()
        {
            var token = new CancellationToken(canceled: true);
            Check.True(token.WhenCanceled().IsCompleted);
        }

        public async Task WhenCanceled_None_NeverCompletes()
        {
            Task t = CancellationToken.None.WhenCanceled();
            await Task.Yield();
            Check.False(t.IsCompleted); // None can never be canceled
        }

        public void CreateLinkedTimeout_CancelsWithParent()
        {
            using var parent = new CancellationTokenSource();
            using var linked = parent.Token.CreateLinkedTimeout(TimeSpan.FromMinutes(5));
            Check.False(linked.IsCancellationRequested);
            parent.Cancel();
            Check.True(linked.IsCancellationRequested);
        }

        public async Task CreateLinkedTimeout_CancelsAfterTimeout()
        {
            using var linked = CancellationToken.None.CreateLinkedTimeout(TimeSpan.FromMilliseconds(30));
            await linked.Token.WhenCanceled().WithTimeout(TimeSpan.FromSeconds(5));
            Check.True(linked.IsCancellationRequested);
        }

        public void CreateLinkedTimeout_NegativeTimeout_Throws()
        {
            Check.Throws<ArgumentOutOfRangeException>(
                () => CancellationToken.None.CreateLinkedTimeout(TimeSpan.FromSeconds(-5)));
        }
    }
}
