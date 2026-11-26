using System.Runtime.InteropServices;

namespace Suite.Core;

internal sealed class PopupViewport : Panel
{
    private readonly Panel scroll = new ThemedScrollPanel { AutoScroll = true };
    private readonly Control content;
    private readonly ScrollRail rail;
    private bool layoutPending;

    public PopupViewport(Control content)
    {
        this.content = content;
        rail = new ScrollRail(this);
        scroll.Controls.Add(content);
        Controls.Add(scroll);
        Controls.Add(rail);
        rail.BringToFront();
        content.SizeChanged += (_, _) => UpdateRail();
        content.LocationChanged += (_, _) => rail.Invalidate();
        scroll.Scroll += (_, _) => rail.Invalidate();
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal int Offset
    {
        get => -scroll.AutoScrollPosition.Y;
        set
        {
            scroll.AutoScrollPosition = new Point(0, Math.Clamp(value, 0, ScrollRange));
            rail.Invalidate();
        }
    }

    private int ScrollRange => Math.Max(0, content.Height - ClientSize.Height);

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        UpdateRail();
    }

    // Keep the WinForms scrolling and focus behavior while clipping its native scrollbar.
    private void UpdateRail()
    {
        if (layoutPending || !IsHandleCreated)
            return;
        layoutPending = true;
        try
        {
            int width = ClientSize.Width;
            int height = ClientSize.Height;
            bool overflow = ScrollRange > 0 && height > 0;
            int railWidth = overflow ? LogicalToDeviceUnits(12) : 0;
            int nativeWidth = overflow ? GetSystemMetricsForDpi(2, (uint)DeviceDpi) : 0;
            scroll.Bounds = new Rectangle(0, 0, width - railWidth + nativeWidth, height);
            rail.Bounds = new Rectangle(width - railWidth, 0, railWidth, height);
            rail.Visible = overflow;
            rail.Invalidate();
        }
        finally { layoutPending = false; }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        UpdateRail();
    }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetricsForDpi(int index, uint dpi);

    private sealed class ScrollRail : Control
    {
        private readonly PopupViewport owner;
        private int grab;

        public ScrollRail(PopupViewport owner)
        {
            this.owner = owner;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            TabStop = false;
            Cursor = Cursors.Hand;
        }

        private int ThumbHeight => Math.Clamp((int)((long)Height * Height / Math.Max(1, owner.content.Height)),
            Math.Min(Height, LogicalToDeviceUnits(30)), Height);
        private int Travel => Height - ThumbHeight;
        private int ThumbTop => (int)((long)owner.Offset * Travel / Math.Max(1, owner.ScrollRange));

        protected override void OnPaint(PaintEventArgs e)
        {
            var theme = PopupAppearance.Current;
            int width = LogicalToDeviceUnits(4);
            var thumb = new RectangleF(Width - width, ThumbTop, width, ThumbHeight);
            Color fill = PopupAppearance.Tint(theme.Track, theme.Muted, Capture ? .8 : .46);
            PopupAppearance.Surface(e.Graphics, thumb, width / 2, fill, fill);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left)
                return;
            int top = ThumbTop;
            grab = e.Y >= top && e.Y < top + ThumbHeight ? e.Y - top : ThumbHeight / 2;
            Capture = true;
            MoveThumb(e.Y);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (Capture)
                MoveThumb(e.Y);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left)
            {
                Capture = false;
                Invalidate();
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            owner.Offset -= e.Delta * LogicalToDeviceUnits(40) / SystemInformation.MouseWheelScrollDelta;
        }

        private void MoveThumb(int y) => owner.Offset = (int)((long)(y - grab) * owner.ScrollRange / Math.Max(1, Travel));
    }
}
