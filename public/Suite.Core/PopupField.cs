namespace Suite.Core;

public sealed class PopupField : Panel
{
    private readonly Control input;

    private PopupField(Control input)
    {
        this.input = input;
        var theme = PopupAppearance.Current;
        TabStop = false;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        input.BackColor = theme.Card;
        input.ForeColor = theme.Text;
        input.Dock = DockStyle.None;
        input.Margin = Padding.Empty;
        if (input is TextBoxBase text)
            text.BorderStyle = BorderStyle.None;
        if (input is UpDownBase numeric)
            numeric.BorderStyle = BorderStyle.None;
        Controls.Add(input);
    }

    public static PopupField Wrap(Control input) => new(input);

    protected override void OnEnter(EventArgs e)
    {
        base.OnEnter(e);
        Invalidate();
    }

    protected override void OnLeave(EventArgs e)
    {
        base.OnLeave(e);
        Invalidate();
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        if (input is null)
            return;
        int inset = LogicalToDeviceUnits(10);
        int height = input.PreferredSize.Height;
        input.SetBounds(inset, Math.Max(0, (Height - height) / 2), Math.Max(0, Width - 2 * inset), height);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        PopupAppearance.PaintParentSurface(this, e);
        var theme = PopupAppearance.Current;
        PopupAppearance.Surface(e.Graphics, new RectangleF(.5f, .5f, Width - 1, Height - 1),
            LogicalToDeviceUnits(10), theme.Card, ContainsFocus ? theme.Accent : theme.Border);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left)
            input.Focus();
    }
}
