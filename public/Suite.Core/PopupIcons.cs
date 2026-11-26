using System.Drawing.Drawing2D;

namespace Suite.Core;

public enum PopupGlyph
{
    Sun, Moon, Display, Refresh, Keyboard, Save, Back, Grid, Cup, Clipboard, Pin, Bolt,
    Sparkle, Gamepad, Pulse, Sound, Scan, Lock, Network, Settings, More, Check, Download
}

public static class PopupIcons
{
    public static void Draw(Graphics graphics, PopupGlyph glyph, Rectangle bounds, Color color)
    {
        var state = graphics.Save();
        graphics.TranslateTransform(bounds.X, bounds.Y);
        graphics.ScaleTransform(bounds.Width / 24f, bounds.Height / 24f);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(color, 1.65f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        switch (glyph)
        {
            case PopupGlyph.Sun:
                graphics.DrawEllipse(pen, 7.5f, 7.5f, 9, 9);
                for (int i = 0; i < 8; i++)
                {
                    double angle = i * Math.PI / 4;
                    float x = (float)Math.Cos(angle);
                    float y = (float)Math.Sin(angle);
                    graphics.DrawLine(pen, 12 + 7.5f * x, 12 + 7.5f * y, 12 + 10f * x, 12 + 10f * y);
                }
                break;
            case PopupGlyph.Moon:
                using (var moon = new GraphicsPath())
                {
                    moon.AddBezier(13, 3, 4, 2, 1, 13, 7, 18);
                    moon.AddBezier(7, 18, 12, 23, 20, 20, 21, 13);
                    moon.AddBezier(21, 13, 14, 16, 9, 10, 13, 3);
                    moon.CloseFigure();
                    graphics.DrawPath(pen, moon);
                }
                break;
            case PopupGlyph.Display:
                using (var outline = PopupAppearance.Rounded(new RectangleF(3, 4.5f, 18, 12), 2))
                    graphics.DrawPath(pen, outline);
                graphics.DrawLine(pen, 12, 16.5f, 12, 21);
                graphics.DrawLine(pen, 8, 21, 16, 21);
                break;
            case PopupGlyph.Refresh:
                graphics.DrawArc(pen, 4, 4, 16, 16, 42, 297);
                graphics.DrawLines(pen, [new PointF(20, 3), new PointF(20, 9), new PointF(14, 9)]);
                break;
            case PopupGlyph.Keyboard:
                using (var outline = PopupAppearance.Rounded(new RectangleF(2, 5, 20, 14), 3))
                    graphics.DrawPath(pen, outline);
                for (int row = 9; row <= 13; row += 4)
                    for (int column = 6; column <= 18; column += 4)
                        graphics.DrawLine(pen, column, row, column + 0.4f, row);
                graphics.DrawLine(pen, 8, 16, 16, 16);
                break;
            case PopupGlyph.Save:
                using (var outline = PopupAppearance.Rounded(new RectangleF(4, 4, 16, 16), 2))
                    graphics.DrawPath(pen, outline);
                graphics.DrawLines(pen, [new PointF(8, 4), new PointF(8, 9), new PointF(16, 9), new PointF(16, 4)]);
                graphics.DrawLines(pen, [new PointF(8, 20), new PointF(8, 14.5f), new PointF(16, 14.5f), new PointF(16, 20)]);
                break;
            case PopupGlyph.Back:
                graphics.DrawLines(pen, [new PointF(11, 5), new PointF(4, 12), new PointF(11, 19)]);
                graphics.DrawLine(pen, 4, 12, 20, 12);
                break;
            case PopupGlyph.Grid:
                foreach (int x in new[] { 4, 13 })
                    foreach (int y in new[] { 4, 13 })
                        Box(x, y, 7, 7);
                break;
            case PopupGlyph.Cup:
                Box(4, 8, 12, 10, 3);
                graphics.DrawArc(pen, 13, 8, 8, 7, -90, 180);
                graphics.DrawLine(pen, 3, 21, 20, 21);
                graphics.DrawArc(pen, 8, 1, 6, 5, 90, 110);
                break;
            case PopupGlyph.Clipboard:
                Box(5, 5, 14, 16);
                Box(9, 3, 6, 4, 1);
                graphics.DrawLine(pen, 9, 12, 15, 12);
                graphics.DrawLine(pen, 9, 16, 15, 16);
                break;
            case PopupGlyph.Pin:
                Line(new(9, 4), new(17, 4), new(16, 7), new(16, 11), new(19, 14), new(7, 14),
                    new(10, 11), new(10, 7), new(9, 4));
                graphics.DrawLine(pen, 13, 14, 13, 21);
                break;
            case PopupGlyph.Bolt:
                Line(new(13, 2), new(5, 13), new(11, 13), new(10, 22), new(19, 10),
                    new(13, 10), new(13, 2));
                break;
            case PopupGlyph.Sparkle:
                Line(new(10, 3), new(12, 9), new(18, 11), new(12, 13), new(10, 19),
                    new(8, 13), new(2, 11), new(8, 9), new(10, 3));
                graphics.DrawLine(pen, 19, 14, 19, 21);
                graphics.DrawLine(pen, 16, 17.5f, 22, 17.5f);
                break;
            case PopupGlyph.Gamepad:
                Box(2, 7, 20, 11, 5);
                graphics.DrawLine(pen, 5.5f, 12, 10.5f, 12);
                graphics.DrawLine(pen, 8, 9.5f, 8, 14.5f);
                graphics.DrawLine(pen, 15, 12, 15.5f, 12);
                graphics.DrawLine(pen, 18, 14, 18.5f, 14);
                break;
            case PopupGlyph.Pulse:
                Line(new(2, 12), new(7, 12), new(10, 6), new(13, 18), new(16, 12), new(22, 12));
                break;
            case PopupGlyph.Sound:
                Line(new(4, 9), new(8, 9), new(12, 5), new(12, 19), new(8, 15), new(4, 15), new(4, 9));
                graphics.DrawArc(pen, 10, 7, 8, 10, -63, 126);
                graphics.DrawArc(pen, 9, 4, 13, 16, -60, 120);
                break;
            case PopupGlyph.Scan:
                Line(new(3, 8), new(3, 3), new(8, 3));
                Line(new(16, 3), new(21, 3), new(21, 8));
                Line(new(21, 16), new(21, 21), new(16, 21));
                Line(new(8, 21), new(3, 21), new(3, 16));
                graphics.DrawLine(pen, 7, 10, 17, 10);
                graphics.DrawLine(pen, 7, 14, 14, 14);
                break;
            case PopupGlyph.Lock:
                Box(5, 10, 14, 11, 3);
                graphics.DrawArc(pen, 8, 3, 8, 10, 180, 180);
                graphics.DrawLine(pen, 8, 8, 8, 10);
                graphics.DrawLine(pen, 12, 14, 12, 17);
                break;
            case PopupGlyph.Network:
                graphics.DrawArc(pen, 0, 5, 24, 22, 223, 94);
                graphics.DrawArc(pen, 4.5f, 10, 15, 14, 222, 96);
                graphics.DrawArc(pen, 8.5f, 15, 7, 6, 205, 130);
                graphics.DrawEllipse(pen, 11.5f, 19.5f, 1, 1);
                break;
            case PopupGlyph.Settings:
                graphics.DrawEllipse(pen, 8.5f, 8.5f, 7, 7);
                graphics.DrawEllipse(pen, 4, 4, 16, 16);
                for (int i = 0; i < 8; i++)
                {
                    double angle = i * Math.PI / 4;
                    float x = (float)Math.Cos(angle), y = (float)Math.Sin(angle);
                    graphics.DrawLine(pen, 12 + 8 * x, 12 + 8 * y, 12 + 10.5f * x, 12 + 10.5f * y);
                }
                break;
            case PopupGlyph.More:
                for (int x = 5; x <= 17; x += 6)
                    graphics.DrawEllipse(pen, x, 11, 2, 2);
                break;
            case PopupGlyph.Check:
                Line(new(5, 12), new(10, 17), new(19, 7));
                break;
            case PopupGlyph.Download:
                Line(new(7, 10), new(12, 15), new(17, 10));
                graphics.DrawLine(pen, 12, 4, 12, 15);
                Line(new(4, 16), new(4, 20), new(20, 20), new(20, 16));
                break;
        }
        graphics.Restore(state);

        void Box(float x, float y, float width, float height, float radius = 2)
        {
            using var outline = PopupAppearance.Rounded(new RectangleF(x, y, width, height), radius);
            graphics.DrawPath(pen, outline);
        }

        void Line(params PointF[] points) => graphics.DrawLines(pen, points);
    }
}

public sealed class PopupIcon(PopupGlyph glyph, Color color) : Control
{
    protected override void OnPaint(PaintEventArgs e)
    {
        int size = Math.Min(LogicalToDeviceUnits(20), Math.Min(Width, Height));
        if (size > 0)
            PopupIcons.Draw(e.Graphics, glyph, new Rectangle((Width - size) / 2, (Height - size) / 2, size, size), color);
    }
}

public sealed class BrightnessMeter : Control
{
    private int level;

    public BrightnessMeter()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Color.Transparent;
        AccessibleRole = AccessibleRole.StaticText;
    }

    public void UpdateLevel(int value)
    {
        level = value;
        AccessibleName = Texts.Get($"Average brightness {level}%", $"Średnia jasność {level}%");
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var theme = PopupAppearance.Current;
        int size = Math.Min(LogicalToDeviceUnits(82), Math.Min(Width, Height) - LogicalToDeviceUnits(9));
        if (size < LogicalToDeviceUnits(36))
            return;

        int left = (Width - size) / 2;
        int top = (Height - size) / 2;
        var dial = new Rectangle(left, top, size, size);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var track = new Pen(theme.Track, LogicalToDeviceUnits(4)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var active = new Pen(theme.Accent, LogicalToDeviceUnits(4)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        e.Graphics.DrawArc(track, dial, 138, 264);
        if (level > 0)
            e.Graphics.DrawArc(active, dial, 138, 264 * level / 100f);

        const TextFormatFlags centered = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
            TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
        TextRenderer.DrawText(e.Graphics, $"{level}%", PopupAppearance.ValueFont,
            new Rectangle(left, top + size / 4, size, size / 3), theme.Text, centered);
        TextRenderer.DrawText(e.Graphics, Texts.Get("AVG", "ŚREDNIA"), PopupAppearance.CaptionFont,
            new Rectangle(left, top + size * 3 / 5, size, size / 5), theme.Muted, centered);
    }
}
