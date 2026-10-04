// ToolBelt.Windows drop-in — Windows-only (net8.0-windows), self-contained (Win32 P/Invoke).
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace ToolBelt.Windows
{
    /// <summary>A point-in-time snapshot of the system power status (AC line, battery, saver mode).</summary>
    public readonly struct PowerStatusInfo
    {
        internal PowerStatusInfo(bool? acOnline, bool batteryPresent, int? chargePercent, bool saverOn, TimeSpan? remaining)
        {
            AcLineOnline = acOnline;
            BatteryPresent = batteryPresent;
            BatteryChargePercent = chargePercent;
            BatterySaverOn = saverOn;
            BatteryLifeRemaining = remaining;
        }

        /// <summary>True if on AC power, false if on battery, null if the state is unknown.</summary>
        public bool? AcLineOnline { get; }

        /// <summary>True if a system battery is installed.</summary>
        public bool BatteryPresent { get; }

        /// <summary>Battery charge 0–100, or null if unknown / no battery.</summary>
        public int? BatteryChargePercent { get; }

        /// <summary>True if Windows battery-saver mode is active.</summary>
        public bool BatterySaverOn { get; }

        /// <summary>Estimated battery time remaining, or null if unknown / on AC.</summary>
        public TimeSpan? BatteryLifeRemaining { get; }

        public override string ToString()
        {
            string ac = AcLineOnline is null ? "AC unknown" : (AcLineOnline.Value ? "on AC" : "on battery");
            string batt = BatteryPresent
                ? (BatteryChargePercent is int p ? $"{p}%" : "battery present")
                : "no battery";
            return BatterySaverOn ? $"{ac}, {batt}, saver on" : $"{ac}, {batt}";
        }
    }

    /// <summary>Reads the machine's power status via the Win32 <c>GetSystemPowerStatus</c> API.</summary>
    public static class PowerStatus
    {
        private const byte Unknown = 255;
        private const byte NoBatteryFlag = 128;
        private const uint TimeUnknown = 0xFFFFFFFF;

        /// <summary>Queries the current power status. Throws <see cref="Win32Exception"/> if the OS call fails.</summary>
        public static PowerStatusInfo Query()
        {
            if (!GetSystemPowerStatus(out SYSTEM_POWER_STATUS s))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "GetSystemPowerStatus failed.");

            bool? ac = s.ACLineStatus == Unknown ? (bool?)null : s.ACLineStatus == 1;
            bool batteryPresent = s.BatteryFlag != Unknown && (s.BatteryFlag & NoBatteryFlag) == 0;
            int? charge = (!batteryPresent || s.BatteryLifePercent == Unknown) ? (int?)null : s.BatteryLifePercent;
            bool saver = (s.SystemStatusFlag & 0x01) != 0;
            TimeSpan? remaining = s.BatteryLifeTime == TimeUnknown ? (TimeSpan?)null : TimeSpan.FromSeconds(s.BatteryLifeTime);
            return new PowerStatusInfo(ac, batteryPresent, charge, saver, remaining);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SYSTEM_POWER_STATUS
        {
            public byte ACLineStatus;
            public byte BatteryFlag;
            public byte BatteryLifePercent;
            public byte SystemStatusFlag; // bit 0: battery saver (Windows 10+)
            public uint BatteryLifeTime;
            public uint BatteryFullLifeTime;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS lpSystemPowerStatus);
    }
}
