using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Dimmli;

internal sealed record PopupAppearance(Color Background, Color Card, Color Highlight, Color Border,
    Color Text, Color Muted, Color Accent, Color Track, Color Hover, Color Day, Color Evening, Color HeroEdge)
{
    public static PopupAppearance Current { get; } = Read();

    public static readonly Font TitleFont = new("Segoe UI", 22, FontStyle.Bold);
    public static readonly Font CaptionFont = new("Segoe UI", 9);
    public static readonly Font LabelFont = new("Segoe UI", 10, FontStyle.Bold);
    public static readonly Font ValueFont = new("Segoe UI", 14, FontStyle.Bold);

    private static PopupAppearance Read()
    {
        if (SystemInformation.HighContrast)
            return new(SystemColors.Window, SystemColors.Window, SystemColors.Window, SystemColors.WindowText,
                SystemColors.WindowText, SystemColors.GrayText, SystemColors.Highlight, SystemColors.GrayText,
                SystemColors.Control, SystemColors.WindowText, SystemColors.WindowText, SystemColors.Window);

        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is 1
            ? new(ColorOf(0xf1f4f8), ColorOf(0xfbfdff), ColorOf(0xeaf3ff), ColorOf(0xd7dfeb),
                ColorOf(0x162236), ColorOf(0x4d5e75), ColorOf(0x1458ae), ColorOf(0xc6d3e5), ColorOf(0xe6effb),
                ColorOf(0x884a14), ColorOf(0x634caa), ColorOf(0xe5e8ff))
            : new(ColorOf(0x111620), ColorOf(0x1a222f), ColorOf(0x1a2c44), ColorOf(0x344358),
                ColorOf(0xf1f6ff), ColorOf(0xa9b7cc), ColorOf(0x8acfff), ColorOf(0x42516a), ColorOf(0x293b52),
                ColorOf(0xffce95), ColorOf(0xc6b4ff), ColorOf(0x2a274a));
    }

    private static Color ColorOf(int rgb) => Color.FromArgb(unchecked((int)0xff000000) | rgb);

    public static Color Tint(Color surface, Color accent, double amount) =>
        Color.FromArgb(
            (int)Math.Round(surface.R + (accent.R - surface.R) * amount),
            (int)Math.Round(surface.G + (accent.G - surface.G) * amount),
            (int)Math.Round(surface.B + (accent.B - surface.B) * amount));

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

internal class PopupCard(Color fill, bool featured = false) : Panel
{
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        base.OnPaintBackground(e);
        var theme = PopupAppearance.Current;
        var bounds = new RectangleF(.5f, .5f, Width - 1, Height - 1);
        int radius = LogicalToDeviceUnits(featured ? 20 : 13);
        if (!featured || SystemInformation.HighContrast || Width <= 2 || Height <= 2)
        {
            PopupAppearance.Surface(e.Graphics, bounds, radius, fill, theme.Border);
            return;
        }

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = PopupAppearance.Rounded(bounds, radius);
        using var gradient = new LinearGradientBrush(bounds, fill, theme.HeroEdge, 20f);
        using var line = new Pen(Color.FromArgb(135, theme.Accent));
        e.Graphics.FillPath(gradient, path);
        e.Graphics.DrawPath(line, path);
    }
}

internal sealed class PopupButton : Button
{
    private bool hovered;
    private readonly PopupGlyph? glyph;
    private readonly bool iconOnly;
    private readonly bool leftAligned;
    private readonly bool inset;

    public PopupButton(PopupGlyph? glyph = null, bool iconOnly = false, bool leftAligned = false, bool inset = false)
    {
        this.glyph = glyph;
        this.iconOnly = iconOnly;
        this.leftAligned = leftAligned;
        this.inset = inset;
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
        Color surface = inset ? BackColor : theme.Card;
        Color fill = hovered && Enabled ? PopupAppearance.Tint(surface, theme.Hover, .82) : surface;
        Color text = Enabled ? ForeColor : theme.Muted;
        Color border = Focused && ShowFocusCues ? theme.Accent : inset ? fill : theme.Border;
        PopupAppearance.Surface(e.Graphics, new RectangleF(.5f, .5f, Width - 1, Height - 1),
            LogicalToDeviceUnits(iconOnly ? 10 : 9), fill, border);

        var bounds = ClientRectangle;
        int pad = LogicalToDeviceUnits(8);
        int iconSize = LogicalToDeviceUnits(iconOnly ? 18 : 17);
        if (glyph is { } symbol)
        {
            int x = iconOnly ? (Width - iconSize) / 2 : pad;
            PopupIcons.Draw(e.Graphics, symbol, new Rectangle(x, (Height - iconSize) / 2, iconSize, iconSize), text);
            if (iconOnly)
                return;
            bounds.X += iconSize + pad * 2;
            bounds.Width -= iconSize + pad * 2;
        }
        TextRenderer.DrawText(e.Graphics, Text, Font, bounds, text,
            (leftAligned ? TextFormatFlags.Left : TextFormatFlags.HorizontalCenter)
            | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
    }
}

internal sealed class PopupSlider : TrackBar
{
    private const uint GetThumbRectangle = 0x0400 + 25;
    private const uint GetChannelRectangle = 0x0400 + 26;
    private int dragStartX, dragStartValue, dragWidth;
    private bool fineDrag;
    public bool Dragging { get; private set; }

    public PopupSlider(bool transparent = false)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw, true);
        if (transparent)
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }
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

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!e.Shift || e.Control || e.Alt)
            return;

        int direction = e.KeyCode switch
        {
            Keys.Right or Keys.Up or Keys.PageUp => 1,
            Keys.Left or Keys.Down or Keys.PageDown => -1,
            _ => 0
        };
        if (direction == 0)
            return;
        e.SuppressKeyPress = true;
        ChangeFromInput(Value + direction);
    }

    protected override void WndProc(ref Message message)
    {
        int x = unchecked((short)(long)message.LParam);
        if (message.Msg is 0x0201 or 0x0203 && Enabled) // WM_LBUTTONDOWN / WM_LBUTTONDBLCLK
        {
            SendMessage(Handle, GetChannelRectangle, 0, out var channel);
            SendMessage(Handle, GetThumbRectangle, 0, out var thumb);
            dragWidth = channel.Right - channel.Left;
            if (dragWidth > 0)
            {
                Focus();
                Dragging = true;
                Capture = true;
                fineDrag = (ModifierKeys & Keys.Shift) != 0;
                if (!fineDrag && (x < thumb.Left || x >= thumb.Right))
                    ChangeFromInput(Minimum + (int)Math.Round((double)(x - channel.Left) * (Maximum - Minimum) / dragWidth));
                dragStartX = x;
                dragStartValue = Value;
                OnMouseDown(new MouseEventArgs(MouseButtons.Left, 1, x, 0, 0));
                return;
            }
        }
        else if (message.Msg == 0x0200 && Dragging) // WM_MOUSEMOVE
        {
            bool fine = (ModifierKeys & Keys.Shift) != 0;
            if (fine != fineDrag)
            {
                fineDrag = fine;
                dragStartX = x;
                dragStartValue = Value;
            }
            double unit = fine ? Math.Max(1, LogicalToDeviceUnits(6)) : (double)dragWidth / (Maximum - Minimum);
            ChangeFromInput(dragStartValue + (int)Math.Truncate((x - dragStartX) / unit));
            return;
        }
        else if (message.Msg == 0x0202 && Dragging) // WM_LBUTTONUP
        {
            Dragging = false;
            Capture = false;
            OnMouseUp(new MouseEventArgs(MouseButtons.Left, 1, x, 0, 0));
            return;
        }
        else if (message.Msg == 0x0215) // WM_CAPTURECHANGED
        {
            Dragging = false;
        }
        base.WndProc(ref message);
    }

    private void ChangeFromInput(int value)
    {
        int adjusted = Math.Clamp(value, Minimum, Maximum);
        if (adjusted == Value)
            return;
        Value = adjusted;
        OnScroll(EventArgs.Empty);
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
        int thickness = LogicalToDeviceUnits(4);
        using var track = new Pen(theme.Track, thickness) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var active = new Pen(Enabled ? theme.Accent : theme.Muted, thickness)
            { StartCap = LineCap.Round, EndCap = LineCap.Round };
        e.Graphics.DrawLine(track, channel.Left, y, channel.Right, y);
        int origin = Minimum < 0 ? (channel.Left + channel.Right) / 2 : channel.Left;
        if (x != origin)
            e.Graphics.DrawLine(active, origin, y, x, y);
        int radius = LogicalToDeviceUnits(8);
        Color thumbColor = Enabled ? theme.Accent : theme.Muted;
        using var knob = new SolidBrush(thumbColor);
        e.Graphics.FillEllipse(knob, x - radius, y - radius, radius * 2, radius * 2);
        using var core = new SolidBrush(theme.Card);
        int coreRadius = LogicalToDeviceUnits(3);
        e.Graphics.FillEllipse(core, x - coreRadius, y - coreRadius, coreRadius * 2, coreRadius * 2);
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
