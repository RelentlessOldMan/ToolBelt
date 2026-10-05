// ToolBelt.Windows drop-in — Windows-only (net8.0-windows), self-contained (advapi32 token privileges).
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace ToolBelt.Windows
{
    /// <summary>
    /// Temporarily enables a token privilege — <c>SeBackupPrivilege</c> to read any file, <c>SeShutdownPrivilege</c> to
    /// reboot, <c>SeDebugPrivilege</c> to open other users' processes — and restores the previous state on dispose:
    /// <c>using (PrivilegeScope.Enable("SeBackupPrivilege")) { … }</c>. A privilege can only be enabled if the account
    /// holds it (an elevated administrator holds most); otherwise <see cref="Enable"/> throws
    /// <see cref="UnauthorizedAccessException"/> and nothing changes. Privileges live on the <em>process</em> token, so
    /// the change is process-wide while the scope is open — keep scopes short and don't overlap them from different
    /// threads for the same privilege.
    /// </summary>
    public sealed class PrivilegeScope : IDisposable
    {
        private readonly LUID _luid;
        private readonly bool _wasEnabled;
        private bool _disposed;

        private PrivilegeScope(string name, LUID luid, bool wasEnabled)
        {
            Name = name;
            _luid = luid;
            _wasEnabled = wasEnabled;
        }

        /// <summary>The privilege name, e.g. "SeShutdownPrivilege".</summary>
        public string Name { get; }

        /// <summary>True if the privilege was already enabled (so disposing leaves it enabled).</summary>
        public bool WasAlreadyEnabled => _wasEnabled;

        /// <summary>Enables <paramref name="privilegeName"/> on the process token until disposed.</summary>
        public static PrivilegeScope Enable(string privilegeName)
        {
            if (string.IsNullOrWhiteSpace(privilegeName)) throw new ArgumentException("Privilege name is required.", nameof(privilegeName));
            LUID luid = Lookup(privilegeName);
            using var token = OpenToken(TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY);
            var state = State(token.Handle, luid);
            if (state == null) throw new UnauthorizedAccessException($"The account does not hold {privilegeName}.");
            if (!state.Value) Adjust(token.Handle, luid, enable: true, privilegeName);
            return new PrivilegeScope(privilegeName, luid, state.Value);
        }

        /// <summary>True if the process token holds <paramref name="privilegeName"/> (enabled or not).</summary>
        public static bool IsHeld(string privilegeName) => Query(privilegeName) != null;

        /// <summary>True if the privilege is held and currently enabled.</summary>
        public static bool IsEnabled(string privilegeName) => Query(privilegeName) == true;

        /// <summary>Every privilege on the process token with whether it is enabled.</summary>
        public static IReadOnlyList<(string Name, bool Enabled)> List()
        {
            using var token = OpenToken(TOKEN_QUERY);
            var result = new List<(string, bool)>();
            foreach (var (luid, attrs) in ReadPrivileges(token.Handle))
            {
                var name = new System.Text.StringBuilder(64);
                int len = name.Capacity;
                LUID copy = luid;
                if (LookupPrivilegeName(null, ref copy, name, ref len)) result.Add((name.ToString(), (attrs & SE_PRIVILEGE_ENABLED) != 0));
            }
            return result;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_wasEnabled) return;
            using var token = OpenToken(TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY);
            Adjust(token.Handle, _luid, enable: false, Name);
        }

        private static bool? Query(string privilegeName)
        {
            if (string.IsNullOrWhiteSpace(privilegeName)) throw new ArgumentException("Privilege name is required.", nameof(privilegeName));
            LUID luid = Lookup(privilegeName);
            using var token = OpenToken(TOKEN_QUERY);
            return State(token.Handle, luid);
        }

        // null = not held; otherwise whether enabled.
        private static bool? State(IntPtr token, LUID luid)
        {
            foreach (var (l, attrs) in ReadPrivileges(token))
                if (l.LowPart == luid.LowPart && l.HighPart == luid.HighPart) return (attrs & SE_PRIVILEGE_ENABLED) != 0;
            return null;
        }

        private static IEnumerable<(LUID Luid, uint Attributes)> ReadPrivileges(IntPtr token)
        {
            GetTokenInformation(token, TokenPrivileges, IntPtr.Zero, 0, out int needed);
            IntPtr buffer = Marshal.AllocHGlobal(needed);
            try
            {
                if (!GetTokenInformation(token, TokenPrivileges, buffer, needed, out _))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "GetTokenInformation failed.");
                int count = Marshal.ReadInt32(buffer);
                var list = new List<(LUID, uint)>(count);
                int size = Marshal.SizeOf<LUID_AND_ATTRIBUTES>();
                for (int i = 0; i < count; i++)
                {
                    var la = Marshal.PtrToStructure<LUID_AND_ATTRIBUTES>(buffer + 4 + i * size);
                    list.Add((la.Luid, la.Attributes));
                }
                return list;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        private static void Adjust(IntPtr token, LUID luid, bool enable, string name)
        {
            var tp = new TOKEN_PRIVILEGES { PrivilegeCount = 1, Luid = luid, Attributes = enable ? SE_PRIVILEGE_ENABLED : 0 };
            if (!AdjustTokenPrivileges(token, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"AdjustTokenPrivileges failed for {name}.");
            int err = Marshal.GetLastWin32Error();
            if (err == ERROR_NOT_ALL_ASSIGNED) throw new UnauthorizedAccessException($"The account does not hold {name}.");
        }

        private static LUID Lookup(string name)
        {
            if (!LookupPrivilegeValue(null, name, out LUID luid))
                throw new ArgumentException($"Unknown privilege '{name}'.", nameof(name), new Win32Exception(Marshal.GetLastWin32Error()));
            return luid;
        }

        private sealed class TokenHandle : IDisposable
        {
            public IntPtr Handle;
            public void Dispose() { if (Handle != IntPtr.Zero) CloseHandle(Handle); Handle = IntPtr.Zero; }
        }

        private static TokenHandle OpenToken(uint access)
        {
            if (!OpenProcessToken(GetCurrentProcess(), access, out IntPtr h))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "OpenProcessToken failed.");
            return new TokenHandle { Handle = h };
        }

        private const uint TOKEN_ADJUST_PRIVILEGES = 0x0020, TOKEN_QUERY = 0x0008, SE_PRIVILEGE_ENABLED = 0x2;
        private const int TokenPrivileges = 3, ERROR_NOT_ALL_ASSIGNED = 1300;

        [StructLayout(LayoutKind.Sequential)]
        private struct LUID { public uint LowPart; public int HighPart; }

        [StructLayout(LayoutKind.Sequential)]
        private struct LUID_AND_ATTRIBUTES { public LUID Luid; public uint Attributes; }

        [StructLayout(LayoutKind.Sequential)]
        private struct TOKEN_PRIVILEGES { public uint PrivilegeCount; public LUID Luid; public uint Attributes; }

        [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CloseHandle(IntPtr h);
        [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool LookupPrivilegeValue(string? system, string name, out LUID luid);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool LookupPrivilegeName(string? system, ref LUID luid, System.Text.StringBuilder name, ref int length);
        [DllImport("advapi32.dll", SetLastError = true)] private static extern bool GetTokenInformation(IntPtr token, int cls, IntPtr info, int length, out int returned);
        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool AdjustTokenPrivileges(IntPtr token, bool disableAll, ref TOKEN_PRIVILEGES newState, int bufferLength, IntPtr previous, IntPtr returnLength);
    }
}
