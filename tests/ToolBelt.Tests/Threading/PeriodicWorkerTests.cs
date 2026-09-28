using System;
using System.Threading;
using System.Threading.Tasks;
using ToolBelt.Threading;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Threading
{
    public sealed class PeriodicWorkerTests
    {
        // A controllable delay: releases `iterationDone` each time the loop reaches the delay, then waits
        // for the test to release `proceed`. This lets the test step the loop deterministically.
        public async Task RunsExactlyExpectedIterations()
        {
            var iterationDone = new SemaphoreSlim(0);
            var proceed = new SemaphoreSlim(0);
            int count = 0;

            Func<TimeSpan, CancellationToken, Task> delay = async (_, ct) =>
            {
                iterationDone.Release();
                await proceed.WaitAsync(ct);
            };

            var worker = new PeriodicWorker(
                work: _ => { Interlocked.Increment(ref count); return Task.CompletedTask; },
                interval: TimeSpan.FromMilliseconds(1),
                delay: delay);
            worker.Start();

            const int steps = 5;
            for (int i = 0; i < steps; i++)
            {
                await iterationDone.WaitAsync(); // work #(i+1) has run and the loop reached the delay
                if (i < steps - 1) proceed.Release();
            }
            worker.Dispose(); // cancels while the loop awaits `proceed` on the 5th iteration

            Check.Equal(steps, count);
        }

        public async Task ContinuesAfterHandlerException()
        {
            var iterationDone = new SemaphoreSlim(0);
            var proceed = new SemaphoreSlim(0);
            int count = 0;
            Exception? reported = null;

            Func<TimeSpan, CancellationToken, Task> delay = async (_, ct) =>
            {
                iterationDone.Release();
                await proceed.WaitAsync(ct);
            };

            var worker = new PeriodicWorker(
                work: _ =>
                {
                    int n = Interlocked.Increment(ref count);
                    if (n == 1) throw new InvalidOperationException("first fails");
                    return Task.CompletedTask;
                },
                interval: TimeSpan.FromMilliseconds(1),
                onError: ex => reported = ex,
                delay: delay);
            worker.Start();

            for (int i = 0; i < 3; i++)
            {
                await iterationDone.WaitAsync();
                if (i < 2) proceed.Release();
            }
            worker.Dispose();

            Check.Equal(3, count);                                // loop survived the exception
            Check.True(reported is InvalidOperationException);    // and reported it
        }

        public void DoubleStart_Throws()
        {
            var worker = new PeriodicWorker(_ => Task.CompletedTask, TimeSpan.FromMilliseconds(1),
                delay: (_, __) => Task.Delay(1000));
            worker.Start();
            Check.Throws<InvalidOperationException>(() => worker.Start());
            worker.Dispose();
        }

        public void NullWork_Throws()
        {
            Check.Throws<ArgumentNullException>(() => new PeriodicWorker(null!, TimeSpan.FromSeconds(1)));
        }
    }
}
