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
    /// than the API's 31 839-character limit are truncated with a marker; a NUL in a message is written as U+2400 (the
    /// API ends the text at a NUL). Source and log names must not contain a NUL or a backslash. Thread-safe.
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
            CheckName(source, nameof(source));
            Source = source;
            _handle = RegisterEventSourceW(null, source);
            if (_handle == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), $"Could not open event source '{source}'.");
        }

        public string Source { get; }

        public void Write(string message, EventLogEntryKind kind = EventLogEntryKind.Information, int eventId = 1000, short category = 0)
        {
            if (message is null) throw new ArgumentNullException(nameof(message));
            if (eventId < 0 || eventId > 0xFFFF) throw new ArgumentOutOfRangeException(nameof(eventId), eventId, "Event ID must be 0–65535.");
            if (kind != EventLogEntryKind.Error && kind != EventLogEntryKind.Warning && kind != EventLogEntryKind.Information)
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown entry kind.");
            // A negative category became 65535 and up (and overflowed in a checked build).
            if (category < 0) throw new ArgumentOutOfRangeException(nameof(category), category, "Category must not be negative.");
            string text = EventText(message);
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

        // What ReportEvent is given for a message.
        internal static string EventText(string message)
        {
            // The API ends the text at a NUL, so everything after one was lost without a word.
            string text = message.IndexOf('\0') >= 0 ? message.Replace('\0', (char)0x2400) : message;
            if (text.Length > MaxMessageLength)
            {
                const string marker = " …[truncated]";
                int cut = MaxMessageLength - marker.Length;                       // a hand count of 14 lost a character
                if (char.IsHighSurrogate(text[cut - 1])) cut--;                  // don't split a surrogate pair
                text = text.Substring(0, cut) + marker;
            }
            return text;
        }

        public void Error(string message, int eventId = 1000) => Write(message, EventLogEntryKind.Error, eventId);
        public void Warning(string message, int eventId = 1000) => Write(message, EventLogEntryKind.Warning, eventId);
        public void Information(string message, int eventId = 1000) => Write(message, EventLogEntryKind.Information, eventId);

        /// <summary>Whether <paramref name="source"/> is registered under <paramref name="log"/> (readable without admin rights).</summary>
        public static bool IsSourceRegistered(string source, string log = "Application")
        {
            string path = SourceKey(source, log);
            if (source.Length > MaxKeyNameLength || log.Length > MaxKeyNameLength) return false;   // no such key can exist
            // A source under the Security log is readable only by administrators: the key is there, so it is registered
            // (a missing key is reported as absent, not denied). This threw SecurityException without admin rights.
            try
            {
                using RegistryKey? key = Registry.LocalMachine.OpenSubKey(path);
                return key != null;
            }
            catch (Exception ex) when (ex is System.Security.SecurityException || ex is UnauthorizedAccessException)
            {
                return true;
            }
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
            CheckName(source, nameof(source));
            CheckName(log, nameof(log));
            return $@"SYSTEM\CurrentControlSet\Services\EventLog\{log}\{source}";
        }

        // A null threw ArgumentException rather than ArgumentNullException. The registry and the event API end a name at a
        // NUL: Application + NUL + "junk" opened the Application source, and UnregisterSource would delete a real one.
        private static void CheckName(string name, string paramName)
        {
            if (name is null) throw new ArgumentNullException(paramName);
            if (string.IsNullOrWhiteSpace(name) || name.IndexOf('\\') >= 0 || name.IndexOf('\0') >= 0)
                throw new ArgumentException("A name is required and may not contain a backslash or a NUL.", paramName);
        }

        private const int MaxKeyNameLength = 255;   // a registry key name's limit

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
