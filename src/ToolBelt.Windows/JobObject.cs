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
    /// Assign a process with <see cref="AssignProcess(System.Diagnostics.Process)"/> right after starting it.
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

        private const int JobObjectExtendedLimitInformation = 9;
        private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;

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

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr hObject);
    }
}
