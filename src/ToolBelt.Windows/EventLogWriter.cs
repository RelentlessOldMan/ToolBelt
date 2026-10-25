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

        // The .NET Framework's generic "%1" message DLL — present on every Windows install, so registered sources render the
        // text verbatim. Framework64 exists only on 64-bit Windows; a 32-bit install has just Framework.
        private static string GenericMessageFile => Environment.Is64BitOperatingSystem
            ? @"%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\EventLogMessages.dll"
            : @"%SystemRoot%\Microsoft.NET\Framework\v4.0.30319\EventLogMessages.dll";

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
            string text = message;
            if (message.Length > MaxMessageLength)
            {
                int cut = MaxMessageLength - 14;
                if (char.IsHighSurrogate(message[cut - 1])) cut--;               // don't split a surrogate pair
                text = message.Substring(0, cut) + " …[truncated]";
            }
            lock (_gate)
            {
                if (_handle == IntPtr.Zero) throw new ObjectDisposedException(nameof(EventLogWriter));
                if (!ReportEventW(_handle, (ushort)kind, (ushort)category, (uint)eventId, IntPtr.Zero, 1, 0, new[] { text }, IntPtr.Zero))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "ReportEvent failed.");
                // The lock is on _gate, not this: without this, the finalizer could close the handle mid-call once the
                // writer was otherwise unreachable.
                GC.KeepAlive(this);
            }
        }

        public void Error(string message, int eventId = 1000) => Write(message, EventLogEntryKind.Error, eventId);
        public void Warning(string message, int eventId = 1000) => Write(message, EventLogEntryKind.Warning, eventId);
        public void Information(string message, int eventId = 1000) => Write(message, EventLogEntryKind.Information, eventId);

        /// <summary>Whether <paramref name="source"/> is registered under <paramref name="log"/> (readable without admin rights).</summary>
        public static bool IsSourceRegistered(string source, string log = "Application")
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(SourceKey(source, log));
            return key != null;
        }

        /// <summary>
        /// Registers <paramref name="source"/> under <paramref name="log"/> with the generic message file so text displays
        /// verbatim. Requires administrator rights (throws <see cref="UnauthorizedAccessException"/> otherwise). Idempotent.
        /// </summary>
        public static void RegisterSource(string source, string log = "Application")
        {
            using RegistryKey key = Registry.LocalMachine.CreateSubKey(SourceKey(source, log), writable: true)
                ?? throw new UnauthorizedAccessException("Could not create the event source registry key.");
            key.SetValue("EventMessageFile", GenericMessageFile, RegistryValueKind.ExpandString);
            key.SetValue("TypesSupported", 7, RegistryValueKind.DWord);
        }

        /// <summary>Removes a registration made by <see cref="RegisterSource"/> (administrator rights required).</summary>
        public static void UnregisterSource(string source, string log = "Application")
        {
            Registry.LocalMachine.DeleteSubKeyTree(SourceKey(source, log), throwOnMissingSubKey: false);
        }

        // Both names are single registry key names: an empty log or a backslash would address some other key
        // (UnregisterSource("Application", log: "") deleted the whole Application log).
        private static string SourceKey(string source, string log)
        {
            if (string.IsNullOrWhiteSpace(source) || source.IndexOf('\\') >= 0)
                throw new ArgumentException("Source is required and may not contain a backslash.", nameof(source));
            if (string.IsNullOrWhiteSpace(log) || log.IndexOf('\\') >= 0)
                throw new ArgumentException("Log is required and may not contain a backslash.", nameof(log));
            return $@"SYSTEM\CurrentControlSet\Services\EventLog\{log}\{source}";
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_handle == IntPtr.Zero) return;
                DeregisterEventSource(_handle);
                _handle = IntPtr.Zero;
            }
            GC.SuppressFinalize(this);
        }

        ~EventLogWriter()
        {
            if (_handle != IntPtr.Zero) DeregisterEventSource(_handle);
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
