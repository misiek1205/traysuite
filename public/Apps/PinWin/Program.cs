using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace PinWin;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args) => AppLifecycle.Run("PinWin", args, () => ApplicationConfiguration.Initialize(),
        () => new TrayContext("PinWin", "PinWin", new PinWindow(), SystemIcons.Application));
}

internal sealed class PinWindow : ModuleWindow
{
    private const string Preferences = @"Software\TraySuite\PinWin";
    private readonly Hotkeys hotkeys = new();
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 90 };
    private readonly Dictionary<nint, (uint Process, uint Thread, Frame Frame)> pins = [];
    private readonly TextBox shortcut = new() { ReadOnly = true };
    private readonly WinEvent foreground;
    private readonly nint hook;
    private nint previous;
    private int hotkeyId = 1;
    private readonly PopupToggle border;
    private readonly FlowLayoutPanel list = new() { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };

    public PinWindow() : base("PinWin", Texts.Get("Keep windows on top", "Okna zawsze na wierzchu"), 430)
    {
        using var settings = Registry.CurrentUser.OpenSubKey(Preferences);
        int key = (int)(settings?.GetValue("Key") ?? (int)Keys.Space);
        int modifiers = (int)(settings?.GetValue("Modifiers") ?? 10);
        border = new PopupToggle { Text = Texts.Get("Show border", "Pokaż ramkę"), Checked = settings?.GetValue("Border") is not 0 };
        border.CheckedChanged += (_, _) => { Save("Border", border.Checked ? 1 : 0); UpdatePins(); };
        Add(Button(Texts.Get("Pin active window", "Przypnij aktywne okno"), () => Toggle(previous, pinOnly: true)), 36);
        Add(border, 30);
        Add(Label(Texts.Get("Focus the field and press a new shortcut", "Kliknij pole i naciśnij nowy skrót")), 23);
        shortcut.BackColor = Theme.Card;
        shortcut.ForeColor = Theme.Text;
        shortcut.AccessibleName = Texts.Get("Global shortcut", "Skrót globalny");
        shortcut.KeyDown += (_, e) =>
        {
            if (e.KeyCode is Keys.Tab or Keys.Escape)
                return;
            e.SuppressKeyPress = true;
            int flags = (e.Control ? 2 : 0) | (e.Alt ? 1 : 0) | (e.Shift ? 4 : 0)
                | ((GetKeyState(0x5b) < 0 || GetKeyState(0x5c) < 0) ? 8 : 0);
            if (flags != 0 && e.KeyCode is not (Keys.ControlKey or Keys.Menu or Keys.ShiftKey or Keys.LWin or Keys.RWin))
                Register((int)e.KeyCode, flags, save: true);
        };
        Add(shortcut, 28);
        Add(list, 150);
        Remember(GetForegroundWindow());
        foreground = (_, _, window, _, _, _, _) => Remember(window);
        hook = SetWinEventHook(3, 3, 0, foreground, 0, 0, 2);
        timer.Tick += (_, _) => UpdatePins();
        VisibleChanged += (_, _) => { if (Visible) Render(); };
        Register(key, modifiers, save: false);
    }

    private void Register(int key, int modifiers, bool save)
    {
        int id = hotkeyId == 1 ? 2 : 1;
        string display = ((modifiers & 2) != 0 ? "Ctrl + " : "") + ((modifiers & 1) != 0 ? "Alt + " : "")
            + ((modifiers & 4) != 0 ? "Shift + " : "") + ((modifiers & 8) != 0 ? "Win + " : "") + (Keys)key;
        if (!save || shortcut.Text != display || !hotkeys.IsRegistered(hotkeyId))
        {
            if (!hotkeys.Register(id, (Keys)key, (HotkeyModifiers)modifiers, () => Toggle(GetForegroundWindow())))
            {
                SetStatus(Texts.Get("This shortcut is unavailable.", "Ten skrót jest niedostępny."));
                return;
            }
            hotkeys.Unregister(hotkeyId);
            hotkeyId = id;
        }
        shortcut.Text = display;
        SetStatus("");
        if (save) { Save("Key", key); Save("Modifiers", modifiers); }
    }

    private void Save(string name, int value)
    {
        try { using var key = Registry.CurrentUser.CreateSubKey(Preferences); key.SetValue(name, value); }
        catch (Exception error) when (error is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        { SetStatus(Texts.Get("Could not save preferences.", "Nie udało się zapisać ustawień.")); }
    }

    private void Remember(nint window)
    {
        if (Eligible(window)) previous = window;
    }

    private static bool Eligible(nint window)
    {
        if (window == 0 || window == GetShellWindow() || !IsWindowVisible(window)) return false;
        GetWindowThreadProcessId(window, out uint pid);
        var type = new StringBuilder(64);
        GetClassName(window, type, type.Capacity);
        return pid != 0 && pid != Environment.ProcessId && type.ToString() is not ("Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "WorkerW");
    }

    private void Toggle(nint window, bool pinOnly = false)
    {
        if (!Eligible(window))
        {
            SetStatus(Texts.Get("Activate a window first.", "Najpierw aktywuj okno."));
            return;
        }
        bool isTopmost = (GetWindowLong(window, -20) & 8) != 0;
        if (pinOnly && pins.ContainsKey(window)) return;
        bool pin = pinOnly || !isTopmost;
        if (!SetTopmost(window, pin))
        {
            SetStatus(Texts.Get("Windows denied the change.", "System odmówił zmiany."));
            return;
        }
        if (pin)
        {
            uint thread = GetWindowThreadProcessId(window, out uint pid);
            Remove(window);
            pins.Add(window, (pid, thread, new Frame()));
        }
        else Remove(window);
        SetStatus("");
        UpdatePins();
        Render();
    }

    private static bool SetTopmost(nint window, bool pin) => SetWindowPos(window, pin ? -1 : -2, 0, 0, 0, 0, 0x13);
    private void Remove(nint window)
    {
        if (pins.Remove(window, out var entry)) entry.Frame.Dispose();
    }

    private void UpdatePins()
    {
        bool changed = false;
        foreach (var (window, entry) in pins.ToArray())
        {
            if (GetWindowThreadProcessId(window, out uint pid) != entry.Thread || pid != entry.Process
                || (GetWindowLong(window, -20) & 8) == 0)
            {
                Remove(window);
                changed = true;
            }
            else if (border.Checked && IsWindowVisible(window) && !IsIconic(window)
                && DwmGetWindowAttribute(window, 14, out int cloaked, 4) == 0 && cloaked == 0
                && DwmGetWindowAttribute(window, 9, out Rectangle rect, 16) == 0)
                entry.Frame.Follow(window, Rectangle.FromLTRB(rect.X, rect.Y, rect.Width, rect.Height));
            else entry.Frame.Hide();
        }
        timer.Enabled = pins.Count != 0;
        if (changed && Visible) Render();
    }

    private void Render()
    {
        list.SuspendLayout();
        try
        {
            while (list.Controls.Count > 0) list.Controls[0].Dispose();
            foreach (nint window in pins.Keys)
            {
                var title = new StringBuilder(256);
                GetWindowText(window, title, title.Capacity);
                string name = title.Length == 0 ? $"0x{window:X}" : title.ToString().Replace("&", "&&");
                var button = Button(Texts.Get("Unpin ", "Odepnij ") + name, () =>
                {
                    if (!SetTopmost(window, false) && IsWindowVisible(window))
                    {
                        SetStatus(Texts.Get("Windows denied the change.", "System odmówił zmiany."));
                        return;
                    }
                    Remove(window);
                    UpdatePins();
                    Render();
                });
                button.Dock = DockStyle.None;
                button.Size = new Size(Math.Max(100, list.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 8), LogicalToDeviceUnits(32));
                list.Controls.Add(button);
            }
        }
        finally { list.ResumeLayout(); }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (hook != 0) UnhookWinEvent(hook);
            timer.Dispose();
            hotkeys.Dispose();
            foreach (var (window, entry) in pins)
            {
                if (GetWindowThreadProcessId(window, out uint pid) == entry.Thread && pid == entry.Process)
                    SetTopmost(window, false);
                entry.Frame.Dispose();
            }
            pins.Clear();
        }
        base.Dispose(disposing);
    }

    private sealed class Frame : Form
    {
        public Frame()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Theme.Accent;
            Opacity = .75;
            TopMost = true;
        }

        protected override bool ShowWithoutActivation => true;
        protected override CreateParams CreateParams
        {
            get { var parameters = base.CreateParams; parameters.ExStyle |= 0x080800A0; return parameters; }
        }

        public void Follow(nint window, Rectangle rect)
        {
            if (rect.Width <= 4 || rect.Height <= 4) { Hide(); return; }
            if (Size != rect.Size || Region is null)
            {
                var outline = new Region(new Rectangle(Point.Empty, rect.Size));
                outline.Exclude(new Rectangle(2, 2, rect.Width - 4, rect.Height - 4));
                var previous = Region;
                Region = outline;
                previous?.Dispose();
            }
            if (Bounds != rect) Bounds = rect;
            if (!Visible) Show();
            nint above = GetWindow(window, 3); // GW_HWNDPREV
            if (above != Handle)
                SetWindowPos(Handle, above == 0 ? -1 : above, 0, 0, 0, 0, 0x13);
        }
    }

    private delegate void WinEvent(nint hook, uint type, nint window, int obj, int child, uint thread, uint time);
    [DllImport("user32.dll")] private static extern nint SetWinEventHook(uint min, uint max, nint module, WinEvent callback, uint process, uint thread, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(nint hook);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern nint GetShellWindow();
    [DllImport("user32.dll")] private static extern nint GetWindow(nint window, uint relation);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint process);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] private static extern bool IsIconic(nint window);
    [DllImport("user32.dll")] private static extern short GetKeyState(int key);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowLong(nint window, int index);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint window, StringBuilder name, int count);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint window, StringBuilder name, int count);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint window, int attr, out Rectangle value, int size);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint window, int attr, out int value, int size);
}
