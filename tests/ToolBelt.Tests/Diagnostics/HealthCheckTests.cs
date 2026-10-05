using System;
using System.Threading;
using System.Threading.Tasks;
using ToolBelt.Diagnostics;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Diagnostics
{
    public sealed class HealthCheckTests
    {
        private static readonly TimeSpan Short = TimeSpan.FromMilliseconds(100);

        public async Task Empty_IsHealthy()
        {
            HealthReport report = await new HealthCheck().RunAsync();
            Check.Equal(HealthStatus.Healthy, report.Status);
            Check.Equal(0, report.Probes.Count);
            Check.True(report.IsHealthy);
        }

        public async Task WorstStatusWins_AndOrderIsPreserved()
        {
            var check = new HealthCheck()
                .Add("db", () => ProbeResult.Healthy("connected"))
                .Add("cache", () => ProbeResult.Degraded("slow"))
                .Add("output", () => true, "unreachable");
            HealthReport report = await check.RunAsync();

            Check.Equal(HealthStatus.Degraded, report.Status);
            Check.Equal("db", report.Probes[0].Name);
            Check.Equal("cache", report.Probes[1].Name);
            Check.Equal("output", report.Probes[2].Name);
            Check.Equal("slow", report.Probes[1].Message);

            check.Add("device", () => ProbeResult.Unhealthy("absent"));
            Check.Equal(HealthStatus.Unhealthy, (await check.RunAsync()).Status);
        }

        public async Task BoolProbe_FalseReportsFailureStatusAndMessage()
        {
            HealthReport report = await new HealthCheck()
                .Add("disk", () => false, "less than 1 GB free", failureStatus: HealthStatus.Degraded)
                .RunAsync();
            Check.Equal(HealthStatus.Degraded, report.Status);
            Check.Equal("less than 1 GB free", report.Probes[0].Message);
        }

        public async Task ThrowingProbe_IsUnhealthyWithException()
        {
            HealthReport report = await new HealthCheck()
                .Add("async-throw", async _ => { await Task.Yield(); throw new InvalidOperationException("boom"); })
                .Add("sync-throw", _ => throw new InvalidOperationException("before task"))
                .RunAsync();

            Check.Equal(HealthStatus.Unhealthy, report.Status);
            foreach (ProbeReport p in report.Probes)
            {
                Check.Equal(HealthStatus.Unhealthy, p.Status);
                Check.True(p.Exception is InvalidOperationException, p.Name);
                Check.False(p.TimedOut);
            }
            Check.Equal("boom", report.Probes[0].Message);
            Check.Equal("before task", report.Probes[1].Message);
        }

        public async Task NonCriticalFailure_ReportsDegraded()
        {
            HealthReport report = await new HealthCheck()
                .Add("telemetry", _ => throw new Exception("down"), failureStatus: HealthStatus.Degraded)
                .Add("core", () => ProbeResult.Healthy())
                .RunAsync();
            Check.Equal(HealthStatus.Degraded, report.Status);
        }

        public async Task Timeout_ProbeHonouringToken()
        {
            HealthReport report = await new HealthCheck()
                .Add("hang", async ct => { await Task.Delay(Timeout.Infinite, ct); return ProbeResult.Healthy(); }, Short)
                .RunAsync();
            ProbeReport p = report.Probes[0];
            Check.True(p.TimedOut);
            Check.Equal(HealthStatus.Unhealthy, p.Status);
            Check.True(p.Message!.StartsWith("Timed out after 100 ms", StringComparison.Ordinal), p.Message);
        }

        public async Task Timeout_ProbeIgnoringToken_StillEnforced()
        {
            var never = new TaskCompletionSource<ProbeResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<HealthReport> run = new HealthCheck()
                .Add("stubborn", _ => never.Task, Short) // never looks at its token
                .RunAsync();

            Task first = await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(10)));
            Check.True(first == run, "the run must finish on the probe's timeout, not wait for the probe");
            HealthReport report = await run;
            Check.True(report.Probes[0].TimedOut);
            Check.Equal(Short, report.Probes[0].Duration);
            never.TrySetResult(ProbeResult.Healthy());
        }

        public async Task AbandonedProbeFault_IsObserved()
        {
            bool unobserved = false;
            EventHandler<UnobservedTaskExceptionEventArgs> handler = (_, e) =>
            {
                if (e.Exception?.InnerException?.Message == "late fault") unobserved = true;
            };
            TaskScheduler.UnobservedTaskException += handler;
            try
            {
                var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                await new HealthCheck()
                    .Add("late", async _ => { await gate.Task; throw new InvalidOperationException("late fault"); }, Short)
                    .RunAsync();
                gate.SetResult(true);              // the abandoned probe now faults
                await Task.Delay(50);
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                Check.False(unobserved, "a timed-out probe's later exception must be observed");
            }
            finally { TaskScheduler.UnobservedTaskException -= handler; }
        }

        public async Task ProbesRunConcurrently()
        {
            // Each probe waits for the other to start: sequential execution could only time out.
            var aStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var bStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            HealthReport report = await new HealthCheck(TimeSpan.FromSeconds(5))
                .Add("a", async _ => { aStarted.TrySetResult(true); await bStarted.Task; return ProbeResult.Healthy(); })
                .Add("b", async _ => { bStarted.TrySetResult(true); await aStarted.Task; return ProbeResult.Healthy(); })
                .RunAsync();
            Check.Equal(HealthStatus.Healthy, report.Status);
            Check.False(report.Probes[0].TimedOut);
            Check.False(report.Probes[1].TimedOut);
        }

        public async Task OuterCancellation_AbandonsRun()
        {
            using var already = new CancellationTokenSource();
            already.Cancel();
            await Check.ThrowsAsync<OperationCanceledException>(
                () => new HealthCheck().Add("x", () => ProbeResult.Healthy()).RunAsync(already.Token));

            using var cts = new CancellationTokenSource();
            var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<HealthReport> run = new HealthCheck(TimeSpan.FromSeconds(30))
                .Add("slow", async ct => { started.TrySetResult(true); await Task.Delay(Timeout.Infinite, ct); return ProbeResult.Healthy(); })
                .RunAsync(cts.Token);
            await started.Task;
            cts.Cancel();
            await Check.ThrowsAsync<OperationCanceledException>(() => run);
        }

        public async Task Report_ToStringSummarises()
        {
            HealthReport report = await new HealthCheck()
                .Add("db", () => ProbeResult.Healthy())
                .Add("device", () => ProbeResult.Unhealthy("not found"))
                .RunAsync();
            string text = report.ToString();
            Check.True(text.StartsWith("Overall: Unhealthy", StringComparison.Ordinal), text);
            Check.True(text.Contains("db: Healthy"), text);
            Check.True(text.Contains("device: Unhealthy") && text.Contains("not found"), text);
        }

        public void Registration_Validation()
        {
            var check = new HealthCheck().Add("a", () => ProbeResult.Healthy());
            Check.Equal(1, check.Count);
            Check.Throws<ArgumentException>(() => check.Add("a", () => ProbeResult.Healthy()));               // duplicate
            Check.Throws<ArgumentException>(() => check.Add("", () => ProbeResult.Healthy()));
            Check.Throws<ArgumentNullException>(() => check.Add(null!, () => ProbeResult.Healthy()));
            Check.Throws<ArgumentNullException>(() => check.Add("b", (Func<ProbeResult>)null!));
            Check.Throws<ArgumentException>(() => check.Add("b", () => ProbeResult.Healthy(), failureStatus: HealthStatus.Healthy));
            Check.Throws<ArgumentOutOfRangeException>(() => check.Add("b", () => ProbeResult.Healthy(), TimeSpan.Zero));
            Check.Throws<ArgumentOutOfRangeException>(() => new HealthCheck(TimeSpan.FromSeconds(-1)));
            Check.Equal(1, check.Count);
        }
    }
}
