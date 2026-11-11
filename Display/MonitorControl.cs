using System.Runtime.InteropServices;

namespace EdgeDashboard.Display;

/// <summary>
/// One physical monitor, controlled over DDC/CI (the control channel built into the video cable).
/// Calls take tens of milliseconds each, so use them off the drawing thread.
/// </summary>
public sealed class DdcMonitor
{
    public const byte FactoryReset = 0x04, Brightness = 0x10, Contrast = 0x12, RedGain = 0x16, GreenGain = 0x18, BlueGain = 0x1A, Sharpness = 0x87;

    private readonly IntPtr _handle;
    private readonly object _gate = new();

    public string Description { get; }
    public int X { get; }
    public int Y { get; }
    public int Width { get; }
    public int Height { get; }
    public int RefreshRate { get; }
    public bool IsEdge => Width == 2560 && Height == 720;

    internal DdcMonitor(IntPtr handle, string description, int x, int y, int width, int height, int refresh)
    {
        _handle = handle; Description = description; X = x; Y = y; Width = width; Height = height; RefreshRate = refresh;
    }

    /// <summary>Current value and maximum of a setting, or null when the monitor doesn't offer it.</summary>
    public (uint Current, uint Max)? Get(byte code)
    {
        lock (_gate)
        {
            for (int attempt = 0; attempt < 2; attempt++) // DDC occasionally drops a reply; one retry is enough
                if (Native.GetVCPFeatureAndVCPFeatureReply(_handle, code, IntPtr.Zero, out uint current, out uint max) && max > 0)
                    return (current, max);
            return null;
        }
    }

    public bool Set(byte code, uint value)
    {
        lock (_gate) return Native.SetVCPFeature(_handle, code, value);
    }

    public static List<DdcMonitor> Enumerate()
    {
        var list = new List<DdcMonitor>();
        Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMon, IntPtr _, ref Native.RECT r, IntPtr _) =>
        {
            var info = new Native.MONITORINFOEX { cbSize = Marshal.SizeOf<Native.MONITORINFOEX>() };
            int refresh = 0;
            if (Native.GetMonitorInfo(hMon, ref info))
            {
                var mode = new byte[220];
                BitConverter.TryWriteBytes(mode.AsSpan(68), (short)220);
                if (Native.EnumDisplaySettingsW(info.szDevice, -1, mode)) refresh = BitConverter.ToInt32(mode, 184);
            }
            if (Native.GetNumberOfPhysicalMonitorsFromHMONITOR(hMon, out uint count) && count > 0)
            {
                var physical = new Native.PHYSICAL_MONITOR[count];
                if (Native.GetPhysicalMonitorsFromHMONITOR(hMon, count, physical))
                    foreach (var p in physical)
                        list.Add(new DdcMonitor(p.hPhysicalMonitor, p.szPhysicalMonitorDescription, r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top, refresh));
            }
            return true;
        }, IntPtr.Zero);
        return list;
    }

    private static class Native
    {
        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct PHYSICAL_MONITOR
        {
            public IntPtr hPhysicalMonitor;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szPhysicalMonitorDescription;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct MONITORINFOEX
        {
            public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
        }

        public delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref RECT rect, IntPtr data);

        [DllImport("user32.dll")] public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX info);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool EnumDisplaySettingsW(string deviceName, int modeNum, byte[] devMode);
        [DllImport("dxva2.dll")] public static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, out uint count);
        [DllImport("dxva2.dll")] public static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, uint count, [Out] PHYSICAL_MONITOR[] monitors);
        [DllImport("dxva2.dll")] public static extern bool GetVCPFeatureAndVCPFeatureReply(IntPtr hMonitor, byte code, IntPtr type, out uint current, out uint max);
        [DllImport("dxva2.dll")] public static extern bool SetVCPFeature(IntPtr hMonitor, byte code, uint value);
    }
}
