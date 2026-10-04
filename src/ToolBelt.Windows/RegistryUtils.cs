// ToolBelt.Windows drop-in — Windows-only (net8.0-windows), self-contained (Microsoft.Win32 registry).
using System;
using System.Collections.Generic;
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
        /// <summary>Reads a value, converting it to <typeparamref name="T"/>; returns <paramref name="defaultValue"/> if absent.</summary>
        public static T? GetValue<T>(RegistryHive hive, string subKey, string name, T? defaultValue = default)
        {
            object? raw = GetValue(hive, subKey, name, null);
            if (raw is null) return defaultValue;
            if (raw is T typed) return typed;
            try
            {
                return (T)Convert.ChangeType(raw, typeof(T));
            }
            catch (Exception ex) when (ex is InvalidCastException || ex is FormatException || ex is OverflowException)
            {
                return defaultValue;
            }
        }

        /// <summary>Reads a raw value, or <paramref name="defaultValue"/> if the key/value is absent.</summary>
        public static object? GetValue(RegistryHive hive, string subKey, string name, object? defaultValue = null)
        {
            if (subKey is null) throw new ArgumentNullException(nameof(subKey));
            if (name is null) throw new ArgumentNullException(nameof(name));
            using RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
            using RegistryKey? key = baseKey.OpenSubKey(subKey, writable: false);
            return key?.GetValue(name, defaultValue) ?? defaultValue;
        }

        /// <summary>Writes a value, creating the subkey if necessary.</summary>
        public static void SetValue(RegistryHive hive, string subKey, string name, object value,
            RegistryValueKind kind = RegistryValueKind.Unknown)
        {
            if (subKey is null) throw new ArgumentNullException(nameof(subKey));
            if (name is null) throw new ArgumentNullException(nameof(name));
            if (value is null) throw new ArgumentNullException(nameof(value));
            using RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
            using RegistryKey key = baseKey.CreateSubKey(subKey, writable: true)
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
            using RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
            using RegistryKey? key = baseKey.OpenSubKey(subKey, writable: true);
            if (key is null) return false;
            if (key.GetValue(name) is null) return false;
            key.DeleteValue(name, throwOnMissingValue: false);
            return true;
        }

        /// <summary>Deletes a subkey and everything under it. Returns false if it did not exist.</summary>
        public static bool DeleteKey(RegistryHive hive, string subKey)
        {
            if (subKey is null) throw new ArgumentNullException(nameof(subKey));
            using RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
            if (baseKey.OpenSubKey(subKey) is null) return false;
            baseKey.DeleteSubKeyTree(subKey, throwOnMissingSubKey: false);
            return true;
        }

        /// <summary>True if the subkey exists.</summary>
        public static bool KeyExists(RegistryHive hive, string subKey)
        {
            if (subKey is null) throw new ArgumentNullException(nameof(subKey));
            using RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
            using RegistryKey? key = baseKey.OpenSubKey(subKey, writable: false);
            return key is not null;
        }

        /// <summary>The value names directly under the subkey (empty if the key is absent).</summary>
        public static IReadOnlyList<string> GetValueNames(RegistryHive hive, string subKey)
        {
            if (subKey is null) throw new ArgumentNullException(nameof(subKey));
            using RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
            using RegistryKey? key = baseKey.OpenSubKey(subKey, writable: false);
            return key is null ? Array.Empty<string>() : key.GetValueNames();
        }

        /// <summary>The immediate child subkey names (empty if the key is absent).</summary>
        public static IReadOnlyList<string> GetSubKeyNames(RegistryHive hive, string subKey)
        {
            if (subKey is null) throw new ArgumentNullException(nameof(subKey));
            using RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
            using RegistryKey? key = baseKey.OpenSubKey(subKey, writable: false);
            return key is null ? Array.Empty<string>() : key.GetSubKeyNames();
        }
    }
}
