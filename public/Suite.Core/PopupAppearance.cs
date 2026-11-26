using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Suite.Core;

public sealed record PopupAppearance(Color Background, Color Card, Color Highlight, Color Border,
    Color Text, Color Muted, Color Accent, Color Track, Color Hover, Color Day, Color Evening, Color HeroEdge)
{
    public static PopupAppearance Current { get; } = Read();

    public static readonly Font TitleFont = new("Segoe UI Variable Display", 21, FontStyle.Bold);
    public static readonly Font CaptionFont = new("Segoe UI", 9);
    public static readonly Font LabelFont = new("Segoe UI Semibold", 10);
    public static readonly Font ValueFont = new("Segoe UI Variable Display", 15, FontStyle.Bold);
    public static readonly Font KeyFont = new("Cascadia Mono", 8.5f);
    public Color OnAccent => SystemInformation.HighContrast ? SystemColors.HighlightText
        : Accent.GetBrightness() > .62f ? ColorOf(0x121227) : Color.White;

    private static PopupAppearance Read()
    {
        if (SystemInformation.HighContrast)
            return new(SystemColors.Window, SystemColors.Window, SystemColors.Window, SystemColors.WindowText,
                SystemColors.WindowText, SystemColors.GrayText, SystemColors.Highlight, SystemColors.GrayText,
                SystemColors.Control, SystemColors.WindowText, SystemColors.WindowText, SystemColors.Window);

        var mode = Application.ColorMode;
        if (mode == SystemColorMode.System)
        {
            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
                mode = Application.SystemColorMode;
            else
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                mode = key?.GetValue("AppsUseLightTheme", 1) is 0 ? SystemColorMode.Dark : SystemColorMode.Classic;
            }
        }
        return mode != SystemColorMode.Dark
            ? new(ColorOf(0xf5f5f9), ColorOf(0xffffff), ColorOf(0xefedfb), ColorOf(0xdad9e5),
                ColorOf(0x201e2c), ColorOf(0x5f5d72), ColorOf(0x5145ad), ColorOf(0xdcd9e9), ColorOf(0xedeaf8),
                ColorOf(0x874819), ColorOf(0x6443a3), ColorOf(0xf5e5f0))
            : new(ColorOf(0x191922), ColorOf(0x23232f), ColorOf(0x30294a), ColorOf(0x3b394e),
                ColorOf(0xf7f5ff), ColorOf(0xb9b5cc), ColorOf(0xc4b6ff), ColorOf(0x484459), ColorOf(0x343147),
                ColorOf(0xffcb90), ColorOf(0xd9bfff), ColorOf(0x3b2942));
    }

    internal static Color ColorOf(int rgb) => Color.FromArgb(unchecked((int)0xff000000) | rgb);

    public static Color Tint(Color surface, Color accent, double amount) =>
        Color.FromArgb(
            (int)Math.Round(surface.R + (accent.R - surface.R) * amount),
            (int)Math.Round(surface.G + (accent.G - surface.G) * amount),
            (int)Math.Round(surface.B + (accent.B - surface.B) * amount));

    public static GraphicsPath Rounded(RectangleF rect, float radius)
    {
        var path = new GraphicsPath();
        if (rect.Width <= 0 || rect.Height <= 0)
            return path;
        float diameter = Math.Min(radius * 2, Math.Min(rect.Width, rect.Height));
        if (diameter <= 0)
        {
            path.AddRectangle(rect);
            return path;
        }

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

    internal static void PaintParentSurface(Control child, PaintEventArgs e)
    {
        void Fill(Color color)
        {
            using var brush = new SolidBrush(color);
            e.Graphics.FillRectangle(brush, child.ClientRectangle);
        }

        var offset = child.Location;
        for (var parent = child.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent is PopupCard card)
            {
                Fill(ParentColor(card));
                var state = e.Graphics.Save();
                e.Graphics.TranslateTransform(-offset.X, -offset.Y);
                card.PaintSurface(e.Graphics);
                e.Graphics.Restore(state);
                return;
            }
            if (parent.BackColor.A == 255)
            {
                Fill(parent.BackColor);
                return;
            }
            offset.Offset(parent.Location);
        }
        Fill(Current.Background);
    }

    internal static Color ParentColor(Control child)
    {
        for (var parent = child.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent is PopupCard card)
                return card.Fill;
            if (parent.BackColor.A == 255)
                return parent.BackColor;
        }
        return Current.Background;
    }
}

public class PopupCard : Panel
{
    private readonly Color fill;
    private readonly bool featured;

    public PopupCard(Color fill, bool featured = false)
    {
        this.fill = fill;
        this.featured = featured;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        PopupAppearance.PaintParentSurface(this, e);
        PaintSurface(e.Graphics);
    }

    internal Color Fill => fill;

    internal void PaintSurface(Graphics graphics)
    {
        var theme = PopupAppearance.Current;
        var bounds = new RectangleF(.5f, .5f, Width - 1, Height - 1);
        int radius = LogicalToDeviceUnits(featured ? 19 : 14);
        if (!featured || SystemInformation.HighContrast || Width <= 2 || Height <= 2)
        {
            PopupAppearance.Surface(graphics, bounds, radius, fill, theme.Border);
            return;
        }

        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = PopupAppearance.Rounded(bounds, radius);
        using var gradient = new LinearGradientBrush(bounds, fill, theme.HeroEdge, 20f);
        using var line = new Pen(PopupAppearance.Tint(theme.Border, theme.Accent, .23));
        graphics.FillPath(gradient, path);
        graphics.DrawPath(line, path);
    }
}

public enum PopupButtonStyle { Standard, Primary, Quiet }

public sealed class PopupButton : Button
{
    private bool hovered;
    private bool pressed;
    private bool selected;
    private readonly PopupGlyph? glyph;
    private readonly bool iconOnly;
    private readonly bool leftAligned;
    private readonly bool inset;
    private readonly PopupButtonStyle style;

    public PopupButton(PopupGlyph? glyph = null, bool iconOnly = false, bool leftAligned = false, bool inset = false,
        PopupButtonStyle style = PopupButtonStyle.Standard)
    {
        this.glyph = glyph;
        this.iconOnly = iconOnly;
        this.leftAligned = leftAligned;
        this.inset = inset;
        this.style = style;
        SetStyle(ControlStyles.UserPaint | ControlStyles.Opaque | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        ForeColor = PopupAppearance.Current.Text;
        Font = PopupAppearance.CaptionFont;
        Cursor = Cursors.Hand;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            SetStyle(ControlStyles.ApplyThemingImplicitly, false);
            return base.CreateParams;
        }
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Selected
    {
        get => selected;
        set
        {
            if (selected == value)
                return;
            selected = value;
            Invalidate();
        }
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

    protected override void OnMouseDown(MouseEventArgs e)
    {
        pressed = e.Button == MouseButtons.Left;
        Invalidate();
        base.OnMouseDown(e);
    }

    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        pressed = false;
        Invalidate();
        base.OnMouseCaptureChanged(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        PopupAppearance.PaintParentSurface(this, e);
        var theme = PopupAppearance.Current;
        bool primary = Enabled && style == PopupButtonStyle.Primary;
        bool quiet = style == PopupButtonStyle.Quiet;
        Color surface = primary ? theme.Accent : selected ? theme.Highlight : inset && BackColor.A == 255 ? BackColor
            : quiet || inset ? PopupAppearance.ParentColor(this) : theme.Card;
        Color fill = !Enabled ? PopupAppearance.Tint(surface, theme.Background, .55)
            : hovered ? PopupAppearance.Tint(surface, primary ? theme.Text : theme.Accent, pressed ? .19 : .085) : surface;
        Color text = !Enabled ? theme.Muted : primary ? theme.OnAccent : selected ? theme.Accent : ForeColor;
        Color border = Focused && ShowFocusCues ? theme.Accent
            : selected ? PopupAppearance.Tint(theme.Border, theme.Accent, .45)
            : primary || inset || quiet ? fill : theme.Border;
        if (primary && Focused && ShowFocusCues)
            border = theme.OnAccent;
        PopupAppearance.Surface(e.Graphics, new RectangleF(.5f, .5f, Width - 1, Height - 1),
            LogicalToDeviceUnits(iconOnly ? 10 : 11), fill, border);

        int pad = LogicalToDeviceUnits(leftAligned ? 11 : 7);
        var bounds = Rectangle.Inflate(ClientRectangle, -pad, 0);
        int iconSize = LogicalToDeviceUnits(iconOnly ? 19 : 16);
        if (glyph is { } symbol)
        {
            var textSize = TextRenderer.MeasureText(e.Graphics, Text, Font, Size.Empty,
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            int x = iconOnly ? (Width - iconSize) / 2 : leftAligned ? pad
                : Math.Max(pad, (Width - iconSize - pad - textSize.Width) / 2);
            PopupIcons.Draw(e.Graphics, symbol, new Rectangle(x, (Height - iconSize) / 2, iconSize, iconSize), text);
            if (iconOnly)
                return;
            bounds = Rectangle.FromLTRB(x + iconSize + pad, 0, Width - pad, Height);
        }
        if (bounds.Width <= 0)
            return;
        TextRenderer.DrawText(e.Graphics, Text, Font, bounds, text,
            (leftAligned || glyph is not null ? TextFormatFlags.Left : TextFormatFlags.HorizontalCenter)
            | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine
            | (UseMnemonic ? TextFormatFlags.Default : TextFormatFlags.NoPrefix));
    }
}

public sealed class PopupSlider : TrackBar
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
        int thickness = LogicalToDeviceUnits(6);
        using var track = new Pen(theme.Track, thickness) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var active = new Pen(Enabled ? theme.Accent : theme.Muted, thickness)
            { StartCap = LineCap.Round, EndCap = LineCap.Round };
        e.Graphics.DrawLine(track, channel.Left, y, channel.Right, y);
        int origin = Minimum < 0 ? (channel.Left + channel.Right) / 2 : channel.Left;
        if (x != origin)
            e.Graphics.DrawLine(active, origin, y, x, y);
        int radius = LogicalToDeviceUnits(8);
        var knobBounds = new Rectangle(x - radius, y - radius, radius * 2, radius * 2);
        using var shadow = new SolidBrush(Color.FromArgb(45, Color.Black));
        e.Graphics.FillEllipse(shadow, knobBounds.X, knobBounds.Y + LogicalToDeviceUnits(2), knobBounds.Width, knobBounds.Height);
        using var knob = new SolidBrush(Enabled && !SystemInformation.HighContrast ? Color.White
            : Enabled ? theme.OnAccent : theme.Track);
        using var edge = new Pen(Enabled ? theme.Accent : theme.Muted);
        e.Graphics.FillEllipse(knob, knobBounds);
        e.Graphics.DrawEllipse(edge, knobBounds);
        if (Focused && ShowFocusCues)
        {
            using var focus = new Pen(theme.Text);
            int outside = radius + LogicalToDeviceUnits(3);
            e.Graphics.DrawEllipse(focus, x - outside, y - outside, outside * 2, outside * 2);
        }
    }

    [DllImport("user32.dll")]
    private static extern nint SendMessage(nint handle, uint message, nint wParam, out NativeRect rectangle);
}
