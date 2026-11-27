using System.Runtime.InteropServices;

namespace FocusDim;

internal sealed class FocusWindow : ToolWindow
{
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 60 };
    private readonly Dictionary<string, Shade> shades = [];
    private readonly TrackBar strength = new() { Minimum = 15, Maximum = 80, Value = 50, TickFrequency = 10 };
    private bool paused;
    private nint lastExternal = ForegroundWindow.External();

    [STAThread]
    private static int Main(string[] args) => Run("FocusDim", args, _ => new FocusWindow());

    private FocusWindow() : base("FocusDim", 435, 245)
    {
        TopMost = true;
        var toggle = ActionButton("Pause dimming", () =>
        {
            paused = !paused;
            UpdateShades();
        });
        AddRow(new Label { Text = "Dimming strength", TextAlign = ContentAlignment.MiddleLeft }, 28);
        AddRow(strength, 49);
        AddRow(Actions(toggle, ActionButton("End FocusDim", Close)));
        timer.Tick += (_, _) =>
        {
            toggle.Text = paused ? "Resume dimming" : "Pause dimming";
            UpdateShades();
        };
        Shown += (_, _) =>
        {
            UpdateShades();
            timer.Start();
        };
        Status("Switch apps normally. FocusDim follows the active window.");
    }

    private void UpdateShades()
    {
        var target = ForegroundWindow.External();
        if (target != 0) lastExternal = target;
        Rectangle active = ForegroundWindow.Bounds(lastExternal);
        if (paused || active.IsEmpty)
        {
            foreach (var shade in shades.Values) shade.Hide();
            return;
        }
        var screens = Screen.AllScreens;
        var names = screens.Select(screen => screen.DeviceName).ToHashSet();
        foreach (string old in shades.Keys.Where(name => !names.Contains(name)).ToArray())
        {
            shades[old].Dispose();
            shades.Remove(old);
        }
        foreach (var screen in screens)
        {
            if (!shades.TryGetValue(screen.DeviceName, out var shade))
                shades.Add(screen.DeviceName, shade = new Shade());
            Rectangle bounds = screen.Bounds;
            Rectangle cutout = Rectangle.Intersect(active, bounds);
            shade.UpdateRegion(bounds, cutout, strength.Value / 100d);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            timer.Dispose();
            foreach (var shade in shades.Values) shade.Dispose();
        }
        base.Dispose(disposing);
    }
}

internal sealed class Shade : Form
{
    private Rectangle lastBounds, lastCutout;

    public Shade()
    {
        AutoScaleMode = AutoScaleMode.None;
        BackColor = Color.Black;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
    }

    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= 0x08000000 | 0x00000020 | 0x00000080;
            return parameters;
        }
    }

    public void UpdateRegion(Rectangle bounds, Rectangle cutout, double opacity)
    {
        if (bounds != lastBounds || cutout != lastCutout)
        {
            Bounds = bounds;
            var region = new Region(new Rectangle(Point.Empty, bounds.Size));
            if (!cutout.IsEmpty)
            {
                cutout.Offset(-bounds.X, -bounds.Y);
                region.Exclude(cutout);
                cutout.Offset(bounds.X, bounds.Y);
            }
            var previous = Region;
            Region = region;
            previous?.Dispose();
            lastBounds = bounds;
            lastCutout = cutout;
        }
        if (Opacity != opacity) Opacity = opacity;
        if (!Visible) Show();
    }
}

internal static class ForegroundWindow
{
    public static nint External()
    {
        nint window = GetForegroundWindow();
        GetWindowThreadProcessId(window, out uint pid);
        return pid == Environment.ProcessId ? 0 : window;
    }

    public static Rectangle Bounds(nint window)
    {
        if (window == 0 || !IsWindowVisible(window) || IsIconic(window)
            || window == GetDesktopWindow() || window == GetShellWindow())
            return Rectangle.Empty;
        if (DwmGetWindowAttribute(window, 9, out var frame, Marshal.SizeOf<Frame>()) != 0
            && !GetWindowRect(window, out frame)) return Rectangle.Empty;
        return Rectangle.FromLTRB(frame.Left, frame.Top, frame.Right, frame.Bottom);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Frame { public int Left, Top, Right, Bottom; }
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint window, int attribute, out Frame value, int size);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern nint GetDesktopWindow();
    [DllImport("user32.dll")] private static extern nint GetShellWindow();
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint window, out Frame value);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] private static extern bool IsIconic(nint window);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint pid);
}
