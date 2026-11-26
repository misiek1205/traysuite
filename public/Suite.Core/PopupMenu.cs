using System.Drawing.Drawing2D;

namespace Suite.Core;

public sealed class PopupMenu : ContextMenuStrip
{
    public PopupMenu()
    {
        Font = PopupAppearance.CaptionFont;
        Padding = new Padding(5, 6, 5, 6);
        BackColor = PopupAppearance.Current.Card;
        ForeColor = PopupAppearance.Current.Text;
        ShowImageMargin = true;
        Renderer = new MenuRenderer();
        ItemAdded += (_, args) =>
        {
            if (args.Item is ToolStripMenuItem item)
                item.Padding = new Padding(0, LogicalToDeviceUnits(4), 12, LogicalToDeviceUnits(4));
        };
    }

    private sealed class MenuRenderer : ToolStripProfessionalRenderer
    {
        private static PopupAppearance Theme => PopupAppearance.Current;

        public MenuRenderer() => RoundedEdges = false;

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) => e.Graphics.Clear(Theme.Card);

        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using var line = new Pen(Theme.Border);
            e.Graphics.DrawRectangle(line, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (e.Item.Selected && e.Item.Enabled)
                PopupAppearance.Surface(e.Graphics, new RectangleF(1, .5f, e.Item.Width - 3, e.Item.Height - 1),
                    7, Theme.Hover, Theme.Hover);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Theme.Text : Theme.Muted;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e) =>
            PopupIcons.Draw(e.Graphics, PopupGlyph.Check, e.ImageRectangle, Theme.Accent);

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            using var line = new Pen(Theme.Border);
            e.Graphics.DrawLine(line, 8, e.Item.Height / 2, e.Item.Width - 8, e.Item.Height / 2);
        }
    }
}
