// ToolBelt.Windows drop-in — Windows-only (net8.0-windows), self-contained (Win32 job objects).
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using SystemProcess = System.Diagnostics.Process;

namespace ToolBelt.Windows
{
    /// <summary>
    /// A Win32 job object that groups child processes so they can be managed and terminated together. With
    /// <c>killOnClose</c> (the default), every assigned process is terminated when this object is disposed
    /// or the owning process exits — a reliable way to prevent orphaned children even on a hard crash.
    /// Assign a process with <see cref="AssignProcess(System.Diagnostics.Process)"/> right after starting it. Limits —
    /// per-process or whole-job memory caps, a process-count cap and a hard CPU-rate cap — apply to everything in the job,
    /// and <see cref="QueryAccounting"/> reports CPU time and peak memory.
    /// </summary>
    public sealed class JobObject : IDisposable
    {
        private IntPtr _handle;
        private bool _disposed;

        /// <summary>Creates a job object. When <paramref name="killOnClose"/> is true, assigned processes die with the job.</summary>
        public JobObject(bool killOnClose = true)
        {
            _handle = CreateJobObject(IntPtr.Zero, null);
            if (_handle == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateJobObject failed.");

            if (killOnClose)
            {
                var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
                info.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
                int length = Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
                if (!SetInformationJobObject(_handle, JobObjectExtendedLimitInformation, ref info, (uint)length))
                {
                    int err = Marshal.GetLastWin32Error();
                    CloseHandle(_handle);
                    _handle = IntPtr.Zero;
                    throw new Win32Exception(err, "SetInformationJobObject failed.");
                }
            }
        }

        /// <summary>Assigns a running process to the job. Throws if the process has exited or cannot be assigned.</summary>
        public void AssignProcess(SystemProcess process)
        {
            if (process is null) throw new ArgumentNullException(nameof(process));
            AssignProcess(process.Handle);
        }

        /// <summary>Assigns a process by native handle (must have the required access rights).</summary>
        public void AssignProcess(IntPtr processHandle)
        {
            ThrowIfDisposed();
            if (!AssignProcessToJobObject(_handle, processHandle))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "AssignProcessToJobObject failed.");
        }

        /// <summary>
        /// Caps the committed memory of <em>each</em> process in the job; an allocation beyond it fails in that process
        /// (an OutOfMemoryException in .NET) instead of paging the machine to death.
        /// </summary>
        public void SetProcessMemoryLimit(long bytes) => SetMemoryLimit(bytes, JOB_OBJECT_LIMIT_PROCESS_MEMORY);

        /// <summary>Caps the committed memory of all processes in the job together.</summary>
        public void SetJobMemoryLimit(long bytes) => SetMemoryLimit(bytes, JOB_OBJECT_LIMIT_JOB_MEMORY);

        /// <summary>Limits how many processes can be in the job at once; creating one more fails.</summary>
        public void SetActiveProcessLimit(int count)
        {
            if (count < 1) throw new ArgumentOutOfRangeException(nameof(count), count, "Must allow at least one process.");
            UpdateExtended(b =>
            {
                b.Info.BasicLimitInformation.LimitFlags |= JOB_OBJECT_LIMIT_ACTIVE_PROCESS;
                b.Info.BasicLimitInformation.ActiveProcessLimit = (uint)count;
            });
        }

        /// <summary>
        /// Hard-caps the job's CPU use at <paramref name="percent"/> of the <em>whole machine</em> (all cores together; 100
        /// = every core busy) — a background analysis that can't starve the acquisition loop. Pass null to remove the cap.
        /// </summary>
        public void SetCpuRateLimit(double? percent)
        {
            ThrowIfDisposed();
            var info = new JOBOBJECT_CPU_RATE_CONTROL_INFORMATION();
            if (percent is double p)
            {
                if (!(p > 0 && p <= 100)) throw new ArgumentOutOfRangeException(nameof(percent), percent, "Percent must be in (0, 100].");
                info.ControlFlags = JOB_OBJECT_CPU_RATE_CONTROL_ENABLE | JOB_OBJECT_CPU_RATE_CONTROL_HARD_CAP;
                info.CpuRate = (uint)Math.Max(1, Math.Round(p * 100));
            }
            if (!SetInformationJobObjectCpu(_handle, JobObjectCpuRateControlInformation, ref info, (uint)Marshal.SizeOf<JOBOBJECT_CPU_RATE_CONTROL_INFORMATION>()))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Setting the job CPU rate failed.");
        }

        /// <summary>CPU time, process counts and peak memory for everything that has run in the job.</summary>
        public JobAccounting QueryAccounting()
        {
            ThrowIfDisposed();
            if (!QueryInformationJobObjectBasic(_handle, JobObjectBasicAccountingInformation, out JOBOBJECT_BASIC_ACCOUNTING_INFORMATION basic,
                    (uint)Marshal.SizeOf<JOBOBJECT_BASIC_ACCOUNTING_INFORMATION>(), IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "QueryInformationJobObject failed.");
            var ext = QueryExtended();
            return new JobAccounting(TimeSpan.FromTicks(basic.TotalUserTime), TimeSpan.FromTicks(basic.TotalKernelTime), (int)basic.ActiveProcesses,
                (int)basic.TotalProcesses, (int)basic.TotalTerminatedProcesses, (long)ext.PeakProcessMemoryUsed, (long)ext.PeakJobMemoryUsed);
        }

        private void SetMemoryLimit(long bytes, uint flag)
        {
            if (bytes < 1 << 20) throw new ArgumentOutOfRangeException(nameof(bytes), bytes, "Limit must be at least 1 MiB.");
            UpdateExtended(b =>
            {
                b.Info.BasicLimitInformation.LimitFlags |= flag;
                if (flag == JOB_OBJECT_LIMIT_PROCESS_MEMORY) b.Info.ProcessMemoryLimit = (UIntPtr)(ulong)bytes;
                else b.Info.JobMemoryLimit = (UIntPtr)(ulong)bytes;
            });
        }

        // Read-modify-write so setting one limit keeps the others (and kill-on-close).
        private void UpdateExtended(Action<JobLimitsBox> change)
        {
            ThrowIfDisposed();
            var box = new JobLimitsBox { Info = QueryExtended() };
            change(box);
            var info = box.Info;
            if (!SetInformationJobObject(_handle, JobObjectExtendedLimitInformation, ref info, (uint)Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>()))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "SetInformationJobObject failed.");
        }

        private sealed class JobLimitsBox
        {
            public JOBOBJECT_EXTENDED_LIMIT_INFORMATION Info;
        }

        private JOBOBJECT_EXTENDED_LIMIT_INFORMATION QueryExtended()
        {
            if (!QueryInformationJobObject(_handle, JobObjectExtendedLimitInformation, out JOBOBJECT_EXTENDED_LIMIT_INFORMATION info,
                    (uint)Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>(), IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "QueryInformationJobObject failed.");
            return info;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(JobObject));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_handle != IntPtr.Zero)
            {
                CloseHandle(_handle); // closing the last handle enforces kill-on-close
                _handle = IntPtr.Zero;
            }
            GC.SuppressFinalize(this);
        }

        // Backstop: if the caller forgets to Dispose, close the handle so it neither leaks nor (with
        // kill-on-close) silently keeps the child processes alive past this object's lifetime.
        ~JobObject()
        {
            if (_handle != IntPtr.Zero)
                CloseHandle(_handle);
        }

        private const int JobObjectExtendedLimitInformation = 9, JobObjectBasicAccountingInformation = 1, JobObjectCpuRateControlInformation = 15;
        private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000, JOB_OBJECT_LIMIT_PROCESS_MEMORY = 0x100, JOB_OBJECT_LIMIT_JOB_MEMORY = 0x200,
                           JOB_OBJECT_LIMIT_ACTIVE_PROCESS = 0x8, JOB_OBJECT_CPU_RATE_CONTROL_ENABLE = 0x1, JOB_OBJECT_CPU_RATE_CONTROL_HARD_CAP = 0x4;

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_CPU_RATE_CONTROL_INFORMATION
        {
            public uint ControlFlags;
            public uint CpuRate;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_BASIC_ACCOUNTING_INFORMATION
        {
            public long TotalUserTime;
            public long TotalKernelTime;
            public long ThisPeriodTotalUserTime;
            public long ThisPeriodTotalKernelTime;
            public uint TotalPageFaultCount;
            public uint TotalProcesses;
            public uint ActiveProcesses;
            public uint TotalTerminatedProcesses;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize;
            public UIntPtr MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IO_COUNTERS
        {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
            public IO_COUNTERS IoInfo;
            public UIntPtr ProcessMemoryLimit;
            public UIntPtr JobMemoryLimit;
            public UIntPtr PeakProcessMemoryUsed;
            public UIntPtr PeakJobMemoryUsed;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string? lpName);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetInformationJobObject(
            IntPtr hJob, int jobObjectInformationClass, ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION lpJobObjectInformation, uint cbJobObjectInformationLength);

        [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "SetInformationJobObject")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetInformationJobObjectCpu(
            IntPtr hJob, int jobObjectInformationClass, ref JOBOBJECT_CPU_RATE_CONTROL_INFORMATION info, uint length);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool QueryInformationJobObject(
            IntPtr hJob, int jobObjectInformationClass, out JOBOBJECT_EXTENDED_LIMIT_INFORMATION info, uint length, IntPtr returnLength);

        [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "QueryInformationJobObject")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool QueryInformationJobObjectBasic(
            IntPtr hJob, int jobObjectInformationClass, out JOBOBJECT_BASIC_ACCOUNTING_INFORMATION info, uint length, IntPtr returnLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr hObject);
    }
}

namespace ToolBelt.Windows
{
    /// <summary>Resource use of everything that has run in a <see cref="JobObject"/>.</summary>
    public sealed class JobAccounting
    {
        internal JobAccounting(TimeSpan user, TimeSpan kernel, int active, int total, int terminated, long peakProcess, long peakJob)
        {
            TotalUserTime = user;
            TotalKernelTime = kernel;
            ActiveProcesses = active;
            TotalProcesses = total;
            TerminatedProcesses = terminated;
            PeakProcessMemoryBytes = peakProcess;
            PeakJobMemoryBytes = peakJob;
        }

        public TimeSpan TotalUserTime { get; }
        public TimeSpan TotalKernelTime { get; }
        public TimeSpan TotalCpuTime => TotalUserTime + TotalKernelTime;
        public int ActiveProcesses { get; }
        public int TotalProcesses { get; }

        /// <summary>Processes ended because a job limit was exceeded.</summary>
        public int TerminatedProcesses { get; }

        /// <summary>Largest committed memory of any single process.</summary>
        public long PeakProcessMemoryBytes { get; }

        /// <summary>Largest committed memory of the whole job at once.</summary>
        public long PeakJobMemoryBytes { get; }
    }
}
