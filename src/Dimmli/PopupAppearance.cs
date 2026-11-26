using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Dimmli;

internal sealed record PopupAppearance(Color Background, Color Card, Color Highlight, Color Border,
    Color Text, Color Muted, Color Accent, Color Track, Color Hover)
{
    public static PopupAppearance Current { get; } = Read();

    public static readonly Font TitleFont = new("Segoe UI", 19, FontStyle.Bold);
    public static readonly Font CaptionFont = new("Segoe UI", 9);
    public static readonly Font LabelFont = new("Segoe UI", 10, FontStyle.Bold);

    private static PopupAppearance Read()
    {
        if (SystemInformation.HighContrast)
            return new(SystemColors.Window, SystemColors.Window, SystemColors.Window, SystemColors.WindowText,
                SystemColors.WindowText, SystemColors.GrayText, SystemColors.Highlight, SystemColors.GrayText,
                SystemColors.Control);

        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is 1
            ? new(ColorOf(0xf3f6fa), Color.White, ColorOf(0xeaf2ff), ColorOf(0xd9e2ee),
                ColorOf(0x132239), ColorOf(0x465970), ColorOf(0x145ad0), ColorOf(0xc6d3e5), ColorOf(0xe8effa))
            : new(ColorOf(0x131a26), ColorOf(0x1c2636), ColorOf(0x1e304b), ColorOf(0x3b506c),
                ColorOf(0xf4f8ff), ColorOf(0xbac9dc), ColorOf(0x8bbdff), ColorOf(0x43536c), ColorOf(0x2c405c));
    }

    private static Color ColorOf(int rgb) => Color.FromArgb(unchecked((int)0xff000000) | rgb);

    public static GraphicsPath Rounded(RectangleF rect, float radius)
    {
        var path = new GraphicsPath();
        float diameter = Math.Min(radius * 2, Math.Min(rect.Width, rect.Height));
        if (diameter <= 0)
            return path;

        path.AddArc(rect.Left, rect.Top, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Top, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static void Surface(Graphics graphics, RectangleF rect, int radius, Color fill, Color border)
    {
        if (rect.Width <= 0 || rect.Height <= 0)
            return;

        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Rounded(rect, radius);
        using var brush = new SolidBrush(fill);
        using var outline = new Pen(border);
        graphics.FillPath(brush, path);
        graphics.DrawPath(outline, path);
    }
}

internal class PopupCard(Color fill) : Panel
{

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        base.OnPaintBackground(e);
        PopupAppearance.Surface(e.Graphics, new RectangleF(.5f, .5f, Width - 1, Height - 1),
            LogicalToDeviceUnits(12), fill, PopupAppearance.Current.Border);
    }
}

internal sealed class PopupButton : Button
{
    private bool hovered;

    public PopupButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        Cursor = Cursors.Hand;
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        hovered = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        hovered = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var theme = PopupAppearance.Current;
        Color fill = hovered && Enabled ? theme.Hover : theme.Card;
        Color text = Enabled ? ForeColor : theme.Muted;
        PopupAppearance.Surface(e.Graphics, new RectangleF(.5f, .5f, Width - 1, Height - 1),
            LogicalToDeviceUnits(8), fill, Focused && ShowFocusCues ? theme.Accent : theme.Border);
        TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, text, TextFormatFlags.HorizontalCenter
            | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
    }
}

internal sealed class PopupSlider : TrackBar
{
    private const uint GetThumbRectangle = 0x0400 + 25;
    private const uint GetChannelRectangle = 0x0400 + 26;

    public PopupSlider()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw, true);
        Cursor = Cursors.Hand;
    }

    protected override void OnValueChanged(EventArgs e)
    {
        base.OnValueChanged(e);
        Invalidate();
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        Invalidate();
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        Invalidate();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (!IsHandleCreated)
            return;

        var theme = PopupAppearance.Current;
        SendMessage(Handle, GetThumbRectangle, 0, out var thumb);
        SendMessage(Handle, GetChannelRectangle, 0, out var channel);
        int x = (thumb.Left + thumb.Right) / 2;
        int y = (thumb.Top + thumb.Bottom) / 2;
        if (channel.Right <= channel.Left || x <= 0 || y <= 0)
            return;

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        int thickness = LogicalToDeviceUnits(5);
        using var track = new Pen(theme.Track, thickness) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var active = new Pen(Enabled ? theme.Accent : theme.Muted, thickness)
            { StartCap = LineCap.Round, EndCap = LineCap.Round };
        e.Graphics.DrawLine(track, channel.Left, y, channel.Right, y);
        int origin = Minimum < 0 ? (channel.Left + channel.Right) / 2 : channel.Left;
        if (x != origin)
            e.Graphics.DrawLine(active, origin, y, x, y);
        int radius = LogicalToDeviceUnits(7);
        using var knob = new SolidBrush(Enabled ? theme.Accent : theme.Muted);
        e.Graphics.FillEllipse(knob, x - radius, y - radius, radius * 2, radius * 2);
        if (Focused && ShowFocusCues)
        {
            using var focus = new Pen(theme.Text);
            int outside = radius + LogicalToDeviceUnits(3);
            e.Graphics.DrawEllipse(focus, x - outside, y - outside, outside * 2, outside * 2);
        }
    }

    [DllImport("user32.dll")]
    private static extern nint SendMessage(nint handle, uint message, nint wParam, out MonitorNative.Rect rectangle);
}
