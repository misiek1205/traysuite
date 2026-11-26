using System.Runtime.InteropServices;

namespace Suite.Core;

public sealed class FullscreenWatcher : IDisposable
{
    private const uint Foreground = 3, Location = 0x800b, State = 0x800a;
    private readonly Action<string?> changed;
    private readonly WinEvent callback;
    private readonly System.Windows.Forms.Timer debounce = new() { Interval = 240 };
    private nint foregroundHook, resizeHook, active;
    private bool watching;
    private string? currentDevice;

    public FullscreenWatcher(Action<string?> changed)
    {
        this.changed = changed;
        callback = OnWinEvent;
        debounce.Tick += (_, _) => { debounce.Stop(); Evaluate(); };
    }

    public bool Enabled => foregroundHook != 0;

    public bool Configure(bool enabled)
    {
        if (enabled == Enabled)
            return true;
        if (!enabled)
        {
            Stop(notify: true);
            return true;
        }

        foregroundHook = SetWinEventHook(Foreground, Foreground, 0, callback, 0, 0, 0);
        if (!Enabled)
            return false;
        Reevaluate();
        return true;
    }

    public void Reevaluate()
    {
        if (!Enabled)
            return;

        nint window = GetForegroundWindow();
        if (window != active || !watching)
        {
            if (resizeHook != 0)
                UnhookWinEvent(resizeHook);
            active = window;
            uint thread = GetWindowThreadProcessId(window, out uint process);
            resizeHook = thread == 0 ? 0 : SetWinEventHook(State, Location, 0, callback, process, thread, 0);
            watching = true;
        }
        debounce.Stop();
        debounce.Start();
    }

    private void OnWinEvent(nint hook, uint type, nint window, int objectId, int child, uint thread, uint time)
    {
        if (!Enabled)
            return;
        if (type == Foreground)
            Reevaluate();
        else if (window == active && objectId == 0 && child == 0)
        {
            debounce.Stop();
            debounce.Start();
        }
    }

    private void Evaluate()
    {
        string? device = FindFullscreenScreen(active);
        if (!string.Equals(device, currentDevice, StringComparison.OrdinalIgnoreCase))
        {
            currentDevice = device;
            changed(device);
        }
    }

    private static string? FindFullscreenScreen(nint window)
    {
        if (window == 0 || !IsWindowVisible(window) || IsIconic(window) || window == GetDesktopWindow()
            || window == GetShellWindow() || window == GetAncestor(GetShellWindow(), 2))
            return null;
        GetWindowThreadProcessId(window, out uint process);
        if (process == (uint)Environment.ProcessId)
            return null;

        long style = GetWindowLongPtr(window, -16).ToInt64();
        if (IsZoomed(window) && (style & 0x00c00000) == 0x00c00000)
            return null; // A maximized window with a title bar is not fullscreen.
        if (DwmGetWindowAttribute(window, 9, out var rect, 16) != 0 && !GetWindowRect(window, out rect))
            return null;

        var screen = Screen.FromHandle(window);
        return CoversDisplay(Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom), screen.Bounds)
            ? screen.DeviceName : null;
    }

    public static bool CoversDisplay(Rectangle window, Rectangle screen) =>
        screen.Width > 0 && screen.Height > 0 && Math.Abs(window.Left - screen.Left) <= 2
        && Math.Abs(window.Top - screen.Top) <= 2 && Math.Abs(window.Right - screen.Right) <= 2
        && Math.Abs(window.Bottom - screen.Bottom) <= 2;

    private void Stop(bool notify)
    {
        debounce.Stop();
        if (foregroundHook != 0)
            UnhookWinEvent(foregroundHook);
        if (resizeHook != 0)
            UnhookWinEvent(resizeHook);
        foregroundHook = resizeHook = active = 0;
        watching = false;
        if (currentDevice is not null)
        {
            currentDevice = null;
            if (notify)
                changed(null);
        }
    }

    public void Dispose()
    {
        Stop(notify: false);
        debounce.Dispose();
    }

    private delegate void WinEvent(nint hook, uint type, nint window, int objectId, int child, uint thread, uint time);

    [DllImport("user32.dll")] private static extern nint SetWinEventHook(uint min, uint max, nint module,
        WinEvent callback, uint process, uint thread, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(nint hook);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern nint GetDesktopWindow();
    [DllImport("user32.dll")] private static extern nint GetShellWindow();
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint window, uint flags);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint process);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] private static extern bool IsIconic(nint window);
    [DllImport("user32.dll")] private static extern bool IsZoomed(nint window);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint window, int index);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint window, out NativeRect rect);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint window, int attribute,
        out NativeRect rect, uint size);
}
