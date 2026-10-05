// ToolBelt Windows drop-in — self-contained (net8.0-windows, P/Invoke only; no NuGet package needed).
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace ToolBelt.Windows
{
    /// <summary>Windows event types (what Event Viewer shows as Level).</summary>
    public enum EventLogEntryKind : ushort
    {
        Error = 0x0001,
        Warning = 0x0002,
        Information = 0x0004,
    }

    /// <summary>
    /// Writes to the Windows Event Log (Application log by default) through the Win32 <c>ReportEvent</c> API — no
    /// System.Diagnostics.EventLog package required. A source must be registered once (an admin operation, typically in
    /// the installer: <see cref="RegisterSource"/>) for Event Viewer to show messages cleanly; unregistered sources still
    /// write, but the viewer prefixes the text with "The description for Event ID … cannot be found". Messages longer
    /// than the API's 31 839-character limit are truncated with a marker. Thread-safe.
    /// </summary>
    public sealed class EventLogWriter : IDisposable
    {
        /// <summary>The longest string ReportEvent accepts.</summary>
        public const int MaxMessageLength = 31839;

        // The .NET Framework's generic "%1" message DLL — present on every Windows install, so registered sources render the text verbatim.
        private const string GenericMessageFile = @"%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\EventLogMessages.dll";

        private readonly object _gate = new object();
        private IntPtr _handle;

        public EventLogWriter(string source)
        {
            if (string.IsNullOrWhiteSpace(source)) throw new ArgumentException("Source is required.", nameof(source));
            Source = source;
            _handle = RegisterEventSourceW(null, source);
            if (_handle == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), $"Could not open event source '{source}'.");
        }

        public string Source { get; }

        public void Write(string message, EventLogEntryKind kind = EventLogEntryKind.Information, int eventId = 1000, short category = 0)
        {
            if (message is null) throw new ArgumentNullException(nameof(message));
            if (eventId < 0 || eventId > 0xFFFF) throw new ArgumentOutOfRangeException(nameof(eventId), eventId, "Event ID must be 0–65535.");
            string text = message.Length <= MaxMessageLength ? message : message.Substring(0, MaxMessageLength - 14) + " …[truncated]";
            lock (_gate)
            {
                if (_handle == IntPtr.Zero) throw new ObjectDisposedException(nameof(EventLogWriter));
                if (!ReportEventW(_handle, (ushort)kind, (ushort)category, (uint)eventId, IntPtr.Zero, 1, 0, new[] { text }, IntPtr.Zero))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "ReportEvent failed.");
            }
        }

        public void Error(string message, int eventId = 1000) => Write(message, EventLogEntryKind.Error, eventId);
        public void Warning(string message, int eventId = 1000) => Write(message, EventLogEntryKind.Warning, eventId);
        public void Information(string message, int eventId = 1000) => Write(message, EventLogEntryKind.Information, eventId);

        /// <summary>Whether <paramref name="source"/> is registered under <paramref name="log"/> (readable without admin rights).</summary>
        public static bool IsSourceRegistered(string source, string log = "Application")
        {
            if (string.IsNullOrWhiteSpace(source)) throw new ArgumentException("Source is required.", nameof(source));
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\EventLog\{log}\{source}");
            return key != null;
        }

        /// <summary>
        /// Registers <paramref name="source"/> under <paramref name="log"/> with the generic message file so text displays
        /// verbatim. Requires administrator rights (throws <see cref="UnauthorizedAccessException"/> otherwise). Idempotent.
        /// </summary>
        public static void RegisterSource(string source, string log = "Application")
        {
            if (string.IsNullOrWhiteSpace(source)) throw new ArgumentException("Source is required.", nameof(source));
            using RegistryKey key = Registry.LocalMachine.CreateSubKey($@"SYSTEM\CurrentControlSet\Services\EventLog\{log}\{source}", writable: true)
                ?? throw new UnauthorizedAccessException("Could not create the event source registry key.");
            key.SetValue("EventMessageFile", GenericMessageFile, RegistryValueKind.ExpandString);
            key.SetValue("TypesSupported", 7, RegistryValueKind.DWord);
        }

        /// <summary>Removes a registration made by <see cref="RegisterSource"/> (administrator rights required).</summary>
        public static void UnregisterSource(string source, string log = "Application")
        {
            if (string.IsNullOrWhiteSpace(source)) throw new ArgumentException("Source is required.", nameof(source));
            Registry.LocalMachine.DeleteSubKeyTree($@"SYSTEM\CurrentControlSet\Services\EventLog\{log}\{source}", throwOnMissingSubKey: false);
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_handle == IntPtr.Zero) return;
                DeregisterEventSource(_handle);
                _handle = IntPtr.Zero;
            }
        }

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr RegisterEventSourceW(string? lpUNCServerName, string lpSourceName);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool DeregisterEventSource(IntPtr hEventLog);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool ReportEventW(IntPtr hEventLog, ushort wType, ushort wCategory, uint dwEventID, IntPtr lpUserSid,
            ushort wNumStrings, uint dwDataSize, string[] lpStrings, IntPtr lpRawData);
    }
}
