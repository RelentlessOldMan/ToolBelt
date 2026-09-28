using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using ToolBelt.Process;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Process
{
    // These are integration tests that drive cmd.exe, so they run only on Windows (the test host's OS).
    public sealed class ProcessRunnerTests
    {
        private static bool Windows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        public void CapturesStdoutAndExitCode()
        {
            if (!Windows) return;
            var r = ProcessRunner.Run("cmd.exe", "/c echo hello");
            Check.Equal(0, r.ExitCode);
            Check.True(r.Success);
            Check.True(r.StandardOutput.Contains("hello"), r.StandardOutput);
        }

        public void CapturesExitCodeAndStderr()
        {
            if (!Windows) return;
            Check.Equal(3, ProcessRunner.Run("cmd.exe", "/c exit 3").ExitCode);
            var err = ProcessRunner.Run("cmd.exe", "/c echo oops 1>&2");
            Check.True(err.StandardError.Contains("oops"), err.StandardError);
        }

        // The deadlock regression: a child that writes far more than a pipe buffer (~64 KB) must not hang.
        public void HandlesLargeOutputWithoutDeadlock()
        {
            if (!Windows) return;
            var r = ProcessRunner.Run("cmd.exe", "/c for /L %i in (1,1,20000) do @echo 0123456789012345678901234567890",
                new ProcessRunOptions { Timeout = TimeSpan.FromSeconds(30) });
            Check.False(r.TimedOut);
            Check.True(r.StandardOutput.Length > 64 * 1024, $"only captured {r.StandardOutput.Length} bytes");
        }

        public void TimeoutKillsAndFlags()
        {
            if (!Windows) return;
            var sw = Stopwatch.StartNew();
            var r = ProcessRunner.Run("cmd.exe", "/c ping -n 10 127.0.0.1",
                new ProcessRunOptions { Timeout = TimeSpan.FromSeconds(1) });
            sw.Stop();
            Check.True(r.TimedOut, "should have timed out");
            Check.True(sw.Elapsed < TimeSpan.FromSeconds(6), $"kill was slow: {sw.Elapsed}");
        }

        public void StandardInputRoundTrip()
        {
            if (!Windows) return;
            var r = ProcessRunner.Run("cmd.exe", "/c sort", new ProcessRunOptions { StandardInput = "banana\r\napple\r\ncherry\r\n" });
            int apple = r.StandardOutput.IndexOf("apple", StringComparison.Ordinal);
            int banana = r.StandardOutput.IndexOf("banana", StringComparison.Ordinal);
            Check.True(apple >= 0 && banana >= 0 && apple < banana, r.StandardOutput);
        }

        public void EnvironmentOverride()
        {
            if (!Windows) return;
            var r = ProcessRunner.Run("cmd.exe", "/c echo %TB_TEST_VAR%",
                new ProcessRunOptions { Environment = new Dictionary<string, string?> { ["TB_TEST_VAR"] = "hello123" } });
            Check.True(r.StandardOutput.Contains("hello123"), r.StandardOutput);
        }

        public void EnsureSuccessThrowsWithContext()
        {
            if (!Windows) return;
            var r = ProcessRunner.Run("cmd.exe", "/c exit 7");
            var ex = Check.Throws<ProcessRunException>(() => r.EnsureSuccess());
            Check.Equal(7, ex.ExitCode);
            Check.Throws<ProcessRunException>(() =>
                ProcessRunner.Run("cmd.exe", "/c exit 7", new ProcessRunOptions { ThrowOnNonZeroExit = true }));
        }

        public async Task RunAsyncBasic()
        {
            if (!Windows) return;
            var r = await ProcessRunner.RunAsync("cmd.exe", "/c echo async-ok");
            Check.True(r.Success && r.StandardOutput.Contains("async-ok"));
        }

        public async Task RunAsyncCancels()
        {
            if (!Windows) return;
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
            await Check.ThrowsAsync<OperationCanceledException>(
                () => ProcessRunner.RunAsync("cmd.exe", "/c ping -n 10 127.0.0.1", cancellationToken: cts.Token));
        }

        public void NullFileName_Throws()
        {
            Check.Throws<ArgumentNullException>(() => ProcessRunner.Run(null!));
        }
    }
}
