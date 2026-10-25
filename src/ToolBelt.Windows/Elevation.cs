// ToolBelt.Windows drop-in — Windows-only (net8.0-windows), self-contained (Win32 token APIs).
using System;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace ToolBelt.Windows
{
    /// <summary>The Windows mandatory integrity level of a process token.</summary>
    public enum IntegrityLevel
    {
        Unknown = 0,
        Untrusted,
        Low,
        Medium,
        High,
        System,
    }

    /// <summary>
    /// Reports the current process's elevation and integrity level. <see cref="IsElevated"/> checks the
    /// token's elevation bit (true inside a UAC-elevated process); <see cref="IsAdministrator"/> checks
    /// whether the Administrators group is active in the token; <see cref="CurrentIntegrityLevel"/> returns
    /// the mandatory integrity level. Always the process token, even on a thread that is impersonating someone else.
    /// Does not launch or request elevation — it only observes.
    /// </summary>
    public static class Elevation
    {
        /// <summary>True if the current process token is elevated.</summary>
        public static bool IsElevated()
        {
            IntPtr buffer = GetTokenInfo(CurrentProcessToken, TokenElevation, out int size);
            try
            {
                // TOKEN_ELEVATION is a single DWORD (TokenIsElevated).
                return Marshal.ReadInt32(buffer) != 0;
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }

        /// <summary>True if the process is in the built-in Administrators role (full admin rights active).</summary>
        public static bool IsAdministrator()
        {
            // The enabled, non-deny-only groups of the process token: what IsInRole checked, but of the thread's token,
            // so impersonating another account (or the anonymous token) answered for that account.
            var administrators = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
            IntPtr buffer = GetTokenInfo(CurrentProcessToken, TokenGroups, out _);
            try
            {
                // TOKEN_GROUPS { DWORD GroupCount; SID_AND_ATTRIBUTES Groups[]; }, the array pointer-aligned;
                // SID_AND_ATTRIBUTES { PSID Sid; DWORD Attributes; } padded to two pointers.
                int count = Marshal.ReadInt32(buffer);
                for (int i = 0; i < count; i++)
                {
                    IntPtr entry = buffer + IntPtr.Size + i * 2 * IntPtr.Size;
                    int attributes = Marshal.ReadInt32(entry, IntPtr.Size);
                    if ((attributes & SE_GROUP_ENABLED) == 0 || (attributes & SE_GROUP_USE_FOR_DENY_ONLY) != 0) continue;
                    if (new SecurityIdentifier(Marshal.ReadIntPtr(entry)) == administrators) return true;
                }
                return false;
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }

        /// <summary>The mandatory integrity level of the current process token.</summary>
        public static IntegrityLevel CurrentIntegrityLevel()
        {
            IntPtr buffer = GetTokenInfo(CurrentProcessToken, TokenIntegrityLevel, out int size);
            try
            {
                // TOKEN_MANDATORY_LABEL { SID_AND_ATTRIBUTES Label; } — Label.Sid is the first pointer.
                IntPtr sid = Marshal.ReadIntPtr(buffer); // Label.Sid
                int subAuthorityCount = Marshal.ReadByte(GetSidSubAuthorityCount(sid));
                IntPtr ridPtr = GetSidSubAuthority(sid, subAuthorityCount - 1);
                int rid = Marshal.ReadInt32(ridPtr);
                return Classify(rid);
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }

        private static IntegrityLevel Classify(int rid)
        {
            if (rid >= SECURITY_MANDATORY_SYSTEM_RID) return IntegrityLevel.System;
            if (rid >= SECURITY_MANDATORY_HIGH_RID) return IntegrityLevel.High;
            if (rid >= SECURITY_MANDATORY_MEDIUM_RID) return IntegrityLevel.Medium;
            if (rid >= SECURITY_MANDATORY_LOW_RID) return IntegrityLevel.Low;
            if (rid >= SECURITY_MANDATORY_UNTRUSTED_RID) return IntegrityLevel.Untrusted;
            return IntegrityLevel.Unknown;
        }

        private static IntPtr GetTokenInfo(IntPtr token, int infoClass, out int size)
        {
            size = 0;
            GetTokenInformation(token, infoClass, IntPtr.Zero, 0, out int needed);
            IntPtr buffer = Marshal.AllocHGlobal(needed);
            if (!GetTokenInformation(token, infoClass, buffer, needed, out size))
            {
                int err = Marshal.GetLastWin32Error();
                Marshal.FreeHGlobal(buffer);
                throw new System.ComponentModel.Win32Exception(err, "GetTokenInformation failed.");
            }
            return buffer;
        }

        // GetCurrentProcessToken(): a pseudo-handle for the process token with TOKEN_QUERY access, needing no open (an
        // impersonating thread may not be allowed to open its own process token). WindowsIdentity.GetCurrent read the
        // thread token instead whenever the thread was impersonating.
        private static readonly IntPtr CurrentProcessToken = new IntPtr(-4);

        private const int TokenGroups = 2;
        private const int SE_GROUP_ENABLED = 0x4, SE_GROUP_USE_FOR_DENY_ONLY = 0x10;
        private const int TokenElevation = 20;
        private const int TokenIntegrityLevel = 25;
        private const int SECURITY_MANDATORY_UNTRUSTED_RID = 0x00000000;
        private const int SECURITY_MANDATORY_LOW_RID = 0x00001000;
        private const int SECURITY_MANDATORY_MEDIUM_RID = 0x00002000;
        private const int SECURITY_MANDATORY_HIGH_RID = 0x00003000;
        private const int SECURITY_MANDATORY_SYSTEM_RID = 0x00004000;

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetTokenInformation(
            IntPtr tokenHandle, int tokenInformationClass, IntPtr tokenInformation, int tokenInformationLength, out int returnLength);

        [DllImport("advapi32.dll")]
        private static extern IntPtr GetSidSubAuthority(IntPtr sid, int index);

        [DllImport("advapi32.dll")]
        private static extern IntPtr GetSidSubAuthorityCount(IntPtr sid);
    }
}
