using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Suite.Core;

public readonly record struct ModuleIdentity(PopupGlyph Glyph, Color Light, Color Dark)
{
    public static ModuleIdentity For(string id)
    {
        var (glyph, light, dark) = id switch
        {
            "Dimmli" => (PopupGlyph.Sun, 0xf5af4b, 0xa44918),
            "Awake" => (PopupGlyph.Cup, 0xd99874, 0x914634),
            "ClipPlain" => (PopupGlyph.Clipboard, 0x66a4ee, 0x2a52aa),
            "PinWin" => (PopupGlyph.Pin, 0xb589ee, 0x6534a8),
            "PowerFlip" => (PopupGlyph.Bolt, 0x5fc9a9, 0x166c59),
            "Sweep" => (PopupGlyph.Sparkle, 0x59c3b9, 0x126963),
            "Boost" => (PopupGlyph.Gamepad, 0xe48aa0, 0x9e3253),
            "Pulse" => (PopupGlyph.Pulse, 0xec8c8b, 0xa03544),
            "Volly" => (PopupGlyph.Sound, 0x819eee, 0x4a46ab),
            "SnapKit" => (PopupGlyph.Scan, 0x8ba9ed, 0x414fa3),
            "Unlocker" => (PopupGlyph.Lock, 0xdfac66, 0x93551f),
            "NetWatch" => (PopupGlyph.Network, 0x64b6df, 0x225e90),
            _ => (PopupGlyph.Grid, 0x9e9afb, 0x4944a5)
        };
        return new(glyph, PopupAppearance.ColorOf(light), PopupAppearance.ColorOf(dark));
    }

    public void Draw(Graphics graphics, Rectangle bounds)
    {
        if (bounds.Width < 2 || bounds.Height < 2)
            return;
        var state = graphics.Save();
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var tile = new RectangleF(bounds.X + .5f, bounds.Y + .5f, bounds.Width - 1, bounds.Height - 1);
        using var path = PopupAppearance.Rounded(tile, bounds.Width * .27f);
        bool highContrast = SystemInformation.HighContrast;
        using Brush paint = highContrast ? new SolidBrush(SystemColors.Highlight)
            : new LinearGradientBrush(tile, Light, Dark, 62f);
        using var edge = new Pen(highContrast ? SystemColors.WindowText : Color.FromArgb(105, Dark));
        graphics.FillPath(paint, path);
        graphics.DrawPath(edge, path);
        int inset = (int)Math.Round(bounds.Width * .17);
        bounds.Inflate(-inset, -inset);
        PopupIcons.Draw(graphics, Glyph, bounds, highContrast ? SystemColors.HighlightText : Color.White);
        graphics.Restore(state);
    }

    public Icon CreateTrayIcon(int size)
    {
        using var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
            Draw(graphics, new Rectangle(0, 0, size, size));
        nint handle = bitmap.GetHicon();
        try
        {
            using var icon = Icon.FromHandle(handle);
            return (Icon)icon.Clone();
        }
        finally { DestroyIcon(handle); }
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(nint icon);
}

public sealed class ModuleMark : Control
{
    private readonly ModuleIdentity identity;
    private readonly int size;

    public ModuleMark(string id, int size = 44)
    {
        identity = ModuleIdentity.For(id);
        this.size = size;
        SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw, true);
        BackColor = Color.Transparent;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        int edge = Math.Min(LogicalToDeviceUnits(size), Math.Min(Width, Height));
        identity.Draw(e.Graphics, new Rectangle((Width - edge) / 2, (Height - edge) / 2, edge, edge));
    }

    protected override void OnPaintBackground(PaintEventArgs e) => PopupAppearance.PaintParentSurface(this, e);
}
