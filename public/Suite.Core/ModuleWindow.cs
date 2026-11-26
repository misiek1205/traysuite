using System.Drawing.Drawing2D;

namespace Suite.Core;

public class ModuleWindow : PopupWindow
{
    protected static PopupAppearance Theme => PopupAppearance.Current;
    private readonly Label status;
    public TableLayoutPanel Body { get; } = new()
    {
        ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top
    };

    public ModuleWindow(string title, string subtitle, int height, bool pro = false)
    {
        Text = title;
        LogicalHeight = height;
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, BackColor = Theme.Background, ColumnCount = 1, RowCount = 3
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        Controls.Add(layout);

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Padding = new Padding(16, 6, 16, 6),
            Margin = Padding.Empty
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, pro ? 40 : 0));
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 72));
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 28));
        var name = Label(title, font: PopupAppearance.TitleFont);
        header.Controls.Add(name, 0, 0);
        header.Controls.Add(Label(subtitle, muted: true), 0, 1);
        if (pro)
            header.Controls.Add(new ProBadge { Anchor = AnchorStyles.Right, Margin = Padding.Empty }, 1, 0);
        layout.Controls.Add(header, 0, 0);
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Margin = new Padding(16, 0, 16, 0) };
        Body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        scroll.Controls.Add(Body);
        layout.Controls.Add(scroll, 0, 1);

        status = Label("", muted: true);
        status.Margin = new Padding(16, 0, 16, 0);
        layout.Controls.Add(status, 0, 2);
    }

    protected int LogicalHeight { get; set; }
    protected override void Place() => Place(352, LogicalHeight);

    public void SetStatus(string text)
    {
        status.Text = text;
        status.AccessibleName = text;
    }

    public void Add(Control control, int height)
    {
        control.Dock = DockStyle.Fill;
        control.Margin = new Padding(0, 0, 0, 6);
        Body.RowStyles.Add(new RowStyle(SizeType.Absolute, LogicalToDeviceUnits(height + 6)));
        Body.Controls.Add(control, 0, Body.RowCount++);
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
        ForeColor = muted ? Theme.Muted : Theme.Text, Font = font ?? PopupAppearance.CaptionFont, AutoEllipsis = true
    };

    public static PopupButton Button(string text, Action action)
    {
        var button = new PopupButton { Text = text, ForeColor = Theme.Text, Dock = DockStyle.Fill };
        button.Click += (_, _) => action();
        return button;
    }

    public static TableLayoutPanel Columns(params Control[] controls)
    {
        var row = new TableLayoutPanel { ColumnCount = controls.Length, RowCount = 1, Dock = DockStyle.Fill, Margin = Padding.Empty };
        row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        for (int index = 0; index < controls.Length; index++)
        {
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / controls.Length));
            row.Controls.Add(controls[index], index, 0);
        }
        return row;
    }
}

public sealed class ProBadge : Control
{
    public ProBadge()
    {
        Size = new Size(39, 20);
        AccessibleName = "Pro";
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var theme = PopupAppearance.Current;
        PopupAppearance.Surface(e.Graphics, new RectangleF(.5f, .5f, Width - 1, Height - 1),
            LogicalToDeviceUnits(6), theme.Highlight, theme.Border);
        TextRenderer.DrawText(e.Graphics, "PRO", PopupAppearance.CaptionFont, ClientRectangle, theme.Accent,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}

public sealed class PopupToggle : CheckBox
{
    public PopupToggle()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Cursor = Cursors.Hand;
    }

    protected override void OnCheckedChanged(EventArgs e)
    {
        base.OnCheckedChanged(e);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var theme = PopupAppearance.Current;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        int h = LogicalToDeviceUnits(19), w = LogicalToDeviceUnits(34), gap = LogicalToDeviceUnits(5);
        int y = (Height - h) / 2;
        PopupAppearance.Surface(e.Graphics, new RectangleF(1, y, w, h), h / 2,
            Checked && Enabled ? theme.Accent : theme.Track, theme.Border);
        int inner = h - gap * 2;
        using var knob = new SolidBrush(Checked && Enabled ? theme.Background : theme.Muted);
        e.Graphics.FillEllipse(knob, Checked ? w - gap - inner + 1 : gap + 1, y + gap, inner, inner);
        var textBounds = new Rectangle(w + gap * 2, 0, Math.Max(0, Width - w - gap * 2), Height);
        TextRenderer.DrawText(e.Graphics, Text, Font, textBounds, Enabled ? theme.Text : theme.Muted,
            TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter);
        if (Focused && ShowFocusCues)
            ControlPaint.DrawFocusRectangle(e.Graphics, ClientRectangle, theme.Text, BackColor);
    }
}

public sealed class PopupProgress : Control
{
    private int percent;
    public PopupProgress() => SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

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
        using var track = new SolidBrush(theme.Track);
        using var active = new SolidBrush(theme.Accent);
        e.Graphics.FillRectangle(track, 0, y, Width, height);
        e.Graphics.FillRectangle(active, 0, y, Width * percent / 100, height);
    }
}
