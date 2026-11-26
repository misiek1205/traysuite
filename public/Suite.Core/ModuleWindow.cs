using System.Drawing.Drawing2D;

namespace Suite.Core;

public class ModuleWindow : PopupWindow
{
    protected static PopupAppearance Theme => PopupAppearance.Current;
    private readonly Label status;
    private readonly TableLayoutPanel layout;
    private readonly TableLayoutPanel header;
    private readonly Label subtitle;
    private readonly PopupViewport viewport;
    private readonly bool pro;
    public TableLayoutPanel Body { get; } = new()
    {
        ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top
    };

    public ModuleWindow(string title, string subtitle, int height, bool pro = false)
    {
        Text = title;
        this.pro = pro;
        LogicalHeight = height;
        layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, BackColor = Theme.Background, ColumnCount = 1, RowCount = 3
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
        Controls.Add(layout);

        header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 2, Padding = new Padding(18, 13, 18, 14),
            Margin = Padding.Empty
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 57));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, pro ? 40 : 0));
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 63));
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 37));
        var mark = new ModuleMark(title) { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 13, 0), TabStop = false };
        header.Controls.Add(mark, 0, 0);
        header.SetRowSpan(mark, 2);
        var name = Label(title, font: PopupAppearance.TitleFont);
        name.TextAlign = ContentAlignment.BottomLeft;
        header.Controls.Add(name, 1, 0);
        this.subtitle = Label(subtitle, muted: true);
        header.Controls.Add(this.subtitle, 1, 1);
        if (pro)
            header.Controls.Add(new ProBadge { Anchor = AnchorStyles.Right, Margin = Padding.Empty }, 2, 0);
        layout.Controls.Add(header, 0, 0);
        Body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        viewport = new PopupViewport(Body) { Dock = DockStyle.Fill, Margin = new Padding(18, 0, 18, 0) };
        layout.Controls.Add(viewport, 0, 1);

        status = Label("", muted: true);
        status.Margin = new Padding(18, 0, 18, 0);
        layout.Controls.Add(status, 0, 2);
    }

    protected int LogicalHeight { get; set; }
    protected virtual int LogicalWidth => 374;
    protected void SetSubtitle(string text) => subtitle.Text = text;
    protected int ScrollOffset
    {
        get => viewport.Offset;
        set => viewport.Offset = value;
    }
    protected override void Place()
    {
        int inset = LogicalToDeviceUnits(18);
        layout.RowStyles[0].Height = LogicalToDeviceUnits(82);
        layout.RowStyles[2].Height = LogicalToDeviceUnits(status.Text.Length == 0 ? 16 : 36);
        header.Padding = new Padding(inset, LogicalToDeviceUnits(11), inset, LogicalToDeviceUnits(10));
        header.ColumnStyles[0].Width = LogicalToDeviceUnits(57);
        header.ColumnStyles[2].Width = pro ? LogicalToDeviceUnits(40) : 0;
        viewport.Margin = status.Margin = new Padding(inset, 0, inset, 0);
        Place(LogicalWidth, LogicalHeight);
    }

    public void SetStatus(string text)
    {
        status.Text = text;
        status.AccessibleName = text;
        layout.RowStyles[2].Height = LogicalToDeviceUnits(text.Length == 0 ? 16 : 36);
    }

    public void Add(Control control, int height)
    {
        control.Dock = DockStyle.Fill;
        control.Margin = new Padding(0, 0, 0, LogicalToDeviceUnits(7));
        Body.RowStyles.Add(new RowStyle(SizeType.Absolute, LogicalToDeviceUnits(height + 7)));
        Body.Controls.Add(control, 0, Body.RowCount++);
        // Rows added after the form is displayed still start with logical child dimensions.
        if (Body.IsHandleCreated && DeviceDpi != 96)
        {
            float scale = DeviceDpi / 96f;
            control.Margin = new Padding(0, 0, 0, 7);
            control.Scale(new SizeF(scale, scale));
            control.Margin = new Padding(0, 0, 0, LogicalToDeviceUnits(7));
        }
    }

    public void ClearBody()
    {
        while (Body.Controls.Count > 0)
            Body.Controls[0].Dispose();
        Body.RowStyles.Clear();
        Body.RowCount = 0;
    }

    public static Label Label(string text, bool muted = false, Font? font = null) => new()
    {
        Text = text, Dock = DockStyle.Fill, Margin = Padding.Empty, TextAlign = ContentAlignment.MiddleLeft,
        ForeColor = muted ? Theme.Muted : Theme.Text, Font = font ?? PopupAppearance.CaptionFont, AutoEllipsis = true,
        UseMnemonic = false
    };

    public static PopupButton Button(string text, Action action, PopupGlyph? glyph = null,
        PopupButtonStyle style = PopupButtonStyle.Standard, bool leftAligned = false)
    {
        var button = new PopupButton(glyph, leftAligned: leftAligned, style: style)
        {
            Text = text, Dock = DockStyle.Fill, UseMnemonic = false
        };
        button.Click += (_, _) => action();
        return button;
    }

    public static PopupCard Card(Control content, Padding? padding = null, bool featured = false)
    {
        var card = new PopupCard(featured ? Theme.Highlight : Theme.Card, featured)
        {
            Padding = padding ?? new Padding(13, 6, 13, 6),
            Dock = DockStyle.Fill
        };
        content.Margin = Padding.Empty;
        content.Dock = DockStyle.Fill;
        if (content is System.Windows.Forms.Label or Panel or PopupToggle)
            content.BackColor = Color.Transparent;
        card.Controls.Add(content);
        return card;
    }

    public static TableLayoutPanel Columns(params Control[] controls)
    {
        var row = new TableLayoutPanel { ColumnCount = controls.Length, RowCount = 1, Dock = DockStyle.Fill, Margin = Padding.Empty };
        row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        for (int index = 0; index < controls.Length; index++)
        {
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / controls.Length));
            controls[index].Dock = DockStyle.Fill;
            controls[index].Margin = new Padding(index == 0 ? 0 : 3, 0, index == controls.Length - 1 ? 0 : 3, 0);
            row.Controls.Add(controls[index], index, 0);
        }
        return row;
    }
}

public sealed class ProBadge : Control
{
    public ProBadge()
    {
        Size = new Size(38, 21);
        AccessibleName = "Pro";
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var theme = PopupAppearance.Current;
        PopupAppearance.Surface(e.Graphics, new RectangleF(.5f, .5f, Width - 1, Height - 1),
            LogicalToDeviceUnits(10), theme.Highlight, theme.Highlight);
        TextRenderer.DrawText(e.Graphics, "PRO", PopupAppearance.KeyFont, ClientRectangle, theme.Accent,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}

public sealed class PopupToggle : CheckBox
{
    public PopupToggle()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.Opaque | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
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

    protected override void OnCheckedChanged(EventArgs e)
    {
        base.OnCheckedChanged(e);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        PopupAppearance.PaintParentSurface(this, e);
        var theme = PopupAppearance.Current;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        int h = LogicalToDeviceUnits(21), w = LogicalToDeviceUnits(36), gap = LogicalToDeviceUnits(3);
        int y = (Height - h) / 2;
        PopupAppearance.Surface(e.Graphics, new RectangleF(1, y, w, h), h / 2,
            Checked && Enabled ? theme.Accent : theme.Track, theme.Border);
        int inner = h - gap * 2;
        using var knob = new SolidBrush(Checked && Enabled ? theme.OnAccent : theme.Muted);
        e.Graphics.FillEllipse(knob, Checked ? w - gap - inner + 1 : gap + 1, y + gap, inner, inner);
        int labelStart = w + LogicalToDeviceUnits(9);
        var textBounds = new Rectangle(labelStart, 0, Math.Max(0, Width - labelStart), Height);
        TextRenderer.DrawText(e.Graphics, Text, Font, textBounds, Enabled ? theme.Text : theme.Muted,
            TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter);
        if (Focused && ShowFocusCues)
            ControlPaint.DrawFocusRectangle(e.Graphics, ClientRectangle, theme.Text, BackColor);
    }
}

public sealed class PopupProgress : Control
{
    private readonly string module;
    private int percent;

    public PopupProgress(string module = "")
    {
        this.module = module;
        AccessibleRole = AccessibleRole.ProgressBar;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
    }

    public void SetPercent(int value)
    {
        percent = Math.Clamp(value, 0, 100);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var theme = PopupAppearance.Current;
        int height = Math.Min(Height, LogicalToDeviceUnits(6));
        int y = (Height - height) / 2;
        var identity = ModuleIdentity.For(module);
        Color fill = SystemInformation.HighContrast ? theme.Accent
            : theme.Background.GetBrightness() < .5f ? identity.Light : identity.Dark;
        PopupAppearance.Surface(e.Graphics, new RectangleF(1, y, Width - 2, height),
            height / 2, theme.Track, theme.Track);
        PopupAppearance.Surface(e.Graphics, new RectangleF(1, y, (Width - 2) * percent / 100f, height),
            height / 2, fill, fill);
    }
}
