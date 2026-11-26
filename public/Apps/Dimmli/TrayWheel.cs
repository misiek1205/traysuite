using System.Runtime.InteropServices;
using System.Text;

namespace Dimmli;

internal sealed class TrayWheel : IDisposable
{
    private const int LowLevelMouseHook = 14;
    private const int MouseWheelMessage = 0x020a;
    private const int WheelDelta = 120;
    private readonly TrayIcon tray;
    private readonly HookCallback callback;
    private readonly StringBuilder className = new(80);
    private readonly System.Windows.Forms.Timer boundsTimer = new() { Interval = 160 };
    private nint hook;
    private Rectangle bounds;
    private nint trayWindow;
    private int remainder;
    private bool available;

    public TrayWheel(TrayIcon tray)
    {
        this.tray = tray;
        callback = OnMouse;
        boundsTimer.Tick += (_, _) => RefreshBounds();
    }

    public bool Enabled => hook != 0;
    public bool Available
    {
        get => available;
        set
        {
            if (available == value)
                return;
            available = value;
            UpdateTimer();
        }
    }

    public bool SetEnabled(bool enabled)
    {
        if (enabled == Enabled)
            return true;

        if (enabled)
        {
            hook = SetWindowsHookEx(LowLevelMouseHook, callback, GetModuleHandle(null), 0);
            if (hook == 0)
                return false;

            UpdateTimer();
            return true;
        }

        UnhookWindowsHookEx(hook);
        hook = 0;
        UpdateTimer();
        return true;
    }

    private void UpdateTimer()
    {
        if (Enabled && Available)
        {
            RefreshBounds();
            boundsTimer.Start();
        }
        else
        {
            boundsTimer.Stop();
            bounds = Rectangle.Empty;
            trayWindow = 0;
            remainder = 0;
        }
    }

    private void RefreshBounds()
    {
        var lastBounds = bounds;
        var lastWindow = trayWindow;
        nint window = Available ? RootAt(Cursor.Position) : 0;
        bounds = IsTaskbar(window) ? tray.IconBounds() : Rectangle.Empty;
        var iconCenter = new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
        trayWindow = bounds.Width > 0 && bounds.Height > 0 && RootAt(iconCenter) == window ? window : 0;
        if (bounds != lastBounds || trayWindow != lastWindow || !bounds.Contains(Cursor.Position))
            remainder = 0;
    }

    private bool IsTaskbar(nint window) =>
        window != 0 && GetClassName(window, className, className.Capacity) > 0
        && className.ToString() is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "NotifyIconOverflowWindow"
            or "TopLevelWindowForOverflowXamlIsland";

    private nint OnMouse(int code, nint message, nint data)
    {
        if (code >= 0 && hook != 0 && Available && message == MouseWheelMessage && trayWindow != 0)
        {
            var input = Marshal.PtrToStructure<MouseInput>(data);
            if (bounds.Contains(input.Position) && RootAt(input.Position) == trayWindow)
            {
                remainder += unchecked((short)(input.MouseData >> 16));
                int steps = remainder / WheelDelta;
                remainder %= WheelDelta;
                if (steps == 0 || tray.PostWheelStep(steps))
                    return 1;
            }
            else
            {
                remainder = 0;
            }
        }
        return CallNextHookEx(0, code, message, data);
    }

    private static nint RootAt(Point point) => GetAncestor(WindowFromPoint(point), 2); // GA_ROOT

    public void Dispose()
    {
        SetEnabled(false);
        boundsTimer.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public Point Position;
        public uint MouseData, Flags, Time;
        public nuint ExtraInfo;
    }

    private delegate nint HookCallback(int code, nint message, nint data);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? name);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint SetWindowsHookEx(int type, HookCallback callback, nint module, uint threadId);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(nint hook);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hook, int code, nint message, nint data);

    [DllImport("user32.dll")]
    private static extern nint WindowFromPoint(Point point);

    [DllImport("user32.dll")]
    private static extern nint GetAncestor(nint window, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint window, StringBuilder className, int count);
}
