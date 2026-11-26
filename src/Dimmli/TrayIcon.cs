using System.Runtime.InteropServices;

namespace Dimmli;

internal sealed class TrayIcon : NativeWindow, IDisposable
{
    private const uint IconId = 1;
    private const int TrayMessage = 0x8001;
    private const int WheelMessage = 0x8002;
    private const uint Add = 0, Delete = 2, SetFocus = 3, SetVersion = 4;
    private const uint MessageIconTip = 0x01 | 0x02 | 0x04 | 0x80;
    private readonly int taskbarCreated = RegisterWindowMessage("TaskbarCreated");
    private readonly Icon icon;
    private readonly ContextMenuStrip menu;
    private bool visible;
    private bool versionFour;

    public event Action<Point>? Selected;
    public event Action<int>? WheelStep;

    public TrayIcon(Icon icon, ContextMenuStrip menu)
    {
        this.icon = icon;
        this.menu = menu;
        // A top-level window receives the TaskbarCreated broadcast when Explorer restarts.
        CreateHandle(new CreateParams());
        menu.Closed += MenuClosed;
    }

    public bool Visible
    {
        get => visible;
        set
        {
            if (visible == value)
                return;
            visible = value;
            if (value)
                AddIcon();
            else
                Notify(Delete);
        }
    }

    private void AddIcon()
    {
        versionFour = false;
        if (Notify(Add))
            versionFour = Notify(SetVersion);
    }

    public Rectangle IconBounds()
    {
        if (!visible)
            return Rectangle.Empty;

        var identifier = new Identifier { Size = (uint)Marshal.SizeOf<Identifier>(), Window = Handle, Id = IconId };
        // A nonzero HRESULT can carry the overflow arrow rectangle rather than the icon.
        return Shell_NotifyIconGetRect(ref identifier, out var bounds) == 0
            ? Rectangle.FromLTRB(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom)
            : Rectangle.Empty;
    }

    public bool PostWheelStep(int step) => PostMessage(Handle, WheelMessage, step, 0);

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == taskbarCreated && taskbarCreated != 0 && visible)
        {
            AddIcon();
            return;
        }
        if (message.Msg == WheelMessage)
        {
            WheelStep?.Invoke((int)message.WParam);
            return;
        }
        if (message.Msg == TrayMessage && visible)
        {
            int notification = (int)message.LParam & 0xffff;
            var point = Cursor.Position;
            if (versionFour && notification is 0x0400 or 0x0401) // NIN_SELECT / NIN_KEYSELECT
            {
                long position = (long)message.WParam;
                Selected?.Invoke(new Point(unchecked((short)position), unchecked((short)(position >> 16))));
            }
            else if (!versionFour && notification == 0x0202) // WM_LBUTTONUP
            {
                Selected?.Invoke(point);
            }
            else if (notification == (versionFour ? 0x007b : 0x0205)) // WM_CONTEXTMENU / WM_RBUTTONUP
            {
                if (versionFour)
                {
                    long position = (long)message.WParam;
                    var anchor = new Point(unchecked((short)position), unchecked((short)(position >> 16)));
                    if (anchor != new Point(-1, -1))
                        point = anchor;
                }
                SetForegroundWindow(Handle);
                menu.Show(point);
                PostMessage(Handle, 0, 0, 0);
            }
            return;
        }
        base.WndProc(ref message);
    }

    private void MenuClosed(object? sender, ToolStripDropDownClosedEventArgs args)
    {
        if (visible && args.CloseReason != ToolStripDropDownCloseReason.ItemClicked)
            Notify(SetFocus);
    }

    private bool Notify(uint operation)
    {
        var data = new IconData
        {
            Size = (uint)Marshal.SizeOf<IconData>(), Window = Handle, Id = IconId,
            Flags = operation == Add ? MessageIconTip : 0,
            Callback = TrayMessage, Icon = icon.Handle, Tip = "Dimmli", Version = 4
        };
        return Shell_NotifyIcon(operation, ref data);
    }

    public void Dispose()
    {
        Visible = false;
        menu.Closed -= MenuClosed;
        DestroyHandle();
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct IconData
    {
        public uint Size;
        public nint Window;
        public uint Id, Flags, Callback;
        public nint Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string? Info;
        public uint Version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string? InfoTitle;
        public uint InfoFlags;
        public Guid Guid;
        public nint BalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Identifier
    {
        public uint Size;
        public nint Window;
        public uint Id;
        public Guid Guid;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(uint operation, ref IconData data);

    [DllImport("shell32.dll")]
    private static extern int Shell_NotifyIconGetRect(ref Identifier identifier, out MonitorNative.Rect rect);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int RegisterWindowMessage(string name);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint window);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(nint window, int message, nint wParam, nint lParam);
}
