using System;
using System.Threading.Tasks;
using System.Windows.Input;
using ToolBelt.Tests.Framework;
using ToolBelt.Wpf;

namespace ToolBelt.Wpf.Tests
{
    public sealed class AsyncRelayCommandTests
    {
        public async Task ExecuteAsync_SetsIsExecuting_AndBlocksReentry()
        {
            var gate = new TaskCompletionSource();
            int runs = 0;
            var cmd = new AsyncRelayCommand(async () => { runs++; await gate.Task; });

            Check.True(cmd.CanExecute(null), "runnable before start");

            Task first = cmd.ExecuteAsync();
            Check.True(cmd.IsExecuting, "executing while task in flight");
            Check.False(cmd.CanExecute(null), "blocked while running");

            await cmd.ExecuteAsync(); // re-entry must be a no-op
            Check.Equal(1, runs);

            gate.SetResult();
            await first;
            Check.False(cmd.IsExecuting, "not executing after completion");
            Check.True(cmd.CanExecute(null), "runnable again");
            Check.Equal(1, runs);
        }

        public async Task CanExecuteChanged_FiresOnStartAndFinish()
        {
            var gate = new TaskCompletionSource();
            var cmd = new AsyncRelayCommand(async () => await gate.Task);
            int fired = 0;
            cmd.CanExecuteChanged += (_, _) => fired++;

            Task t = cmd.ExecuteAsync(); // fires once (start)
            gate.SetResult();
            await t;                     // fires again (finish)
            Check.Equal(2, fired);
        }

        public async Task ResetsIsExecuting_WhenTaskThrows()
        {
            var cmd = new AsyncRelayCommand(() => throw new InvalidOperationException("boom"));
            await Check.ThrowsAsync<InvalidOperationException>(() => cmd.ExecuteAsync());
            Check.False(cmd.IsExecuting, "flag reset even on exception");
            Check.True(cmd.CanExecute(null));
        }

        public async Task RespectsCanExecutePredicate()
        {
            bool allowed = false;
            var cmd = new AsyncRelayCommand(() => Task.CompletedTask, () => allowed);
            Check.False(cmd.CanExecute(null));
            await cmd.ExecuteAsync(); // predicate false -> no-op (no throw)
            allowed = true;
            Check.True(cmd.CanExecute(null));
        }

        public void NullExecute_Throws()
        {
            Check.Throws<ArgumentNullException>(() => new AsyncRelayCommand(null!));
        }

        public void ImplementsICommand()
        {
            ICommand cmd = new AsyncRelayCommand(() => Task.CompletedTask);
            Check.NotNull(cmd);
        }
    }
}
