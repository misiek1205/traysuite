using System.Drawing.Drawing2D;

namespace Dimmli;

internal enum PopupGlyph { Sun, Moon, Display, Refresh, Keyboard, Save, Back }

internal static class PopupIcons
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
        }
        graphics.Restore(state);
    }
}

internal sealed class PopupIcon(PopupGlyph glyph, Color color) : Control
{
    protected override void OnPaint(PaintEventArgs e)
    {
        int size = Math.Min(LogicalToDeviceUnits(20), Math.Min(Width, Height));
        if (size > 0)
            PopupIcons.Draw(e.Graphics, glyph, new Rectangle((Width - size) / 2, (Height - size) / 2, size, size), color);
    }
}

internal sealed class BrightnessMeter : Control
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
