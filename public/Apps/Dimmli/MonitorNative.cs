using System.Runtime.InteropServices;

namespace Dimmli;

internal static class MonitorNative
{
    internal const byte BrightnessCode = 0x10;
    internal const uint DeviceInterfaceName = 1;
    internal const uint ActiveDevice = 1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct PhysicalMonitor
    {
        public nint Handle;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Description;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct MonitorInfo
    {
        public uint Size;
        public Rect Monitor, Work;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string Device;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct DisplayDevice
    {
        public uint Size;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string Name;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Description;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Id;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Key;
    }

    internal delegate bool MonitorCallback(nint monitor, nint dc, nint rect, nint data);

    [DllImport("user32.dll")]
    internal static extern bool EnumDisplayMonitors(nint dc, nint clip, MonitorCallback callback, nint data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern bool EnumDisplayDevices(string device, uint index, ref DisplayDevice result, uint flags);

    [DllImport("dxva2.dll")]
    internal static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(nint monitor, out uint count);

    [DllImport("dxva2.dll")]
    internal static extern bool GetPhysicalMonitorsFromHMONITOR(nint monitor, uint count, [Out] PhysicalMonitor[] monitors);

    [DllImport("dxva2.dll")]
    internal static extern bool DestroyPhysicalMonitor(nint monitor);

    [DllImport("dxva2.dll")]
    internal static extern bool GetMonitorBrightness(nint monitor, out uint minimum, out uint current, out uint maximum);

    [DllImport("dxva2.dll")]
    internal static extern bool SetMonitorBrightness(nint monitor, uint value);

    [DllImport("dxva2.dll")]
    internal static extern bool GetVCPFeatureAndVCPFeatureReply(nint monitor, byte code, out uint type, out uint current, out uint maximum);

    [DllImport("dxva2.dll")]
    internal static extern bool SetVCPFeature(nint monitor, byte code, uint value);
}
