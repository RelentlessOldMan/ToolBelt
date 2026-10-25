using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading;
using Microsoft.Win32;
using ToolBelt.Tests.Framework;
using ToolBelt.Windows;

namespace ToolBelt.Windows.Tests
{
    // Round 22 review regressions for the Windows satellite (second pass).
    public sealed class WindowsReview2Tests
    {
        private static T OnThread<T>(Func<T> f)
        {
            T result = default!;
            Exception? error = null;
            var t = new Thread(() => { try { result = f(); } catch (Exception ex) { error = ex; } });
            t.Start();
            t.Join();
            if (error != null) throw new InvalidOperationException("thread failed: " + error.Message, error);
            return result;
        }

        private static string NewAppId() => "TbR22." + Guid.NewGuid().ToString("N").Substring(0, 12);

        private static string Sid()
        {
            using var identity = WindowsIdentity.GetCurrent();
            return identity.User!.Value;
        }

        private static string GateName(string appId) => @"Local\ToolBelt.SingleInstance." + appId + "." + Sid();

        private static string PipeName(string appId)
        {
            using var self = Process.GetCurrentProcess();
            return "ToolBelt.SingleInstance." + appId + "." + self.SessionId + "." + Sid();
        }

        // ---------- SingleInstance: the gate is not tied to the acquiring thread ----------

        public void SingleInstance_AcquiringThreadExiting_DoesNotFreeTheGate()
        {
            // A Win32 mutex is owned by a thread: when the thread that acquired it exited (an async Main resuming on the
            // pool, a Task.Run), the mutex was abandoned and the next caller became a second owner.
            string name = @"Local\ToolBelt.Windows.Tests.R22." + Guid.NewGuid().ToString("N");
            SingleInstance first = OnThread(() => SingleInstance.TryAcquire(name));
            try
            {
                Check.True(first.IsOwned);
                bool second = OnThread(() => { using var s = SingleInstance.TryAcquire(name); return s.IsOwned; });
                Check.False(second, "the gate was freed when the acquiring thread exited");
            }
            finally { first.Dispose(); }
        }

        public void SingleInstance_DisposedOnAnotherThread_ReleasesTheGate()
        {
            string name = @"Local\ToolBelt.Windows.Tests.R22." + Guid.NewGuid().ToString("N");
            var first = SingleInstance.TryAcquire(name);
            Check.True(first.IsOwned);
            OnThread(() => { first.Dispose(); return 0; });
            bool again = OnThread(() => { using var s = SingleInstance.TryAcquire(name); return s.IsOwned; });
            Check.True(again, "disposing from another thread left the gate held");
        }

        public void SingleInstance_SameThreadTwice_IsNotOwnedTwice()
        {
            string name = @"Local\ToolBelt.Windows.Tests.R22." + Guid.NewGuid().ToString("N");
            using var first = SingleInstance.TryAcquire(name);
            using var second = SingleInstance.TryAcquire(name);
            Check.True(first.IsOwned);
            Check.False(second.IsOwned, "one gate, one owner, whichever thread asks");
        }

        public void SingleInstance_NameTakenByAnotherKindOfObject_IsNotOwned()
        {
            string name = @"Local\ToolBelt.Windows.Tests.R22." + Guid.NewGuid().ToString("N");
            using var ev = new EventWaitHandle(false, EventResetMode.ManualReset, name);
            SingleInstance? gate = null;
            try
            {
                gate = SingleInstance.TryAcquire(name);
                Check.False(gate.IsOwned);
            }
            catch (WaitHandleCannotBeOpenedException) { Check.True(false, "threw instead of returning a non-owning instance"); }
            finally { gate?.Dispose(); }
        }

        public void SingleInstanceApp_StartedOnAThreadThatExits_StaysTheOnlyPrimary()
        {
            string id = NewAppId();
            SingleInstanceApp primary = OnThread(() => SingleInstanceApp.Start(id, Array.Empty<string>()));
            try
            {
                Check.True(primary.IsPrimary);
                var (isPrimary, forwarded) = OnThread(() =>
                {
                    using var s = SingleInstanceApp.Start(id, new[] { "x" }, TimeSpan.FromSeconds(3));
                    return (s.IsPrimary, s.ForwardedToPrimary);
                });
                Check.False(isPrimary, "a second primary appeared");
                Check.True(forwarded);
            }
            finally { primary.Dispose(); }
        }

        // ---------- SingleInstanceApp: forwarding ----------

        public void SingleInstanceApp_ForwardTimeout_BoundsAStalledPrimary()
        {
            string id = NewAppId();
            using var gate = SingleInstance.TryAcquire(GateName(id));
            Check.True(gate.IsOwned);
            using var server = new NamedPipeServerStream(PipeName(id), PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            var accepted = server.WaitForConnectionAsync();                          // accepts, then never says anything
            var sw = Stopwatch.StartNew();
            bool forwarded = OnThread(() => { using var s = SingleInstanceApp.Start(id, new[] { "a" }, TimeSpan.FromSeconds(1)); return s.ForwardedToPrimary; });
            sw.Stop();
            Check.False(forwarded);
            Check.True(sw.ElapsedMilliseconds < 2500, $"a 1 s forward timeout took {sw.ElapsedMilliseconds} ms");
        }

        private static void FakePrimary(string pipeName, List<string> received)
        {
            using var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.CurrentUserOnly);
            server.WaitForConnection();
            server.Write(BitConverter.GetBytes(Environment.ProcessId));
            server.Flush();
            int Int() { var b = new byte[4]; server.ReadExactly(b); return BitConverter.ToInt32(b, 0); }
            string Str() { var b = new byte[Int()]; server.ReadExactly(b); return Encoding.UTF8.GetString(b); }
            Int(); Str();
            int count = Int();
            for (int i = 0; i < count; i++) received.Add(Str());
            server.WriteByte(1);
            server.Flush();
            server.WaitForPipeDrain();
        }

        public void SingleInstanceApp_InfiniteOrMaxForwardTimeout_Forwards()
        {
            foreach (TimeSpan timeout in new[] { Timeout.InfiniteTimeSpan, TimeSpan.MaxValue })
            {
                string id = NewAppId();
                using var gate = SingleInstance.TryAcquire(GateName(id));
                var received = new List<string>();
                var primary = new Thread(() => { Thread.Sleep(300); FakePrimary(PipeName(id), received); }) { IsBackground = true };
                primary.Start();
                bool forwarded;
                try { forwarded = OnThread(() => { using var s = SingleInstanceApp.Start(id, new[] { "hello" }, timeout); return s.ForwardedToPrimary; }); }
                catch (InvalidOperationException ex) { forwarded = false; Check.True(false, $"{timeout}: {ex.InnerException?.GetType().Name}"); }
                Check.True(primary.Join(5000), "fake primary finished");
                Check.True(forwarded, $"{timeout}: not forwarded to a primary that came up after 300 ms");
                Check.Equal("hello", string.Join(",", received));
            }
        }

        public void SingleInstanceApp_PrimaryExitingBeforeTheForward_MakesThisLaunchPrimary()
        {
            string id = NewAppId();
            using var held = new ManualResetEventSlim();
            var owner = new Thread(() =>
            {
                var g = SingleInstance.TryAcquire(GateName(id));                     // a primary that never listens, then exits
                held.Set();
                Thread.Sleep(150);
                g.Dispose();
            });
            owner.Start();
            held.Wait();
            bool primary = OnThread(() =>
            {
                var s = SingleInstanceApp.Start(id, Array.Empty<string>(), TimeSpan.FromMilliseconds(600));
                bool p = s.IsPrimary;
                s.Dispose();
                return p;
            });
            owner.Join();
            Check.True(primary, "the launch was lost: no primary to forward to, and it did not take over");
        }

        public void SingleInstanceApp_QueueFull_IsNotAcknowledged()
        {
            string id = NewAppId();
            using var app = SingleInstanceApp.Start(id, Array.Empty<string>());
            Check.True(app.IsPrimary);
            int acknowledged = 0;
            bool last = true;
            for (int i = 0; i < 101; i++)
            {
                last = OnThread(() => { using var s = SingleInstanceApp.Start(id, new[] { "n" }); return s.ForwardedToPrimary; });
                if (last) acknowledged++;
            }
            int delivered = 0;
            using var done = new ManualResetEventSlim();
            app.SecondInstanceStarted += (s, e) => { if (Interlocked.Increment(ref delivered) == acknowledged) done.Set(); };
            Check.True(done.Wait(10000), $"{Volatile.Read(ref delivered)} of {acknowledged} acknowledged launches delivered");
            Thread.Sleep(200);
            Check.Equal(acknowledged, Volatile.Read(ref delivered), "an acknowledged launch was dropped");
            Check.False(last, "the 101st launch was acknowledged although it could not be kept");
        }

        // ---------- RegistryUtils ----------

        private static string TestKey() => @"Software\ToolBeltReviewTest\" + Guid.NewGuid().ToString("N");

        public void RegistryUtils_HighBitDwordAndQword_ConvertToEveryNumericType()
        {
            string key = TestKey();
            try
            {
                RegistryUtils.SetValue(RegistryHive.CurrentUser, key, "d", unchecked((int)0x80000000), RegistryValueKind.DWord);
                RegistryUtils.SetValue(RegistryHive.CurrentUser, key, "q", long.MinValue, RegistryValueKind.QWord);
                Check.Equal(2147483648UL, RegistryUtils.GetValue<ulong>(RegistryHive.CurrentUser, key, "d", 42UL));
                Check.Equal(2147483648L, RegistryUtils.GetValue<long>(RegistryHive.CurrentUser, key, "d", 42L));
                Check.Equal(2147483648.0, RegistryUtils.GetValue<double>(RegistryHive.CurrentUser, key, "d", 42.0));
                Check.Equal(9223372036854775808m, RegistryUtils.GetValue<decimal>(RegistryHive.CurrentUser, key, "q", 42m));
                Check.Equal(9223372036854775808.0, RegistryUtils.GetValue<double>(RegistryHive.CurrentUser, key, "q", 42.0));
                Check.Equal(int.MinValue, RegistryUtils.GetValue<int>(RegistryHive.CurrentUser, key, "d", 7), "the stored type still reads bit for bit");
            }
            finally { RegistryUtils.DeleteKey(RegistryHive.CurrentUser, key); }
        }

        public void RegistryUtils_NullableAndEnumTargets_Convert()
        {
            string key = TestKey();
            try
            {
                RegistryUtils.SetValue(RegistryHive.CurrentUser, key, "s", "7");
                RegistryUtils.SetValue(RegistryHive.CurrentUser, key, "day", "Tuesday");
                RegistryUtils.SetValue(RegistryHive.CurrentUser, key, "dayNum", 3, RegistryValueKind.DWord);
                Check.Equal((int?)7, RegistryUtils.GetValue<int?>(RegistryHive.CurrentUser, key, "s"));
                Check.Equal((int?)null, RegistryUtils.GetValue<int?>(RegistryHive.CurrentUser, key, "missing"));
                Check.Equal(DayOfWeek.Tuesday, RegistryUtils.GetValue<DayOfWeek>(RegistryHive.CurrentUser, key, "day", DayOfWeek.Sunday));
                Check.Equal(DayOfWeek.Wednesday, RegistryUtils.GetValue<DayOfWeek>(RegistryHive.CurrentUser, key, "dayNum", DayOfWeek.Sunday));
            }
            finally { RegistryUtils.DeleteKey(RegistryHive.CurrentUser, key); }
        }

        public void RegistryUtils_KeyExists_TrueForAKeyThatCannotBeRead()
        {
            // HKLM\SECURITY\Policy exists on every Windows install and is readable only by SYSTEM.
            if (WindowsIdentity.GetCurrent().IsSystem) return;
            bool exists = false;
            try { exists = RegistryUtils.KeyExists(RegistryHive.LocalMachine, @"SECURITY\Policy"); }
            catch (Exception ex) { Check.True(false, "KeyExists threw " + ex.GetType().Name); }
            Check.True(exists);
            Check.False(RegistryUtils.KeyExists(RegistryHive.LocalMachine, @"SECURITY\NoSuchKey" + Guid.NewGuid().ToString("N")));
        }

        // ---------- DpiInfo ----------

        public void DpiInfo_Scale_DoesNotOverflowInIntArithmetic()
        {
            Check.Equal(25_000_000, DpiInfo.Scale(10_000_000, 240));
            Check.Equal(-25_000_000, DpiInfo.Scale(-10_000_000, 240));
            Check.Throws<OverflowException>(() => DpiInfo.Scale(int.MaxValue, 192));
        }

        // ---------- Elevation reads the process token ----------

        [DllImport("advapi32.dll", SetLastError = true)] private static extern bool ImpersonateAnonymousToken(IntPtr thread);
        [DllImport("advapi32.dll", SetLastError = true)] private static extern bool RevertToSelf();
        [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentThread();

        public void Elevation_WhileImpersonating_DescribesTheProcess()
        {
            bool elevated = Elevation.IsElevated();
            bool admin = Elevation.IsAdministrator();
            IntegrityLevel level = Elevation.CurrentIntegrityLevel();
            var result = OnThread(() =>
            {
                if (!ImpersonateAnonymousToken(GetCurrentThread())) return ("could not impersonate", true, true, IntegrityLevel.System);
                try
                {
                    try { return ("", Elevation.IsElevated(), Elevation.IsAdministrator(), Elevation.CurrentIntegrityLevel()); }
                    catch (Exception ex) { return (ex.GetType().Name, false, false, IntegrityLevel.Untrusted); }
                }
                finally { RevertToSelf(); }
            });
            Check.Equal("", result.Item1);
            Check.Equal(elevated, result.Item2);
            Check.Equal(admin, result.Item3);
            Check.Equal(level, result.Item4);
        }

        // ---------- StorageDeviceInfo ----------

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool GetVolumeNameForVolumeMountPointW(string mountPoint, StringBuilder name, int length);

        public void StorageDeviceInfo_VolumeGuidPath_IsALocalPath()
        {
            string root = Path.GetPathRoot(Environment.SystemDirectory)!;                   // "C:\"
            var guid = new StringBuilder(64);
            Check.True(GetVolumeNameForVolumeMountPointW(root, guid, guid.Capacity));
            var plain = StorageDeviceInfo.Describe(root);
            var byGuid = StorageDeviceInfo.Describe(guid.ToString());
            Check.Equal(plain.BusType, byGuid.BusType);
            Check.Equal(plain.Product, byGuid.Product);
        }

        public void StorageDeviceInfo_DotUncPrefix_IsANetworkPath()
        {
            Check.Equal(@"\\server\share\x", StorageDeviceInfo.GetUncPath(@"\\.\UNC\server\share\x"));
            Check.Equal(@"\\server\share\x", StorageDeviceInfo.GetUncPath(@"\\?\UNC\server\share\x"));
        }
    }
}
