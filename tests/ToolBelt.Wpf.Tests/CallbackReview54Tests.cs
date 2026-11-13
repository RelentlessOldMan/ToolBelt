using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ToolBelt.Tests.Framework;
using ToolBelt.Wpf;

namespace ToolBelt.Wpf.Tests
{
    /// <summary>
    /// Round 54 review regressions (cross-cutting: throwing callbacks), WPF satellite: a handler that throws must not
    /// leave state stuck, skip the other notifications, or replace the operation's own exception.
    /// </summary>
    public sealed class CallbackReview54Tests
    {
        private sealed class Model : ValidationObservableObject
        {
            public void Set(string name, params string[] errors) => SetErrors(name, errors);
            public void Add(string name, string error) => AddError(name, error);
            public void ClearAll() => ClearAllErrors();
        }

        public void ClearAllErrors_ThrowingErrorsChangedHandler_StillRaisesTheRest()
        {
            var m = new Model();
            m.Set("A", "a");
            m.Set("B", "b");
            var seen = new List<string>();
            m.ErrorsChanged += (_, e) =>
            {
                seen.Add(e.PropertyName!);
                if (e.PropertyName == "A") throw new InvalidOperationException("handler");
            };
            bool hasErrorsRaised = false;
            m.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(m.HasErrors)) hasErrorsRaised = true; };

            var ex = Check.Throws<InvalidOperationException>(() => m.ClearAll());
            Check.Equal("handler", ex.Message);
            Check.Equal("A,B", string.Join(",", seen.OrderBy(s => s, StringComparer.Ordinal)));
            Check.True(hasErrorsRaised, "HasErrors change announced");
            Check.False(m.HasErrors);
        }

        public void ClearAllErrors_SeveralThrowingHandlers_AggregatesThem()
        {
            var m = new Model();
            m.Set("A", "a");
            m.Set("B", "b");
            m.ErrorsChanged += (_, e) => throw new InvalidOperationException(e.PropertyName);
            var ex = Check.Throws<AggregateException>(() => m.ClearAll());
            Check.Equal(2, ex.InnerExceptions.Count);
        }

        public void AddError_ThrowingErrorsChangedHandler_StillAnnouncesHasErrors()
        {
            var m = new Model();
            m.ErrorsChanged += (_, __) => throw new InvalidOperationException("handler");
            bool hasErrorsRaised = false;
            m.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(m.HasErrors)) hasErrorsRaised = true; };
            Check.Throws<InvalidOperationException>(() => m.Add("A", "a"));
            Check.True(hasErrorsRaised);
            Check.True(m.HasErrors);
        }

        public async Task AsyncRelayCommand_ThrowingCanExecuteChanged_DoesNotHideTheOperationsException()
        {
            int raised = 0;
            var cmd = new AsyncRelayCommand(async () => { await Task.Yield(); throw new InvalidOperationException("op"); });
            cmd.CanExecuteChanged += (_, __) => { if (++raised == 2) throw new FormatException("handler"); };
            var ex = await Check.ThrowsAsync<AggregateException>(() => cmd.ExecuteAsync());
            Check.True(ex.InnerExceptions[0] is InvalidOperationException { Message: "op" });
            Check.True(ex.InnerExceptions[1] is FormatException);
            Check.True(cmd.CanExecute(null), "not stuck executing");
        }

        public async Task AsyncRelayCommand_OperationFailsAlone_ThrowsItUnwrapped()
        {
            var cmd = new AsyncRelayCommand(async () => { await Task.Yield(); throw new InvalidOperationException("op"); });
            var ex = await Check.ThrowsAsync<InvalidOperationException>(() => cmd.ExecuteAsync());
            Check.Equal("op", ex.Message);
        }

        public async Task AsyncRelayCommand_CancelledOperation_StaysCancelled()
        {
            var cmd = new AsyncRelayCommand(async () => { await Task.Yield(); throw new OperationCanceledException(); });
            Task run = cmd.ExecuteAsync();
            try { await run; } catch (OperationCanceledException) { }
            Check.True(run.IsCanceled);
        }

        public async Task IncrementalCollection_ThrowingHandlerOnLoadingStart_DoesNotLeaveIsLoadingStuck()
        {
            var c = new IncrementalCollection<int>((skip, take, _) => Task.FromResult<IReadOnlyList<int>>(new[] { 1 }), pageSize: 10);
            bool fail = true;
            ((INotifyPropertyChanged)c).PropertyChanged += (_, e) =>
            {
                if (fail && e.PropertyName == nameof(c.IsLoading) && c.IsLoading) throw new InvalidOperationException("handler");
            };
            await Check.ThrowsAsync<InvalidOperationException>(() => c.LoadMoreAsync());
            Check.False(c.IsLoading, "IsLoading reset");
            fail = false;
            Check.Equal(1, await c.LoadMoreAsync());
        }

        public async Task IncrementalCollection_ThrowingHandlerOnLoadingEnd_DoesNotHideTheLoadsException()
        {
            var c = new IncrementalCollection<int>(async (skip, take, _) =>
            {
                await Task.Yield();
                throw new FormatException("load");
            }, pageSize: 10);
            ((INotifyPropertyChanged)c).PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(c.IsLoading) && !c.IsLoading) throw new InvalidOperationException("handler");
            };
            var ex = await Check.ThrowsAsync<AggregateException>(() => c.LoadMoreAsync());
            Check.True(ex.InnerExceptions.Any(e => e is FormatException), "the load's own exception survives");
            Check.True(c.LastError is FormatException);
            Check.False(c.IsLoading);
        }
    }
}
