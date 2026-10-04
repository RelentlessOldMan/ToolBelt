using System;
using Microsoft.Win32;
using ToolBelt.Tests.Framework;
using ToolBelt.Windows;

namespace ToolBelt.Windows.Tests
{
    public sealed class RegistryUtilsTests
    {
        // All tests write under a throwaway HKCU subkey (no elevation needed) and delete it afterwards.
        private const RegistryHive Hive = RegistryHive.CurrentUser;

        private static string FreshSubKey() =>
            @"Software\ToolBelt.Windows.Tests\" + Guid.NewGuid().ToString("N");

        public void RoundTrip_StringAndInt()
        {
            string sub = FreshSubKey();
            try
            {
                RegistryUtils.SetValue(Hive, sub, "Name", "Toolbelt");
                RegistryUtils.SetValue(Hive, sub, "Count", 42);

                Check.Equal("Toolbelt", RegistryUtils.GetValue<string>(Hive, sub, "Name"));
                Check.Equal(42, RegistryUtils.GetValue<int>(Hive, sub, "Count"));
                // Convert.ChangeType path: read the int back as a string.
                Check.Equal("42", RegistryUtils.GetValue<string>(Hive, sub, "Count"));
            }
            finally { RegistryUtils.DeleteKey(Hive, sub); }
        }

        public void Missing_ReturnsDefault()
        {
            string sub = FreshSubKey();
            Check.False(RegistryUtils.KeyExists(Hive, sub), "fresh key absent");
            Check.Equal(-1, RegistryUtils.GetValue<int>(Hive, sub, "Nope", -1));
            Check.Null(RegistryUtils.GetValue(Hive, sub, "Nope"));
        }

        public void Enumerate_ValueAndSubKeyNames()
        {
            string sub = FreshSubKey();
            try
            {
                RegistryUtils.SetValue(Hive, sub, "A", 1);
                RegistryUtils.SetValue(Hive, sub, "B", 2);
                RegistryUtils.SetValue(Hive, sub + @"\Child", "X", "y");

                var values = RegistryUtils.GetValueNames(Hive, sub);
                Check.True(values.Count == 2, $"two values, got {values.Count}");

                var subs = RegistryUtils.GetSubKeyNames(Hive, sub);
                Check.True(subs.Count == 1 && subs[0] == "Child", "one child subkey named Child");
            }
            finally { RegistryUtils.DeleteKey(Hive, sub); }
        }

        public void Delete_ValueAndKey()
        {
            string sub = FreshSubKey();
            try
            {
                RegistryUtils.SetValue(Hive, sub, "Temp", 7);
                Check.True(RegistryUtils.DeleteValue(Hive, sub, "Temp"), "value deleted");
                Check.False(RegistryUtils.DeleteValue(Hive, sub, "Temp"), "second delete is a no-op false");
                Check.True(RegistryUtils.KeyExists(Hive, sub), "key still present");
            }
            finally { RegistryUtils.DeleteKey(Hive, sub); }

            Check.False(RegistryUtils.KeyExists(Hive, sub), "key gone after DeleteKey");
            Check.False(RegistryUtils.DeleteKey(Hive, sub), "deleting a missing key returns false");
        }

        public void Validation_Throws()
        {
            Check.Throws<ArgumentNullException>(() => RegistryUtils.GetValue(Hive, null!, "x"));
            Check.Throws<ArgumentNullException>(() => RegistryUtils.SetValue(Hive, "sub", "x", null!));
            Check.Throws<ArgumentNullException>(() => RegistryUtils.KeyExists(Hive, null!));
        }
    }
}
