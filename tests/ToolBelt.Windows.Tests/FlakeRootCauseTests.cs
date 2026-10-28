using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using ToolBelt.Tests.Framework;
using ToolBelt.Windows;

namespace ToolBelt.Windows.Tests
{
    /// <summary>
    /// Regressions from round 46 (root causes of flaky tests): each test failed before its fix (here, by not compiling:
    /// there was no way to start a process inside a job).
    /// </summary>
    public sealed class FlakeRootCauseTests
    {
        private static int Pings() => Process.GetProcessesByName("PING").Length;

        private static void KillPings()
        {
            foreach (var p in Process.GetProcessesByName("PING"))
            {
                try { p.Kill(); } catch { }
                p.Dispose();
            }
        }

        // Process.Start then AssignProcess let a child started in between escape the job: with a 200 ms stall before the
        // assignment (a preemption or GC pause), cmd's ping survived kill-on-close in 10 of 10 runs.
        public void Start_ChildrenStartedAtOnceStayInTheJob()
        {
            int before = Pings();
            try
            {
                using (var job = new JobObject(killOnClose: true))
                {
                    using var cmd = job.Start("cmd.exe", "/c ping -n 30 127.0.0.1");
                    Thread.Sleep(500);                                  // cmd has started ping by now
                    Check.True(job.QueryAccounting().ActiveProcesses >= 2, "cmd and its ping are in the job");
                }
                Thread.Sleep(300);
                Check.Equal(before, Pings());
            }
            finally { if (Pings() > before) KillPings(); }
        }

        public void Start_ReportsExitCodeAndRefusesOverTheLimitBeforeRunning()
        {
            using var job = new JobObject();
            using (var quick = job.Start("cmd.exe", "/c exit 7"))
            {
                Check.True(quick.WaitForExit(10000));
                Check.Equal(7, quick.ExitCode);
            }
            job.SetActiveProcessLimit(1);
            int before = Pings();
            try
            {
                using var first = job.Start("ping.exe", "127.0.0.1 -n 30");
                var e = Check.Throws<Win32Exception>(() => job.Start("ping.exe", "127.0.0.1 -n 30"));
                Check.True(e.Message.Contains("terminated"), e.Message);
                Thread.Sleep(300);
                Check.Equal(before + 1, Pings());                       // the refused one never ran
            }
            finally { job.Dispose(); Thread.Sleep(300); if (Pings() > before) KillPings(); }
        }

        public void Start_ValidatesItsArguments()
        {
            using var job = new JobObject();
            Check.Equal("fileName", Check.Throws<ArgumentNullException>(() => job.Start(null!)).ParamName);
            Check.Equal("fileName", Check.Throws<ArgumentException>(() => job.Start("")).ParamName);
            Check.Equal("fileName", Check.Throws<ArgumentException>(() => job.Start("a\"b.exe")).ParamName);
            Check.Equal("arguments", Check.Throws<ArgumentException>(() => job.Start("cmd.exe", "/c\0x")).ParamName);
            Check.Throws<Win32Exception>(() => job.Start("no-such-program-" + Guid.NewGuid().ToString("N") + ".exe"));
            job.Dispose();
            Check.Throws<ObjectDisposedException>(() => job.Start("cmd.exe"));
        }
    }
}
