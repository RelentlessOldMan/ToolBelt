using System;
using System.Diagnostics;
using ToolBelt.Tests.Framework;
using ToolBelt.Windows;

namespace ToolBelt.Windows.Tests
{
    public sealed class JobObjectTests
    {
        public void KillOnClose_TerminatesAssignedChild()
        {
            // A long-lived child that would otherwise outlive this test.
            var psi = new ProcessStartInfo("ping.exe", "127.0.0.1 -n 60")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
            };
            Process? child = null;
            try
            {
                var job = new JobObject(killOnClose: true);
                child = Process.Start(psi)!;
                job.AssignProcess(child);
                Check.False(child.HasExited, "child running after assignment");

                job.Dispose(); // closing the job must terminate the assigned child
                bool exited = child.WaitForExit(5000);
                Check.True(exited, "child terminated when job closed");
            }
            finally
            {
                try { if (child is not null && !child.HasExited) child.Kill(); } catch { /* best effort */ }
                child?.Dispose();
            }
        }

        public void AssignAfterDispose_Throws()
        {
            var job = new JobObject();
            job.Dispose();
            Check.Throws<ObjectDisposedException>(() => job.AssignProcess(IntPtr.Zero));
        }

        public void AssignNullProcess_Throws()
        {
            using var job = new JobObject();
            Check.Throws<ArgumentNullException>(() => job.AssignProcess((Process)null!));
        }
    }
}
