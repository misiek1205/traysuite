namespace Dimmli;

internal sealed class ShortcutGuide : TableLayoutPanel
{
    private readonly Button toggle;
    private readonly Dictionary<ShortcutAction, Label> labels = [];
    private readonly TableLayoutPanel title;
    private readonly List<TableLayoutPanel> lines = [];
    private const int TitleHeight = 46;
    private const int RowHeight = 49;

    public ShortcutGuide(Action toggleShortcuts)
    {
        var theme = PopupAppearance.Current;
        ColumnCount = 1;
        Dock = DockStyle.Top;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Margin = Padding.Empty;
        title = new TableLayoutPanel
        {
            ColumnCount = 2, RowCount = 1, Dock = DockStyle.Fill, Margin = Padding.Empty
        };
        title.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        title.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 83));
        title.Controls.Add(LabelFor(Texts.Get("Keyboard shortcuts", "Skróty klawiaturowe"),
            PopupAppearance.ValueFont, theme.Text), 0, 0);
        toggle = new PopupButton
        {
            Dock = DockStyle.Fill, Margin = new Padding(3, 3, 0, 8), ForeColor = theme.Accent
        };
        toggle.Click += (_, _) => toggleShortcuts();
        title.Controls.Add(toggle, 1, 0);
        AddRow(title, TitleHeight);

        foreach (var binding in GlobalShortcuts.Bindings)
        {
            var card = new PopupCard(theme.Card)
            {
                BackColor = theme.Background, Padding = new Padding(11, 5, 10, 5),
                Margin = new Padding(0, 0, 0, 7), Dock = DockStyle.Fill
            };
            var line = new TableLayoutPanel
            {
                ColumnCount = 2, RowCount = 1, Dock = DockStyle.Fill, BackColor = theme.Card,
                Margin = Padding.Empty
            };
            line.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            line.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 128));
            line.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var label = LabelFor(binding.Label, PopupAppearance.CaptionFont, theme.Text);
            labels.Add(binding.Action, label);
            line.Controls.Add(label, 0, 0);
            var keys = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false,
                Margin = Padding.Empty, Padding = new Padding(0, 3, 0, 0)
            };
            foreach (string name in new[] { binding.KeyLabel, "Alt", "Ctrl" })
            {
                var keycap = new Label
                {
                    Text = name, AutoSize = true, Padding = new Padding(5, 3, 5, 3), Font = PopupAppearance.KeyFont,
                    Margin = new Padding(3, 0, 0, 0), BackColor = theme.Highlight, ForeColor = theme.Accent
                };
                keys.Controls.Add(keycap);
            }
            label.AccessibleDescription = $"Ctrl + Alt + {binding.KeyLabel}";
            line.Controls.Add(keys, 1, 0);
            lines.Add(line);
            card.Controls.Add(line);
            AddRow(card, RowHeight);
        }
    }

    public int LogicalHeight => TitleHeight + lines.Count * RowHeight + 4;

    public void FitToDpi()
    {
        SuspendLayout();
        title.ColumnStyles[1].Width = LogicalToDeviceUnits(96);
        RowStyles[0].Height = LogicalToDeviceUnits(TitleHeight);
        for (int index = 0; index < lines.Count; index++)
        {
            lines[index].ColumnStyles[1].Width = LogicalToDeviceUnits(128);
            RowStyles[index + 1].Height = LogicalToDeviceUnits(RowHeight);
        }
        ResumeLayout();
    }

    private void AddRow(Control control, int height)
    {
        RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        Controls.Add(control, 0, RowCount++);
    }

    private static Label LabelFor(string text, Font font, Color color) => new()
    {
        Text = text, Font = font, ForeColor = color, TextAlign = ContentAlignment.MiddleLeft,
        Dock = DockStyle.Fill, Margin = Padding.Empty, AutoEllipsis = true
    };

    public void UpdateBindings(GlobalShortcuts shortcuts)
    {
        toggle.Text = shortcuts.Enabled ? Texts.Get("Turn off", "Wyłącz") : Texts.Get("Turn on", "Włącz");
        foreach (var binding in GlobalShortcuts.Bindings)
        {
            var label = labels[binding.Action];
            bool active = shortcuts.IsRegistered(binding.Action);
            string detail = !shortcuts.Enabled ? Texts.Get(" • Off", " • Wyłączony")
                : !active ? Texts.Get(" • Unavailable", " • Niedostępny") : "";
            label.Text = binding.Label + detail;
            label.ForeColor = active ? PopupAppearance.Current.Text : PopupAppearance.Current.Muted;
        }
    }
}
