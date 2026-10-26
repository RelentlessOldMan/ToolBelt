// ToolBelt.Windows drop-in — Windows-only (net8.0-windows), self-contained (Microsoft.Win32 registry).
using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Win32;

namespace ToolBelt.Windows
{
    /// <summary>
    /// Convenience read/write/delete helpers over the Windows registry, taking a <see cref="RegistryHive"/>
    /// plus a subkey path so callers don't juggle base keys and <c>IDisposable</c> handles. Reads return a
    /// default when the key or value is missing; writes create the subkey as needed. Uses the default
    /// registry view (the process's native 32/64-bit view).
    /// </summary>
    public static class RegistryUtils
    {
        /// <summary>
        /// Reads a value, converting it to <typeparamref name="T"/> (strings parse culture-invariantly; a DWORD or QWORD
        /// converts as the unsigned number it is, so DWORD 0x80000000 reads as 2147483648 into <c>uint</c>, <c>long</c> or
        /// <c>double</c>; <c>int</c> for a DWORD and <c>long</c> for a QWORD still read the stored bits as they are).
        /// Nullable and enum targets work (an enum from its name or number). Returns
        /// <paramref name="defaultValue"/> if absent, not convertible, or the key cannot be read.
        /// </summary>
        public static T? GetValue<T>(RegistryHive hive, string subKey, string name, T? defaultValue = default)
        {
            object? raw = GetValue(hive, subKey, name, null);
            if (raw is null) return defaultValue;
            if (raw is T typed) return typed;
            Type target = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
            try
            {
                if (target.IsEnum && (raw is int || raw is long))
                {
                    // An enum as wide as the stored value takes its bits as they are: widened first, DWORD 0x80000001 overflowed
                    // an int-backed enum and read as the default.
                    Type underlying = Enum.GetUnderlyingType(target);
                    if ((raw is int && (underlying == typeof(int) || underlying == typeof(uint)))
                        || (raw is long && (underlying == typeof(long) || underlying == typeof(ulong))))
                        return (T)Enum.ToObject(target, raw);
                }
                // .NET returns REG_DWORD as int and REG_QWORD as long. They are unsigned: widen them first, or the high bit
                // read as negative and failed (or went negative) for every wider or floating-point target.
                if (raw is int dword) raw = (long)unchecked((uint)dword);
                else if (raw is long qword && qword < 0) raw = unchecked((ulong)qword);
                if (target.IsEnum)
                {
                    object value = raw is string text
                        ? Enum.Parse(target, text.Trim(), ignoreCase: true)
                        : Enum.ToObject(target, Convert.ChangeType(raw, Enum.GetUnderlyingType(target), CultureInfo.InvariantCulture)!);
                    return (T)value;
                }
                // ChangeType rejects Nullable<T> targets, so convert to the underlying type (boxing it is a boxed T?).
                return (T)Convert.ChangeType(raw, target, CultureInfo.InvariantCulture);
            }
            catch (Exception ex) when (ex is InvalidCastException || ex is FormatException || ex is OverflowException || ex is ArgumentException)
            {
                return defaultValue;
            }
        }

        /// <summary>Reads a raw value, or <paramref name="defaultValue"/> if the key/value is absent or the key cannot be read.</summary>
        public static object? GetValue(RegistryHive hive, string subKey, string name, object? defaultValue = null)
        {
            if (subKey is null) throw new ArgumentNullException(nameof(subKey));
            if (name is null) throw new ArgumentNullException(nameof(name));
            using RegistryKey baseKey = OpenBase(hive);
            using RegistryKey? key = OpenForRead(baseKey, subKey, out _);
            return key?.GetValue(name, defaultValue) ?? defaultValue;
        }

        // Opens a subkey for reading; a key that exists but this account may not read (HKLM\SECURITY, other users' hives)
        // threw SecurityException out of every read helper. It reads as null, with denied telling the two cases apart. A
        // path no key can have (a segment over 255 characters) reads as absent too: it threw naming "name".
        private static RegistryKey? OpenForRead(RegistryKey baseKey, string subKey, out bool denied)
        {
            denied = false;
            try { return baseKey.OpenSubKey(subKey, writable: false); }
            catch (Exception ex) when (ex is System.Security.SecurityException || ex is UnauthorizedAccessException)
            {
                denied = true;
                return null;
            }
            catch (ArgumentException) { return null; }
        }

        // An undefined hive threw IndexOutOfRangeException from inside RegistryKey.
        private static RegistryKey OpenBase(RegistryHive hive)
        {
            if (!Enum.IsDefined(typeof(RegistryHive), hive)) throw new ArgumentOutOfRangeException(nameof(hive), hive, "Unknown registry hive.");
            return RegistryKey.OpenBaseKey(hive, RegistryView.Default);
        }

        // For writes and deletes: an over-long segment threw ArgumentException naming "name", the value-name parameter.
        private static T ForSubKey<T>(string subKey, Func<T> f)
        {
            try { return f(); }
            catch (ArgumentException ex) when (ex.ParamName != nameof(subKey) && !(ex is ArgumentNullException))
            {
                throw new ArgumentException($"'{subKey}' is not a valid registry key path: {ex.Message}", nameof(subKey), ex);
            }
        }

        /// <summary>
        /// Writes a value, creating the subkey if necessary. A <c>uint</c> or <c>ulong</c> is stored as the DWORD or QWORD
        /// with the same bits (when <paramref name="kind"/> is <see cref="RegistryValueKind.Unknown"/> or that kind), so it
        /// reads back through <see cref="GetValue{T}"/> unchanged.
        /// </summary>
        public static void SetValue(RegistryHive hive, string subKey, string name, object value,
            RegistryValueKind kind = RegistryValueKind.Unknown)
        {
            if (subKey is null) throw new ArgumentNullException(nameof(subKey));
            if (name is null) throw new ArgumentNullException(nameof(name));
            if (value is null) throw new ArgumentNullException(nameof(value));
            // RegistryKey range-checks a DWORD against int and a QWORD against long, and stored an unknown-kind uint as text:
            // 0x80000000u threw, or became REG_SZ "2147483648", although GetValue reads DWORDs as unsigned.
            if (value is uint u && (kind == RegistryValueKind.Unknown || kind == RegistryValueKind.DWord))
            { value = unchecked((int)u); kind = RegistryValueKind.DWord; }
            else if (value is ulong ul && (kind == RegistryValueKind.Unknown || kind == RegistryValueKind.QWord))
            { value = unchecked((long)ul); kind = RegistryValueKind.QWord; }
            using RegistryKey baseKey = OpenBase(hive);
            using RegistryKey key = ForSubKey(subKey, () => baseKey.CreateSubKey(subKey, writable: true))
                ?? throw new InvalidOperationException($"Could not open or create subkey '{subKey}'.");
            if (kind == RegistryValueKind.Unknown)
                key.SetValue(name, value);
            else
                key.SetValue(name, value, kind);
        }

        /// <summary>Deletes a single value. Returns false if the key or value did not exist.</summary>
        public static bool DeleteValue(RegistryHive hive, string subKey, string name)
        {
            if (subKey is null) throw new ArgumentNullException(nameof(subKey));
            if (name is null) throw new ArgumentNullException(nameof(name));
            using RegistryKey baseKey = OpenBase(hive);
            using RegistryKey? key = ForSubKey(subKey, () => baseKey.OpenSubKey(subKey, writable: true));
            if (key is null) return false;
            // Not GetValue(name) is null: that is also null for a kind .NET does not read (REG_RESOURCE_LIST, REG_LINK),
            // which was then reported missing and left in place.
            try { key.DeleteValue(name, throwOnMissingValue: true); }
            catch (ArgumentException) { return false; }
            return true;
        }

        /// <summary>
        /// Deletes a subkey and everything under it. Returns false if it did not exist. A path naming the hive itself
        /// (empty, or only backslashes) is rejected.
        /// </summary>
        public static bool DeleteKey(RegistryHive hive, string subKey)
        {
            if (subKey is null) throw new ArgumentNullException(nameof(subKey));
            // RegistryKey refuses "" but not "\": its hive guard runs before it trims the backslashes, so DeleteKey(hive, @"\")
            // deleted everything in the hive.
            if (subKey.Trim('\\').Length == 0) throw new ArgumentException("The subkey must name a key below the hive.", nameof(subKey));
            using RegistryKey baseKey = OpenBase(hive);
            using (RegistryKey? existing = ForSubKey(subKey, () => baseKey.OpenSubKey(subKey)))
                if (existing is null) return false;
            baseKey.DeleteSubKeyTree(subKey, throwOnMissingSubKey: false);
            return true;
        }

        /// <summary>True if the subkey exists (including one this account may not read).</summary>
        public static bool KeyExists(RegistryHive hive, string subKey)
        {
            if (subKey is null) throw new ArgumentNullException(nameof(subKey));
            using RegistryKey baseKey = OpenBase(hive);
            using RegistryKey? key = OpenForRead(baseKey, subKey, out bool denied);
            return key is not null || denied;
        }

        /// <summary>The value names directly under the subkey (empty if the key is absent or cannot be read).</summary>
        public static IReadOnlyList<string> GetValueNames(RegistryHive hive, string subKey)
        {
            if (subKey is null) throw new ArgumentNullException(nameof(subKey));
            using RegistryKey baseKey = OpenBase(hive);
            using RegistryKey? key = OpenForRead(baseKey, subKey, out _);
            return key is null ? Array.Empty<string>() : key.GetValueNames();
        }

        /// <summary>The immediate child subkey names (empty if the key is absent or cannot be read).</summary>
        public static IReadOnlyList<string> GetSubKeyNames(RegistryHive hive, string subKey)
        {
            if (subKey is null) throw new ArgumentNullException(nameof(subKey));
            using RegistryKey baseKey = OpenBase(hive);
            using RegistryKey? key = OpenForRead(baseKey, subKey, out _);
            return key is null ? Array.Empty<string>() : key.GetSubKeyNames();
        }
    }
}
