namespace TrayLuminance;

internal sealed class BrightnessPanel : Form
{
    private readonly TableLayoutPanel layout = new();
    private readonly Panel scroller = new();
    private readonly TableLayoutPanel rows = new();
    private readonly TableLayoutPanel shortcuts = new();
    private readonly TrackBar master = new();
    private readonly Label offsetLabel = new();
    private readonly Button applyDay;
    private readonly Button applyEvening;
    private readonly Button saveDay;
    private readonly Button saveEvening;
    private readonly Label status = new();
    private Dictionary<BrightnessRow, int>? groupStart;
    private bool masterKeyHeld;
    private bool shortcutsRequested;
    private Rectangle workingArea;
    private Point anchor;
    private const int RowHeight = 76;

    public BrightnessPanel(Action refresh, Action<Profile> apply, Action<Profile> save)
    {
        Text = "TrayLuminance";
        ShowInTaskbar = false;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96, 96);
        TopMost = true;
        KeyPreview = true;

        BackColor = SystemColors.ActiveBorder;
        Padding = new Padding(1);
        layout.Dock = DockStyle.Fill;
        layout.BackColor = SystemColors.Window;
        layout.ColumnCount = 1;
        layout.RowCount = 4;
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        Controls.Add(layout);

        var header = new TableLayoutPanel
        {
            BackColor = SystemColors.Control,
            ColumnCount = 2,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 106));
        header.Controls.Add(new Label
        {
            Text = Texts.Get("Monitor brightness", "Jasność monitorów"),
            Font = new Font(SystemFonts.MessageBoxFont!.FontFamily, 14, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
            Dock = DockStyle.Fill,
            Margin = new Padding(16, 0, 0, 0),
            AutoEllipsis = true
        }, 0, 0);
        var refreshButton = new Button
        {
            Text = Texts.Get("Refresh", "Odśwież"),
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            AutoSize = true,
            Margin = new Padding(0, 0, 12, 0)
        };
        refreshButton.Click += (_, _) => refresh();
        header.Controls.Add(refreshButton, 1, 0);
        layout.Controls.Add(header, 0, 0);

        shortcuts.Dock = DockStyle.Fill;
        shortcuts.Margin = new Padding(12, 0, 12, 0);
        shortcuts.ColumnCount = 1;
        shortcuts.RowCount = 2;
        shortcuts.RowStyles.Add(new RowStyle(SizeType.Percent, 54));
        shortcuts.RowStyles.Add(new RowStyle(SizeType.Percent, 46));
        layout.Controls.Add(shortcuts, 0, 1);

        var allDisplays = new GroupBox
        {
            Text = Texts.Get("All monitors", "Wszystkie monitory"),
            Dock = DockStyle.Fill
        };
        var masterLayout = new TableLayoutPanel { ColumnCount = 2, RowCount = 1, Dock = DockStyle.Fill };
        masterLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        masterLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 58));
        offsetLabel.Dock = DockStyle.Fill;
        offsetLabel.TextAlign = ContentAlignment.MiddleRight;
        master.Dock = DockStyle.Fill;
        master.AutoSize = false;
        master.Minimum = -100;
        master.Maximum = 100;
        master.SmallChange = 1;
        master.LargeChange = 10;
        master.TickStyle = TickStyle.None;
        master.AccessibleName = allDisplays.Text;
        master.AccessibleDescription = Texts.Get("Change each monitor by the same amount.",
            "Zmień jasność każdego monitora o tę samą wartość.");
        master.Scroll += (_, _) => ShiftDisplays();
        master.MouseUp += (_, _) => ResetMaster();
        master.MouseCaptureChanged += (_, _) =>
        {
            if ((Control.MouseButtons & MouseButtons.Left) == 0)
                ResetMaster();
        };
        master.KeyDown += (_, args) => masterKeyHeld = args.KeyCode is Keys.Left or Keys.Right
            or Keys.Up or Keys.Down or Keys.PageUp or Keys.PageDown or Keys.Home or Keys.End;
        master.KeyUp += (_, _) => ResetMaster();
        master.LostFocus += (_, _) => ResetMaster();
        ResetMaster();
        masterLayout.Controls.Add(master, 0, 0);
        masterLayout.Controls.Add(offsetLabel, 1, 0);
        allDisplays.Controls.Add(masterLayout);
        shortcuts.Controls.Add(allDisplays, 0, 0);

        var profiles = new GroupBox { Text = Texts.Get("Brightness profiles", "Profile jasności"), Dock = DockStyle.Fill };
        var buttons = new TableLayoutPanel { ColumnCount = 4, RowCount = 1, Dock = DockStyle.Fill };
        for (int column = 0; column < buttons.ColumnCount; column++)
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        applyDay = AddProfileButton(Texts.Get("Day", "Dzień"), 0, () => apply(Profile.Day));
        saveDay = AddProfileButton(Texts.Get("Save day", "Zapisz dzień"), 1, () => save(Profile.Day));
        applyEvening = AddProfileButton(Texts.Get("Evening", "Wieczór"), 2, () => apply(Profile.Evening));
        saveEvening = AddProfileButton(Texts.Get("Save evening", "Zapisz wieczór"), 3, () => save(Profile.Evening));
        profiles.Controls.Add(buttons);
        shortcuts.Controls.Add(profiles, 0, 1);

        Button AddProfileButton(string text, int column, Action action)
        {
            var button = new Button { Text = text, Dock = DockStyle.Fill, Margin = new Padding(2, 0, 2, 2) };
            button.Click += (_, _) => action();
            buttons.Controls.Add(button, column, 0);
            return button;
        }

        scroller.Margin = Padding.Empty;
        scroller.Dock = DockStyle.Fill;
        scroller.AutoScroll = true;
        rows.ColumnCount = 1;
        rows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        rows.AutoSize = true;
        rows.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        rows.Dock = DockStyle.Top;
        scroller.Controls.Add(rows);
        layout.Controls.Add(scroller, 0, 2);

        status.Dock = DockStyle.Fill;
        status.BackColor = SystemColors.Control;
        status.TextAlign = ContentAlignment.MiddleLeft;
        status.Padding = new Padding(16, 0, 6, 0);
        status.Margin = Padding.Empty;
        status.AutoEllipsis = true;
        layout.Controls.Add(status, 0, 3);
        shortcuts.Visible = false;

        DpiChanged += (_, _) => BeginInvoke(ResizeAndPlace);
    }

    public void Open(Point position)
    {
        anchor = position;
        workingArea = Screen.FromPoint(anchor).WorkingArea;
        // Pick the correct monitor before Windows assigns the per-monitor DPI.
        if (!Visible)
            Location = anchor;
        ResizeAndPlace();
        Show();
        ResizeAndPlace();
        Activate();
    }

    private void ResizeAndPlace()
    {
        if (workingArea.IsEmpty)
            return;

        int S(int units) => LogicalToDeviceUnits(units);
        int gap = S(8);
        int width = Math.Min(S(428), Math.Max(1, workingArea.Width - 2 * gap));
        int shortcutsHeight = shortcutsRequested ? 116 : 0;
        int height = Math.Min(S(92 + shortcutsHeight + Math.Max(1, rows.RowCount) * RowHeight),
            Math.Max(1, workingArea.Height - 2 * gap));
        layout.RowStyles[0].Height = S(56);
        layout.RowStyles[1].Height = S(shortcutsHeight);
        layout.RowStyles[3].Height = S(34);
        ((TableLayoutPanel)layout.GetControlFromPosition(0, 0)!).ColumnStyles[1].Width = S(106);
        foreach (Control row in rows.Controls)
            row.Height = S(RowHeight);

        int x = Math.Clamp(anchor.X - width + S(24), workingArea.Left + gap,
            Math.Max(workingArea.Left + gap, workingArea.Right - width - gap));
        int preferredY = anchor.Y >= workingArea.Top + workingArea.Height / 2
            ? anchor.Y - height - gap : anchor.Y + gap;
        int y = Math.Clamp(preferredY, workingArea.Top + gap,
            Math.Max(workingArea.Top + gap, workingArea.Bottom - height - gap));
        Bounds = new Rectangle(x, y, width, height);
    }

    public void SetStatus(string message)
    {
        status.Text = message;
        status.AccessibleName = message;
    }

    public void SetDisplays(IReadOnlyList<Display> displays, Action<BrightnessRow, int> changed)
    {
        ResetMaster();
        rows.SuspendLayout();
        while (rows.Controls.Count > 0)
            rows.Controls[0].Dispose();
        rows.RowStyles.Clear();
        rows.RowCount = 0;

        foreach (Display display in displays)
        {
            var row = new BrightnessRow(display);
            row.BrightnessChanged += changed;
            rows.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            rows.Controls.Add(row, 0, rows.RowCount++);
        }

        if (displays.Count == 0)
        {
            rows.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            rows.Controls.Add(new Label
            {
                Text = Texts.Get("No displays found. Connect a display and refresh.",
                    "Nie wykryto monitorów. Podłącz monitor i odśwież listę."),
                Height = RowHeight,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(16, 0, 16, 0),
                Margin = Padding.Empty
            }, 0, rows.RowCount++);
        }

        rows.ResumeLayout(true);
        int controllable = ControllableRows.Count();
        shortcutsRequested = controllable > 0;
        shortcuts.Visible = shortcutsRequested;
        master.Enabled = controllable > 1;
        ResizeAndPlace();
    }

    private IEnumerable<BrightnessRow> ControllableRows =>
        rows.Controls.OfType<BrightnessRow>().Where(row => row.Display.SetBrightness is not null);

    public Dictionary<string, BrightnessRow> ProfileRows() =>
        ControllableRows.Where(row => row.Display.Id is not null)
            .GroupBy(row => row.Display.Id!, StringComparer.Ordinal)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single(), StringComparer.Ordinal);

    public void SetProfileAvailability(bool canSave, bool canApplyDay, bool canApplyEvening)
    {
        saveDay.Enabled = saveEvening.Enabled = canSave;
        applyDay.Enabled = canApplyDay;
        applyEvening.Enabled = canApplyEvening;
    }

    public void SetControlsEnabled(bool enabled)
    {
        rows.Enabled = shortcuts.Enabled = enabled;
        if (!enabled)
            ResetMaster();
    }

    private void ShiftDisplays()
    {
        groupStart ??= ControllableRows.ToDictionary(row => row, row => row.Target);
        int shift = master.Value;
        offsetLabel.Text = shift.ToString("+0;-0;0") + " " + Texts.Get("pp", "p.p.");
        foreach (var (row, start) in groupStart)
            row.SetTarget(Math.Clamp(start + shift, 0, 100));
        if (!masterKeyHeld && (Control.MouseButtons & MouseButtons.Left) == 0)
            ResetMaster();
    }

    private void ResetMaster()
    {
        groupStart = null;
        masterKeyHeld = false;
        master.Value = 0;
        offsetLabel.Text = "0 " + Texts.Get("pp", "p.p.");
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData != Keys.Escape)
            return base.ProcessCmdKey(ref msg, keyData);

        Hide();
        return true;
    }
}

internal sealed class BrightnessRow : Panel
{
    private readonly TrackBar slider;
    private readonly Label valueLabel;
    public Display Display { get; }
    public int Confirmed { get; private set; }
    public int Target => slider.Value;
    public event Action<BrightnessRow, int>? BrightnessChanged;

    public BrightnessRow(Display display)
    {
        Display = display;
        Confirmed = Math.Clamp(display.Brightness, 0, 100);
        Height = 76;
        Dock = DockStyle.Top;
        Margin = Padding.Empty;

        var card = new TableLayoutPanel
        {
            ColumnCount = 2,
            RowCount = 2,
            Dock = DockStyle.Fill,
            Padding = new Padding(16, 6, 16, 0)
        };
        card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        card.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 60));
        card.RowStyles.Add(new RowStyle(SizeType.Percent, 44));
        card.RowStyles.Add(new RowStyle(SizeType.Percent, 56));
        Controls.Add(card);
        var name = new Label
        {
            Text = display.Name,
            AutoEllipsis = true,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = Padding.Empty
        };
        card.Controls.Add(name, 0, 0);

        valueLabel = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleRight,
            Margin = Padding.Empty,
            Text = $"{Confirmed}%"
        };
        slider = new TrackBar
        {
            Minimum = 0,
            Maximum = 100,
            Value = Confirmed,
            TickStyle = TickStyle.None,
            SmallChange = 1,
            LargeChange = 10,
            AutoSize = false,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            AccessibleName = Texts.Get("Brightness", "Jasność") + " — " + display.Name
        };
        if (display.SetBrightness is null)
        {
            card.SetColumnSpan(name, 2);
            var unavailable = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = SystemColors.GrayText,
                Margin = Padding.Empty,
                Text = Texts.Get("Brightness control unavailable", "Regulacja jasności niedostępna")
            };
            card.Controls.Add(unavailable, 0, 1);
            card.SetColumnSpan(unavailable, 2);
            slider.Dispose();
            valueLabel.Dispose();
            return;
        }

        card.Controls.Add(valueLabel, 1, 0);
        card.Controls.Add(slider, 0, 1);
        card.SetColumnSpan(slider, 2);
        slider.Scroll += (_, _) =>
        {
            valueLabel.Text = $"{slider.Value}%";
            BrightnessChanged?.Invoke(this, slider.Value);
        };
    }

    public void SetTarget(int value)
    {
        if (slider.Value == value)
            return;

        slider.Value = value;
        valueLabel.Text = $"{value}%";
        BrightnessChanged?.Invoke(this, value);
    }

    public void ShowResult(int? brightness, bool pending)
    {
        if (brightness is int actual)
            Confirmed = actual;
        if (pending)
            return;

        slider.Value = Confirmed;
        valueLabel.Text = $"{Confirmed}%";
    }
}
