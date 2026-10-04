using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ToolBelt.Tests.Framework;
using ToolBelt.Wpf;

namespace ToolBelt.Wpf.Tests
{
    public sealed class NotifyTaskCompletionTests
    {
        public async Task Success_ExposesResult_AndNotifies()
        {
            var tcs = new TaskCompletionSource<int>();
            var ntc = new NotifyTaskCompletion<int>(tcs.Task);
            Check.True(ntc.IsNotCompleted, "not completed initially");
            Check.False(ntc.IsCompleted);

            var changed = new List<string?>();
            ntc.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

            tcs.SetResult(42);
            await ntc.Completion;

            Check.True(ntc.IsCompleted);
            Check.True(ntc.IsSuccessfullyCompleted);
            Check.False(ntc.IsFaulted);
            Check.Equal(42, ntc.Result);
            Check.True(changed.Contains("Result") && changed.Contains("IsSuccessfullyCompleted"),
                "notified Result + IsSuccessfullyCompleted");
        }

        public async Task Faulted_SurfacesError_WithoutThrowing()
        {
            var tcs = new TaskCompletionSource<int>();
            var ntc = new NotifyTaskCompletion<int>(tcs.Task);

            tcs.SetException(new InvalidOperationException("nope"));
            await ntc.Completion; // must not throw even though the task faulted

            Check.True(ntc.IsFaulted);
            Check.False(ntc.IsSuccessfullyCompleted);
            Check.Equal(0, ntc.Result); // default for int when not successful
            Check.NotNull(ntc.InnerException);
            Check.Equal("nope", ntc.ErrorMessage);
        }

        public async Task AlreadyCompletedTask_IsImmediatelyDone()
        {
            var ntc = new NotifyTaskCompletion<string>(Task.FromResult("hi"));
            await ntc.Completion;
            Check.True(ntc.IsSuccessfullyCompleted);
            Check.Equal("hi", ntc.Result);
        }

        public void NullTask_Throws()
        {
            Check.Throws<ArgumentNullException>(() => new NotifyTaskCompletion<int>(null!));
        }
    }
}
