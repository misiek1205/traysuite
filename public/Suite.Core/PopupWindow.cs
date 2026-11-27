using System.Runtime.InteropServices;

namespace Suite.Core;

public class PopupWindow : Form
{
    private long lastDismissal = -1000;
    private Point dismissalPoint;

    protected Point AnchorPoint { get; private set; }
    protected Rectangle WorkArea { get; private set; }

    public PopupWindow()
    {
        var theme = PopupAppearance.Current;
        ShowInTaskbar = false;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96, 96);
        TopMost = true;
        KeyPreview = true;
        Font = PopupAppearance.CaptionFont;
        BackColor = theme.Background;
        ForeColor = theme.Text;
        Padding = new Padding(1);
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        DpiChanged += (_, _) => BeginInvoke(() => { ShapeWindow(); Place(); });
    }

    public void Open(Point position)
    {
        AnchorPoint = position;
        WorkArea = Screen.FromPoint(position).WorkingArea;
        if (!Visible)
            Location = position;
        Place();
        Show();
        Place();
        Activate();
    }

    public void Reopen() => Open(AnchorPoint);

    public void Toggle(Point position)
    {
        if (Visible)
            Hide();
        else if (Environment.TickCount64 - lastDismissal > 250
            || Math.Abs(position.X - dismissalPoint.X) > 4 || Math.Abs(position.Y - dismissalPoint.Y) > 4)
            Open(position);
        lastDismissal = -1000;
    }

    public static Point ClockAnchor()
    {
        var screen = Screen.PrimaryScreen;
        var taskbar = FindWindow("Shell_TrayWnd", null);
        if (taskbar != 0 && GetWindowRect(taskbar, out var bounds))
            return bounds.Right - bounds.Left >= bounds.Bottom - bounds.Top
                ? new Point(bounds.Right - 20, (bounds.Top + bounds.Bottom) / 2)
                : new Point((bounds.Left + bounds.Right) / 2, bounds.Bottom - 20);
        return screen is null ? Cursor.Position : new Point(screen.WorkingArea.Right - 20, screen.WorkingArea.Bottom);
    }

    protected virtual void Place() => Place(352, 310);

    protected void Place(int logicalWidth, int logicalHeight)
    {
        if (WorkArea.IsEmpty)
            return;
        int gap = LogicalToDeviceUnits(12);
        int width = Math.Min(LogicalToDeviceUnits(logicalWidth), Math.Max(1, WorkArea.Width - 2 * gap));
        int height = Math.Min(LogicalToDeviceUnits(logicalHeight), Math.Max(1, WorkArea.Height - 2 * gap));
        int x = Math.Clamp(AnchorPoint.X - width + LogicalToDeviceUnits(24), WorkArea.Left + gap,
            Math.Max(WorkArea.Left + gap, WorkArea.Right - width - gap));
        int preferredY = AnchorPoint.Y >= WorkArea.Top + WorkArea.Height / 2
            ? AnchorPoint.Y - height - gap : AnchorPoint.Y + gap;
        int y = Math.Clamp(preferredY, WorkArea.Top + gap, Math.Max(WorkArea.Top + gap, WorkArea.Bottom - height - gap));
        Bounds = new Rectangle(x, y, width, height);
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        if (Visible && Enabled)
        {
            lastDismissal = Environment.TickCount64;
            dismissalPoint = Cursor.Position;
            Hide();
        }
    }

    protected override bool ProcessDialogKey(Keys keyData)
    {
        if (keyData != Keys.Escape)
            return base.ProcessDialogKey(keyData);
        Hide();
        return true;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ClassStyle |= 0x00020000;
            return parameters;
        }
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        ShapeWindow();
    }

    private void ShapeWindow()
    {
        if (Width <= 0 || Height <= 0)
            return;
        using var outline = PopupAppearance.Rounded(new RectangleF(0, 0, Width, Height), LogicalToDeviceUnits(19));
        Region = new Region(outline);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var theme = PopupAppearance.Current;
        PopupAppearance.Surface(e.Graphics, new RectangleF(.5f, .5f, Width - 1, Height - 1),
            LogicalToDeviceUnits(19), theme.Background, theme.Border);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint FindWindow(string className, string? title);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint window, out NativeRect rect);
}
