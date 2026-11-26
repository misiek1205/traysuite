using System.Management;
using System.Runtime.InteropServices;
using static TrayLuminance.MonitorNative;

namespace TrayLuminance;

internal sealed record Display(string Name, int Brightness, Func<int, int?>? SetBrightness, string? Id = null);

internal sealed class MonitorService : IDisposable
{
    private readonly List<nint> physicalHandles = [];

    public List<Display> Discover()
    {
        Dispose();
        var panels = DiscoverWmi();
        var displays = new List<Display>();
        var logicalMonitors = new List<nint>();
        EnumDisplayMonitors(0, 0, (monitor, _, _, _) =>
        {
            logicalMonitors.Add(monitor);
            return true;
        }, 0);

        foreach (nint monitor in logicalMonitors)
        {
            var devices = GetDisplayDevices(monitor);
            PhysicalMonitor[] physical = [];
            if (GetNumberOfPhysicalMonitorsFromHMONITOR(monitor, out uint count) && count > 0)
            {
                var candidates = new PhysicalMonitor[count];
                if (GetPhysicalMonitorsFromHMONITOR(monitor, count, candidates))
                    physical = candidates;
            }

            if (physical.Length == 0)
            {
                foreach (DisplayDevice device in devices)
                {
                    if (panels.Remove(InterfaceToInstance(device.Id), out Display? panel))
                        displays.Add(panel with { Name = NameOrDefault(device.Description) });
                    else
                        displays.Add(new Display(NameOrDefault(device.Description), 0, null));
                }
                continue;
            }

            foreach (PhysicalMonitor screen in physical)
                physicalHandles.Add(screen.Handle);

            foreach (PhysicalMonitor screen in physical)
            {
                // Use WMI first for a matching panel. Only match unambiguous device names in clone mode.
                var matchingDevices = devices.Where(device =>
                    (devices.Count == 1 && physical.Length == 1) ||
                    (string.Equals(device.Description, screen.Description, StringComparison.OrdinalIgnoreCase)
                    && physical.Count(candidate => string.Equals(candidate.Description, screen.Description,
                        StringComparison.OrdinalIgnoreCase)) == 1)).ToArray();
                DisplayDevice? match = matchingDevices.Length == 1 ? matchingDevices[0] : null;
                if (match is { } device && panels.Remove(InterfaceToInstance(device.Id), out Display? panel))
                {
                    displays.Add(panel with { Name = NameOrDefault(device.Description) });
                    continue;
                }

                string name = NameOrDefault(screen.Description, match?.Description);
                string id = InterfaceToInstance(match?.Id);
                displays.Add(DiscoverDdc(screen.Handle, name) with { Id = id.Length == 0 ? null : id });
            }
        }

        displays.AddRange(panels.Values);
        return displays;
    }

    private static Display DiscoverDdc(nint handle, string name)
    {
        if (GetMonitorBrightness(handle, out uint minimum, out uint current, out uint maximum) && maximum > minimum)
        {
            return new Display(name, ToPercent(current, minimum, maximum), percent =>
            {
                uint value = FromPercent(percent, minimum, maximum);
                if (SetMonitorBrightness(handle, value))
                    return ToPercent(value, minimum, maximum);
                return DiscoverVcpWriter(handle, out _)?.Invoke(percent);
            });
        }

        var write = DiscoverVcpWriter(handle, out int brightness);
        return new Display(name, brightness, write);
    }

    private static Func<int, int?>? DiscoverVcpWriter(nint handle, out int brightness)
    {
        if (GetVCPFeatureAndVCPFeatureReply(handle, BrightnessCode, out _, out uint current, out uint maximum)
            && maximum > 0)
        {
            brightness = ToPercent(current, 0, maximum);
            return percent =>
            {
                uint value = FromPercent(percent, 0, maximum);
                return SetVCPFeature(handle, BrightnessCode, value) ? ToPercent(value, 0, maximum) : null;
            };
        }

        brightness = 0;
        return null;
    }

    private static Dictionary<string, Display> DiscoverWmi()
    {
        var panels = new Dictionary<string, Display>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var scope = new ManagementScope(@"root\wmi");
            using var methodsQuery = new ManagementObjectSearcher(scope,
                new ObjectQuery("SELECT * FROM WmiMonitorBrightnessMethods WHERE Active = TRUE"));
            using var methods = methodsQuery.Get();
            var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (ManagementObject method in methods)
            {
                using (method)
                {
                    if (method["InstanceName"] is string instance)
                        paths[instance] = method.Path.Path;
                }
            }

            using var levelsQuery = new ManagementObjectSearcher(scope,
                new ObjectQuery("SELECT * FROM WmiMonitorBrightness WHERE Active = TRUE"));
            using var panelsFound = levelsQuery.Get();
            foreach (ManagementObject panel in panelsFound)
            {
                using (panel)
                {
                    if (panel["InstanceName"] is not string instance || !paths.TryGetValue(instance, out string? path)
                        || panel["CurrentBrightness"] is not byte current)
                        continue;

                    byte[] levels = panel["Level"] as byte[] ?? [];
                    int suffix = instance.LastIndexOf('_');
                    string id = suffix > instance.LastIndexOf('\\') ? instance[..suffix] : instance;
                    panels[id] = new Display(Texts.Get("Built-in display", "Ekran wbudowany"), current,
                        percent => SetPanelBrightness(path, percent, levels), id.ToUpperInvariant());
                }
            }
        }
        catch (Exception error) when (IsWmiError(error))
        {
            // Desktop GPUs may not expose the WMI brightness classes.
        }
        return panels;
    }

    private static int? SetPanelBrightness(string path, int percent, byte[] levels)
    {
        byte level = levels.Length == 0
            ? (byte)percent
            : levels.MinBy(value => Math.Abs(value - percent));

        try
        {
            using var monitor = new ManagementObject(new ManagementScope(@"root\wmi"), new ManagementPath(path), null);
            using var input = monitor.GetMethodParameters("WmiSetBrightness");
            input["Timeout"] = 0u;
            input["Brightness"] = level;
            using var result = monitor.InvokeMethod("WmiSetBrightness", input, null);
            return result?["ReturnValue"] is uint code && code == 0 ? level : null;
        }
        catch (Exception error) when (IsWmiError(error))
        {
            return null;
        }
    }

    private static bool IsWmiError(Exception error) =>
        error is ManagementException or COMException or UnauthorizedAccessException;

    private static List<DisplayDevice> GetDisplayDevices(nint monitor)
    {
        var devices = new List<DisplayDevice>();
        var info = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info))
            return devices;

        for (uint index = 0; ; index++)
        {
            var device = new DisplayDevice { Size = (uint)Marshal.SizeOf<DisplayDevice>() };
            if (!EnumDisplayDevices(info.Device, index, ref device, DeviceInterfaceName))
                return devices;
            if ((device.Flags & ActiveDevice) != 0)
                devices.Add(device);
        }
    }

    private static string InterfaceToInstance(string? path)
    {
        string[] parts = path?.Split('#') ?? [];
        return parts.Length >= 3 && parts[1].Length > 0 && parts[2].Length > 0
            ? $@"DISPLAY\{parts[1]}\{parts[2]}".ToUpperInvariant() : "";
    }

    private static string NameOrDefault(string? name, string? fallback = null) =>
        !string.IsNullOrWhiteSpace(name) ? name.Trim() : fallback?.Trim() is { Length: > 0 } label
            ? label : Texts.Get("Display", "Monitor");

    private static int ToPercent(uint value, uint minimum, uint maximum) =>
        (int)Math.Round((Math.Clamp(value, minimum, maximum) - minimum) * 100.0 / (maximum - minimum));

    private static uint FromPercent(int percent, uint minimum, uint maximum) =>
        minimum + (uint)Math.Round((maximum - minimum) * (percent / 100.0));

    public void Dispose()
    {
        foreach (nint handle in physicalHandles)
            DestroyPhysicalMonitor(handle);
        physicalHandles.Clear();
    }
}
