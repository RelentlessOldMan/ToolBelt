// ToolBelt.Windows drop-in — Windows-only (net8.0-windows), self-contained (powrprof).
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace ToolBelt.Windows
{
    /// <summary>A power plan.</summary>
    public sealed class PowerPlan
    {
        internal PowerPlan(Guid id, string name, bool isActive)
        {
            Id = id;
            Name = name;
            IsActive = isActive;
        }

        public Guid Id { get; }

        /// <summary>The localized display name ("Balanced", "High performance", …).</summary>
        public string Name { get; }

        public bool IsActive { get; }

        public override string ToString() => Name + (IsActive ? " (active)" : "");
    }

    /// <summary>
    /// The Windows power plans: which one is active (a benchmark or long acquisition should check it's not running on
    /// "Power saver"), the installed list, and switching — <see cref="SetActive"/> plus a <see cref="Use"/> scope that
    /// restores the previous plan, e.g. <c>using (PowerScheme.Use(PowerScheme.HighPerformance)) { RunBenchmark(); }</c>.
    /// Switching plans needs no elevation for the current user's plans.
    /// </summary>
    public static class PowerScheme
    {
        /// <summary>The built-in "Balanced" plan.</summary>
        public static readonly Guid Balanced = new Guid("381b4222-f694-41f0-9685-ff5bb260df2e");

        /// <summary>The built-in "High performance" plan (hidden on some modern-standby laptops).</summary>
        public static readonly Guid HighPerformance = new Guid("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");

        /// <summary>The built-in "Power saver" plan.</summary>
        public static readonly Guid PowerSaver = new Guid("a1841308-3541-4fab-bc81-f71556f20b4a");

        /// <summary>The active plan.</summary>
        public static PowerPlan Active()
        {
            Guid id = ActiveId();
            return new PowerPlan(id, FriendlyName(id), true);
        }

        public static Guid ActiveId()
        {
            uint err = PowerGetActiveScheme(IntPtr.Zero, out IntPtr ptr);
            if (err != 0) throw new Win32Exception((int)err, "PowerGetActiveScheme failed.");
            try { return Marshal.PtrToStructure<Guid>(ptr); }
            finally { LocalFree(ptr); }
        }

        /// <summary>Every installed plan.</summary>
        public static IReadOnlyList<PowerPlan> All()
        {
            Guid active = ActiveId();
            var plans = new List<PowerPlan>();
            var buffer = new byte[16];
            for (uint index = 0; ; index++)
            {
                uint size = 16;
                uint err = PowerEnumerate(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, ACCESS_SCHEME, index, buffer, ref size);
                if (err == ERROR_NO_MORE_ITEMS) break;
                if (err != 0) throw new Win32Exception((int)err, "PowerEnumerate failed.");
                var id = new Guid(buffer);
                plans.Add(new PowerPlan(id, FriendlyName(id), id == active));
            }
            return plans;
        }

        /// <summary>Makes <paramref name="planId"/> the active plan.</summary>
        public static void SetActive(Guid planId)
        {
            uint err = PowerSetActiveScheme(IntPtr.Zero, ref planId);
            if (err != 0) throw new Win32Exception((int)err, $"PowerSetActiveScheme failed for {planId}.");
        }

        /// <summary>Switches to <paramref name="planId"/> until disposed, then restores the plan that was active.</summary>
        public static IDisposable Use(Guid planId)
        {
            Guid previous = ActiveId();
            if (previous != planId) SetActive(planId);
            return new Restore(previous, previous != planId);
        }

        private sealed class Restore : IDisposable
        {
            private readonly Guid _previous;
            private bool _pending;

            public Restore(Guid previous, bool pending)
            {
                _previous = previous;
                _pending = pending;
            }

            public void Dispose()
            {
                if (!_pending) return;
                _pending = false;
                SetActive(_previous);
            }
        }

        private static string FriendlyName(Guid id)
        {
            uint size = 0;
            PowerReadFriendlyName(IntPtr.Zero, ref id, IntPtr.Zero, IntPtr.Zero, null, ref size);
            if (size == 0) return id.ToString();
            var buffer = new byte[size];
            uint err = PowerReadFriendlyName(IntPtr.Zero, ref id, IntPtr.Zero, IntPtr.Zero, buffer, ref size);
            if (err != 0) return id.ToString();
            return System.Text.Encoding.Unicode.GetString(buffer).TrimEnd('\0');
        }

        private const uint ACCESS_SCHEME = 16, ERROR_NO_MORE_ITEMS = 259;

        [DllImport("powrprof.dll")] private static extern uint PowerGetActiveScheme(IntPtr root, out IntPtr activeScheme);
        [DllImport("powrprof.dll")] private static extern uint PowerSetActiveScheme(IntPtr root, ref Guid scheme);
        [DllImport("powrprof.dll")]
        private static extern uint PowerEnumerate(IntPtr root, IntPtr scheme, IntPtr subgroup, uint accessFlags, uint index, byte[] buffer, ref uint bufferSize);
        [DllImport("powrprof.dll")]
        private static extern uint PowerReadFriendlyName(IntPtr root, ref Guid scheme, IntPtr subgroup, IntPtr setting, byte[]? buffer, ref uint bufferSize);
        [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr mem);
    }
}
