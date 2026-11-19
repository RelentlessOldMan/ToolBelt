using System;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using ToolBelt.Tests.Framework;
using ToolBelt.Windows;

namespace ToolBelt.Windows.Tests
{
    // Review round 144 (Windows satellite, pass 9) regressions.
    public sealed class WindowsReview144Tests
    {
        private const RegistryHive Hive = RegistryHive.CurrentUser;

        private static string NewKey() => @"Software\ToolBelt.Windows.Tests\R144." + Guid.NewGuid().ToString("N");

        private static void Cleanup(string sub)
        {
            try { RegistryUtils.DeleteKey(Hive, sub); } catch (Exception) { }
        }

        private enum Small : short { Neg = -1, A = 1 }
        private enum Mid : int { Neg = -1, A = 1 }

        // ---------- RegistryUtils: a negative number stored by its bits reads back ----------

        // GetValue widened every DWORD as unsigned (and a negative QWORD to ulong) before converting, so a negative short,
        // sbyte or short-backed enum (or an int as a QWORD) overflowed and read back as the default.
        public void Registry_NegativeNarrowNumbersReadBack()
        {
            string sub = NewKey();
            try
            {
                RegistryUtils.SetValue(Hive, sub, "s", (short)-1, RegistryValueKind.DWord);
                Check.Equal((short)-1, RegistryUtils.GetValue<short>(Hive, sub, "s", 7));
                RegistryUtils.SetValue(Hive, sub, "b", (sbyte)-5, RegistryValueKind.DWord);
                Check.Equal((sbyte)-5, RegistryUtils.GetValue<sbyte>(Hive, sub, "b", 7));
                RegistryUtils.SetValue(Hive, sub, "e", Small.Neg, RegistryValueKind.DWord);
                Check.Equal(Small.Neg, RegistryUtils.GetValue<Small>(Hive, sub, "e", Small.A));
                RegistryUtils.SetValue(Hive, sub, "q", -1, RegistryValueKind.QWord);
                Check.Equal(-1, RegistryUtils.GetValue<int>(Hive, sub, "q", 7));
                Check.Equal((int?)-1, RegistryUtils.GetValue<int?>(Hive, sub, "q"));
                RegistryUtils.SetValue(Hive, sub, "qe", Mid.Neg, RegistryValueKind.QWord);
                Check.Equal(Mid.Neg, RegistryUtils.GetValue<Mid>(Hive, sub, "qe", Mid.A));
                RegistryUtils.SetValue(Hive, sub, "qs", (short)-1, RegistryValueKind.QWord);
                Check.Equal((short)-1, RegistryUtils.GetValue<short>(Hive, sub, "qs", 7));
                // Guards: a wider type still reads the unsigned number, and one that fits neither reading is the default.
                Check.Equal(4294967295L, RegistryUtils.GetValue<long>(Hive, sub, "s"));
                Check.Equal(ulong.MaxValue, RegistryUtils.GetValue<ulong>(Hive, sub, "q"));
                RegistryUtils.SetValue(Hive, sub, "big", 40000, RegistryValueKind.DWord);
                Check.Equal((short)7, RegistryUtils.GetValue<short>(Hive, sub, "big", 7));
            }
            finally { Cleanup(sub); }
        }

        // Integer text given DWord or QWord was range-checked as signed only, by RegistryKey: "4294967295" was refused
        // though "-1" was taken, and a uint, long or double of 4294967295 was stored.
        public void Registry_IntegerTextFollowsTheWordRule()
        {
            string sub = NewKey();
            try
            {
                RegistryUtils.SetValue(Hive, sub, "d", "4294967295", RegistryValueKind.DWord);
                Check.Equal(uint.MaxValue, RegistryUtils.GetValue<uint>(Hive, sub, "d"));
                RegistryUtils.SetValue(Hive, sub, "q", "18446744073709551615", RegistryValueKind.QWord);
                Check.Equal(ulong.MaxValue, RegistryUtils.GetValue<ulong>(Hive, sub, "q"));
                RegistryUtils.SetValue(Hive, sub, "n", " -1 ", RegistryValueKind.DWord);
                Check.Equal(-1, RegistryUtils.GetValue<int>(Hive, sub, "n"));
                Check.Equal("value", Check.Throws<ArgumentException>(() => RegistryUtils.SetValue(Hive, sub, "x", "4294967296", RegistryValueKind.DWord)).ParamName);
                Check.Equal("value", Check.Throws<ArgumentException>(() => RegistryUtils.SetValue(Hive, sub, "x", "1.5", RegistryValueKind.DWord)).ParamName);
                Check.Equal("value", Check.Throws<ArgumentException>(() => RegistryUtils.SetValue(Hive, sub, "x", "text", RegistryValueKind.QWord)).ParamName);
            }
            finally { Cleanup(sub); }
        }

        // ---------- RegistryUtils.SetValue: a refused write creates nothing ----------

        // The subkey path was created before RegistryKey checked the value, so every refused write left new, empty keys;
        // an undefined kind was reported as RegistryKey's own "valueKind".
        public void Registry_ARefusedWriteCreatesNoKey()
        {
            string sub = NewKey();
            try
            {
                string deep = sub + @"\a\b";
                Check.Equal("value", Check.Throws<ArgumentException>(() => RegistryUtils.SetValue(Hive, deep, "x", "text", RegistryValueKind.DWord)).ParamName);
                Check.False(RegistryUtils.KeyExists(Hive, sub), "a refused DWORD created the key");
                Check.Equal("value", Check.Throws<ArgumentException>(() => RegistryUtils.SetValue(Hive, deep, "x", 5, RegistryValueKind.MultiString)).ParamName);
                Check.Equal("value", Check.Throws<ArgumentException>(() => RegistryUtils.SetValue(Hive, deep, "x", "abc", RegistryValueKind.Binary)).ParamName);
                Check.Equal("value", Check.Throws<ArgumentException>(() => RegistryUtils.SetValue(Hive, deep, "x", "abc", RegistryValueKind.None)).ParamName);
                Check.Equal("name", Check.Throws<ArgumentException>(() => RegistryUtils.SetValue(Hive, deep, new string('n', 16384), "v")).ParamName);
                Check.Equal("kind", Check.Throws<ArgumentException>(() => RegistryUtils.SetValue(Hive, deep, "x", "v", (RegistryValueKind)99)).ParamName);
                Check.False(RegistryUtils.KeyExists(Hive, sub), "a refused write created the key");
                // Guards: the limits themselves are accepted.
                RegistryUtils.SetValue(Hive, deep, new string('n', 16383), "v");
                RegistryUtils.SetValue(Hive, deep, "m", new[] { "a" }, RegistryValueKind.MultiString);
                RegistryUtils.SetValue(Hive, deep, "z", new byte[] { 1 }, RegistryValueKind.None);
                Check.Equal("v", RegistryUtils.GetValue<string>(Hive, deep, new string('n', 16383)));
            }
            finally { Cleanup(sub); }
        }

        // ---------- SingleInstanceApp: a message that went out is not sent again ----------

        // When the answer from a live primary was lost after the message went out, Start disposed the gate, failed to take
        // it back (the primary held it) and forwarded again: the primary got the launch twice.
        public void SingleInstance_AMessageThatWentOutIsNotResent()
        {
            string id = "R144Probe" + Guid.NewGuid().ToString("N");
            string folded = id.ToUpperInvariant();
            string user;
            using (var identity = System.Security.Principal.WindowsIdentity.GetCurrent())
                user = identity.User!.Value;
            int session;
            using (var self = System.Diagnostics.Process.GetCurrentProcess())
                session = self.SessionId;
            string pipe = "ToolBelt.SingleInstance.2." + folded + "." + session + "." + user;
            using var gate = SingleInstance.TryAcquire(@"Local\ToolBelt.SingleInstance." + folded + "." + user);
            Check.True(gate.IsOwned, "the scripted primary holds the gate");

            int received = 0;
            using var stop = new CancellationTokenSource();
            // A primary that reads the whole message and then closes without answering.
            Task primary = Task.Run(async () =>
            {
                while (!stop.IsCancellationRequested)
                {
                    using var server = new NamedPipeServerStream(pipe, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    try { await server.WaitForConnectionAsync(stop.Token); }
                    catch (OperationCanceledException) { return; }
                    try
                    {
                        await server.WriteAsync(BitConverter.GetBytes(Environment.ProcessId), stop.Token);
                        var buffer = new byte[64 * 1024];
                        if (await server.ReadAsync(buffer, stop.Token) > 0) Interlocked.Increment(ref received);
                    }
                    catch (Exception) { }
                }
            });
            try
            {
                using SingleInstanceApp app = SingleInstanceApp.Start(id, new[] { "open", "file.txt" }, TimeSpan.FromSeconds(4));
                Check.False(app.IsPrimary, "primary");
                Check.False(app.ForwardedToPrimary, "forwarded");
                Thread.Sleep(300);   // test code: give a resend time to arrive
                Check.Equal(1, Volatile.Read(ref received));
            }
            finally
            {
                stop.Cancel();
                Check.True(primary.Wait(5000), "the scripted primary stopped");
            }
        }
    }
}
