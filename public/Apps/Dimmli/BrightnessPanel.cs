namespace Dimmli;

internal sealed class BrightnessPanel : PopupWindow
{
    private static readonly PopupAppearance Theme = PopupAppearance.Current;
    private readonly TableLayoutPanel layout = new();
    private readonly Panel scroller = new ThemedScrollPanel();
    private readonly TableLayoutPanel body = new();
    private readonly TableLayoutPanel rows = new();
    private readonly TableLayoutPanel overview = new();
    private readonly TableLayoutPanel header = new();
    private readonly TableLayoutPanel footer = new();
    private readonly TableLayoutPanel masterLayout = new();
    private readonly ShortcutGuide guide;
    private readonly PopupSlider master = new(transparent: true);
    private readonly BrightnessMeter meter = new();
    private readonly Label offsetLabel = new();
    private readonly Button applyDay;
    private readonly Button applyEvening;
    private readonly Button saveDay;
    private readonly Button saveEvening;
    private readonly PopupButton guideButton;
    private readonly Label status = new();
    private Dictionary<BrightnessRow, int>? groupStart;
    private double? calibrationBase;
    private double? gestureCalibrationBase;
    private bool groupUpdating;
    private bool masterKeyHeld;
    private bool overviewVisible;
    private bool guideVisible;
    private const int HeaderHeight = 82;
    private const int OverviewHeight = 180;
    private const int RowHeight = 82;
    private const int RowGap = 8;
    private const int FooterHeight = 53;

    public BrightnessPanel(Action refresh, Action<Profile> apply, Action<Profile> save, Action? toggleShortcuts = null)
    {
        Text = "Dimmli";
        layout.Dock = DockStyle.Fill;
        layout.BackColor = Theme.Background;
        layout.ColumnCount = 1;
        layout.RowCount = 3;
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, HeaderHeight));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, FooterHeight));
        Controls.Add(layout);

        header.ColumnCount = 3;
        header.RowCount = 2;
        header.Dock = DockStyle.Fill;
        header.Margin = Padding.Empty;
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 57));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 43));
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 64));
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 36));
        var tips = new ToolTip();
        Disposed += (_, _) => tips.Dispose();
        var mark = new ModuleMark("Dimmli")
        {
            Dock = DockStyle.Fill, Margin = new Padding(0, 0, 13, 0), TabStop = false
        };
        header.Controls.Add(mark, 0, 0);
        header.SetRowSpan(mark, 2);
        header.Controls.Add(CreateLabel("Dimmli", Theme.Text, PopupAppearance.TitleFont, ContentAlignment.BottomLeft), 1, 0);
        header.Controls.Add(CreateLabel(Texts.Get("Monitor brightness", "Jasność monitorów"),
            Theme.Muted, PopupAppearance.CaptionFont, ContentAlignment.TopLeft), 1, 1);

        var refreshButton = new PopupButton(PopupGlyph.Refresh, iconOnly: true)
        {
            Text = Texts.Get("Refresh displays", "Odśwież monitory"), Height = 40,
            Anchor = AnchorStyles.Left | AnchorStyles.Right, Margin = Padding.Empty
        };
        refreshButton.Click += (_, _) => refresh();
        tips.SetToolTip(refreshButton, refreshButton.Text);
        header.Controls.Add(refreshButton, 2, 0);
        header.SetRowSpan(refreshButton, 2);
        layout.Controls.Add(header, 0, 0);

        scroller.Margin = new Padding(19, 0, 19, 0);
        scroller.Dock = DockStyle.Fill;
        scroller.AutoScroll = true;
        layout.Controls.Add(scroller, 0, 1);

        body.ColumnCount = 1;
        body.RowCount = 2;
        body.AutoSize = true;
        body.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        body.Dock = DockStyle.Top;
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
        body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        scroller.Controls.Add(body);

        overview.Dock = DockStyle.Fill;
        overview.Margin = Padding.Empty;
        overview.ColumnCount = 1;
        overview.RowCount = 2;
        overview.RowStyles.Add(new RowStyle(SizeType.Percent, 62));
        overview.RowStyles.Add(new RowStyle(SizeType.Percent, 38));
        body.Controls.Add(overview, 0, 0);

        string allDisplaysLabel = Texts.Get("All monitors", "Wszystkie monitory");
        var featured = new PopupCard(Theme.Highlight, featured: true)
        {
            BackColor = Theme.Background, Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 10), Padding = new Padding(16, 12, 11, 8)
        };
        masterLayout.ColumnCount = 2;
        masterLayout.RowCount = 3;
        masterLayout.Dock = DockStyle.Fill;
        masterLayout.BackColor = Color.Transparent;
        masterLayout.Margin = Padding.Empty;
        masterLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        masterLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 102));
        masterLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 36));
        masterLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 26));
        masterLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 38));
        masterLayout.Controls.Add(CreateLabel(allDisplaysLabel, Theme.Text, PopupAppearance.ValueFont), 0, 0);

        offsetLabel.Dock = DockStyle.Fill;
        offsetLabel.ForeColor = Theme.Muted;
        offsetLabel.Font = PopupAppearance.CaptionFont;
        offsetLabel.Margin = Padding.Empty;
        offsetLabel.TextAlign = ContentAlignment.MiddleLeft;
        offsetLabel.AutoEllipsis = true;
        masterLayout.Controls.Add(offsetLabel, 0, 1);

        master.Dock = DockStyle.Fill;
        master.Margin = Padding.Empty;
        master.AutoSize = false;
        master.Minimum = -100;
        master.Maximum = 100;
        master.SmallChange = 5;
        master.LargeChange = 10;
        master.TickStyle = TickStyle.None;
        master.AccessibleName = allDisplaysLabel;
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
        masterLayout.Controls.Add(master, 0, 2);

        meter.TabStop = false;
        meter.Dock = DockStyle.Fill;
        meter.Margin = Padding.Empty;
        masterLayout.Controls.Add(meter, 1, 0);
        masterLayout.SetRowSpan(meter, 3);
        featured.Controls.Add(masterLayout);
        overview.Controls.Add(featured, 0, 0);

        var profiles = new TableLayoutPanel
        {
            ColumnCount = 2, RowCount = 2, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 9)
        };
        profiles.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        profiles.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        profiles.RowStyles.Add(new RowStyle(SizeType.Percent, 32));
        profiles.RowStyles.Add(new RowStyle(SizeType.Percent, 68));
        var profilesTitle = CreateLabel(Texts.Get("Profiles", "Profile"), Theme.Muted, PopupAppearance.CaptionFont);
        profiles.Controls.Add(profilesTitle, 0, 0);
        profiles.SetColumnSpan(profilesTitle, 2);
        (applyDay, saveDay) = MakeProfile(Profile.Day, 0, PopupGlyph.Sun, Theme.Day,
            Texts.Get("Day", "Dzień"), Texts.Get("Save day", "Zapisz dzień"));
        (applyEvening, saveEvening) = MakeProfile(Profile.Evening, 1, PopupGlyph.Moon, Theme.Evening,
            Texts.Get("Evening", "Wieczór"), Texts.Get("Save evening", "Zapisz wieczór"));
        overview.Controls.Add(profiles, 0, 1);

        (Button activate, Button store) MakeProfile(Profile profile, int column, PopupGlyph glyph,
            Color color, string label, string saveLabel)
        {
            Color surface = SystemInformation.HighContrast ? Theme.Card : PopupAppearance.Tint(Theme.Card, color, .07);
            var tile = new PopupCard(surface)
            {
                Dock = DockStyle.Fill, BackColor = Theme.Background, Padding = new Padding(10, 7, 10, 7),
                Margin = column == 0 ? new Padding(0, 0, 4, 0) : new Padding(4, 0, 0, 0)
            };
            var pair = new TableLayoutPanel
            {
                ColumnCount = 3, RowCount = 1, Dock = DockStyle.Fill,
                Margin = Padding.Empty, BackColor = surface
            };
            pair.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            pair.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 1));
            pair.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 38));
            pair.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var activate = new PopupButton(glyph, leftAligned: true, inset: true)
            {
                Text = label, ForeColor = color, Font = PopupAppearance.LabelFont,
                Dock = DockStyle.Fill, Margin = new Padding(0, 0, 3, 0), BackColor = surface
            };
            var store = new PopupButton(PopupGlyph.Save, iconOnly: true, inset: true)
            {
                Text = saveLabel, ForeColor = Theme.Muted, Dock = DockStyle.Fill,
                Margin = new Padding(3, 0, 0, 0), BackColor = surface
            };
            tips.SetToolTip(store, saveLabel);
            activate.Click += (_, _) => apply(profile);
            store.Click += (_, _) => save(profile);
            pair.Controls.Add(activate, 0, 0);
            pair.Controls.Add(new Panel { Dock = DockStyle.Fill, BackColor = Theme.Border, Margin = new Padding(0, 5, 0, 5) }, 1, 0);
            pair.Controls.Add(store, 2, 0);
            tile.Controls.Add(pair);
            profiles.Controls.Add(tile, column, 1);
            return (activate, store);
        }

        rows.ColumnCount = 1;
        rows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        rows.AutoSize = true;
        rows.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        rows.Dock = DockStyle.Fill;
        rows.Margin = Padding.Empty;
        body.Controls.Add(rows, 0, 1);

        guide = new ShortcutGuide(() => toggleShortcuts?.Invoke()) { Visible = false };
        scroller.Controls.Add(guide);

        footer.ColumnCount = 2;
        footer.Dock = DockStyle.Fill;
        footer.Margin = Padding.Empty;
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 106));
        status.Dock = DockStyle.Fill;
        status.ForeColor = Theme.Muted;
        status.TextAlign = ContentAlignment.MiddleLeft;
        status.Margin = new Padding(0, 0, 8, 0);
        status.AutoEllipsis = true;
        footer.Controls.Add(status, 0, 0);
        guideButton = new PopupButton(PopupGlyph.Keyboard)
        {
            Text = Texts.Get("Keys", "Skróty"),
            Dock = DockStyle.Fill,
            Margin = Padding.Empty
        };
        guideButton.Click += (_, _) => ShowShortcutGuide(!guideVisible);
        footer.Controls.Add(guideButton, 1, 0);
        layout.Controls.Add(footer, 0, 2);
        overview.Visible = false;

    }

    private static Label CreateLabel(string text, Color color, Font font,
        ContentAlignment align = ContentAlignment.MiddleLeft) => new()
    {
        Text = text, ForeColor = color, Font = font, TextAlign = align, Dock = DockStyle.Fill,
        Margin = Padding.Empty, AutoEllipsis = true
    };

    protected override void Place()
    {
        int overviewHeight = overviewVisible ? OverviewHeight : 0;
        int bodyHeight = guideVisible ? guide.LogicalHeight : overviewHeight + Math.Max(1, rows.RowCount) * (RowHeight + RowGap);
        int inset = LogicalToDeviceUnits(19);
        header.Padding = new Padding(inset, LogicalToDeviceUnits(10), inset, LogicalToDeviceUnits(11));
        header.ColumnStyles[0].Width = LogicalToDeviceUnits(57);
        header.ColumnStyles[2].Width = LogicalToDeviceUnits(43);
        footer.Padding = new Padding(inset, LogicalToDeviceUnits(4), inset, LogicalToDeviceUnits(10));
        footer.ColumnStyles[1].Width = LogicalToDeviceUnits(112);
        masterLayout.ColumnStyles[1].Width = LogicalToDeviceUnits(98);
        scroller.Margin = new Padding(inset, 0, inset, 0);
        layout.RowStyles[0].Height = LogicalToDeviceUnits(HeaderHeight);
        body.RowStyles[0].Height = LogicalToDeviceUnits(overviewHeight);
        layout.RowStyles[2].Height = LogicalToDeviceUnits(FooterHeight);
        guide.FitToDpi();
        foreach (Control row in rows.Controls)
        {
            row.Height = LogicalToDeviceUnits(RowHeight);
            row.Margin = new Padding(0, 0, 0, LogicalToDeviceUnits(RowGap));
            if (row is BrightnessRow display)
                display.FitToDpi();
        }
        base.Place(390, 2 + HeaderHeight + FooterHeight + bodyHeight);
    }

    public void SetStatus(string message)
    {
        status.Text = message;
        status.AccessibleName = message;
    }

    public void SetDisplays(IReadOnlyList<Display> displays, Action<BrightnessRow, int> changed,
        IReadOnlyDictionary<string, string>? aliases = null, Action<Display>? rename = null,
        IReadOnlyDictionary<string, int>? offsets = null, Action<Display>? calibrate = null)
    {
        ResetMaster();
        calibrationBase = null;
        rows.SuspendLayout();
        while (rows.Controls.Count > 0)
            rows.Controls[0].Dispose();
        rows.RowStyles.Clear();
        rows.RowCount = 0;

        var uniqueIds = displays.Where(display => display.Id is not null)
            .GroupBy(display => display.Id!, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() == 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (Display display in displays)
        {
            var row = new BrightnessRow(display);
            if (display.Id is { } id && uniqueIds.Contains(id))
            {
                if (aliases?.TryGetValue(id, out var alias) == true)
                    row.SetName(alias);
                if (offsets?.TryGetValue(id, out int offset) == true)
                    row.Offset = offset;
                if (rename is not null || calibrate is not null)
                    row.EnableOptions(rename is null ? null : () => rename(display),
                        calibrate is null || display.SetBrightness is null ? null : () => calibrate(display));
            }
            row.BrightnessChanged += (sender, level) =>
            {
                if (!groupUpdating)
                    calibrationBase = null;
                changed(sender, level);
            };
            row.TargetChanged += UpdateMeter;
            rows.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            rows.Controls.Add(row, 0, rows.RowCount++);
        }

        if (displays.Count == 0)
        {
            rows.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var empty = ModuleWindow.Card(ModuleWindow.Label(Texts.Get("No displays found. Connect a display and refresh.",
                "Nie wykryto monitorów. Podłącz monitor i odśwież listę."), muted: true));
            rows.Controls.Add(empty, 0, rows.RowCount++);
        }

        rows.ResumeLayout(true);
        UpdateMeter();
        int controllable = ControllableRows.Count();
        overviewVisible = controllable > 0;
        overview.Visible = overviewVisible;
        master.Enabled = controllable > 1;
        Place();
    }

    public void UpdateMonitorName(string id, string alias)
    {
        var matching = rows.Controls.OfType<BrightnessRow>()
            .Where(row => string.Equals(row.Display.Id, id, StringComparison.OrdinalIgnoreCase))
            .Take(2).ToArray();
        if (matching.Length == 1)
            matching[0].SetName(alias);
    }

    private IEnumerable<BrightnessRow> ControllableRows =>
        DisplayRows.Where(row => row.Display.SetBrightness is not null);

    public IEnumerable<BrightnessRow> DisplayRows => rows.Controls.OfType<BrightnessRow>();

    public void UpdateMonitorOffset(string id, int offset)
    {
        var matching = DisplayRows.Where(row => string.Equals(row.Display.Id, id, StringComparison.OrdinalIgnoreCase))
            .Take(2).ToArray();
        if (matching.Length == 1)
        {
            matching[0].Offset = offset;
            calibrationBase = null;
            ResetMaster();
        }
    }

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
        rows.Enabled = overview.Enabled = enabled;
        if (!enabled)
            ResetMaster();
    }

    public void UpdateShortcuts(GlobalShortcuts shortcuts) => guide.UpdateBindings(shortcuts);

    public void ShowShortcutGuide(bool visible)
    {
        guideVisible = visible;
        body.Visible = !visible;
        guide.Visible = visible;
        scroller.AutoScrollPosition = Point.Empty;
        guideButton.Text = visible ? Texts.Get("Back", "Wróć") : Texts.Get("Keys", "Skróty");
        Place();
    }

    public void ShiftBy(int delta)
    {
        ResetMaster();
        ApplyShift(ControllableRows.ToDictionary(row => row, row => row.Target), delta, calibrationBase);
    }

    private void UpdateMeter()
    {
        meter.UpdateLevel((int)Math.Round(ControllableRows.Select(row => row.Target).DefaultIfEmpty().Average()));
    }

    private void ShiftDisplays()
    {
        if (groupStart is null)
        {
            groupStart = ControllableRows.ToDictionary(row => row, row => row.Target);
            gestureCalibrationBase = calibrationBase;
        }
        int shift = master.Value;
        offsetLabel.ForeColor = Theme.Accent;
        offsetLabel.Text = shift.ToString("+0;-0;0") + " " + Texts.Get("pp", "p.p.");
        ApplyShift(groupStart, shift, gestureCalibrationBase);
        if (!masterKeyHeld && !master.Dragging && (Control.MouseButtons & MouseButtons.Left) == 0)
            ResetMaster();
    }

    private void ApplyShift(Dictionary<BrightnessRow, int> levels, int shift, double? reference)
    {
        groupUpdating = true;
        try
        {
            if (levels.Keys.Any(row => row.Offset != 0))
            {
                double start = reference ?? levels.Average(pair => pair.Value - pair.Key.Offset);
                calibrationBase = Math.Clamp(start + shift, 0, 100);
                foreach (var row in levels.Keys)
                    row.SetTarget(Math.Clamp((int)Math.Round(calibrationBase.Value + row.Offset), 0, 100));
            }
            else
            {
                foreach (var (row, start) in levels)
                    row.SetTarget(Math.Clamp(start + shift, 0, 100));
            }
        }
        finally
        {
            groupUpdating = false;
        }
    }

    private void ResetMaster()
    {
        groupStart = null;
        gestureCalibrationBase = null;
        masterKeyHeld = false;
        master.Value = 0;
        offsetLabel.ForeColor = Theme.Muted;
        offsetLabel.Text = Texts.Get("Drag to adjust together", "Przeciągnij, aby zmienić jasność");
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData != Keys.Escape)
            return base.ProcessCmdKey(ref msg, keyData);

        if (guideVisible)
            ShowShortcutGuide(false);
        else
            Hide();
        return true;
    }
}

internal sealed class BrightnessRow : PopupCard
{
    private readonly PopupSlider slider;
    private readonly Label name;
    private readonly Label valueLabel;
    private readonly TableLayoutPanel heading;
    private PopupButton? options;
    public Display Display { get; }
    public int Confirmed { get; private set; }
    public int Target => slider.Value;
    public string DisplayName => name.Text;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int Offset { get; set; }
    public event Action<BrightnessRow, int>? BrightnessChanged;
    public event Action? TargetChanged;

    public BrightnessRow(Display display) : base(PopupAppearance.Current.Card)
    {
        Display = display;
        Confirmed = Math.Clamp(display.Brightness, 0, 100);
        var theme = PopupAppearance.Current;
        BackColor = theme.Background;
        Height = 82;
        Dock = DockStyle.Top;
        Margin = new Padding(0, 0, 0, 8);
        Padding = new Padding(14, 7, 15, 7);

        var card = new TableLayoutPanel
        {
            ColumnCount = 1,
            RowCount = 2,
            Dock = DockStyle.Fill,
            BackColor = theme.Card, Margin = Padding.Empty
        };
        card.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        card.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        Controls.Add(card);
        name = new Label
        {
            Text = display.Name,
            Font = PopupAppearance.LabelFont,
            AutoEllipsis = true,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = Padding.Empty
        };
        heading = new TableLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, ColumnCount = 4, RowCount = 1 };
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 34));
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, display.SetBrightness is null ? 0 : 54));
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 0));
        card.Controls.Add(heading, 0, 0);
        heading.Controls.Add(new PopupIcon(PopupGlyph.Display, theme.Accent)
        {
            Dock = DockStyle.Fill, TabStop = false, Margin = new Padding(0, 0, 10, 0)
        }, 0, 0);
        heading.Controls.Add(name, 1, 0);

        valueLabel = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleRight,
            Margin = Padding.Empty,
            Font = PopupAppearance.ValueFont,
            ForeColor = theme.Accent,
            Text = $"{Confirmed}%",
            UseMnemonic = false
        };
        slider = new PopupSlider
        {
            Minimum = 0,
            Maximum = 100,
            Value = Confirmed,
            TickStyle = TickStyle.None,
            SmallChange = 5,
            LargeChange = 10,
            AutoSize = false,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            AccessibleName = Texts.Get("Brightness", "Jasność") + " — " + display.Name
        };
        if (display.SetBrightness is null)
        {
            var unavailable = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = theme.Muted,
                Margin = Padding.Empty,
                Text = Texts.Get("Brightness control unavailable", "Regulacja jasności niedostępna")
            };
            card.Controls.Add(unavailable, 0, 1);
            slider.Dispose();
            valueLabel.Dispose();
            return;
        }

        heading.Controls.Add(valueLabel, 2, 0);
        card.Controls.Add(slider, 0, 1);
        slider.Scroll += (_, _) =>
        {
            valueLabel.Text = $"{slider.Value}%";
            BrightnessChanged?.Invoke(this, slider.Value);
            TargetChanged?.Invoke();
        };
    }

    public void SetName(string alias)
    {
        name.Text = alias.Length == 0 ? Display.Name : alias;
        if (!slider.IsDisposed)
            slider.AccessibleName = Texts.Get("Brightness", "Jasność") + " — " + name.Text;
    }

    public void EnableOptions(Action? rename, Action? calibrate)
    {
        var menu = new PopupMenu();
        if (rename is not null)
            menu.Items.Add(Texts.Get("Rename monitor…", "Zmień nazwę monitora…")).Click += (_, _) => rename();
        if (calibrate is not null)
            menu.Items.Add(Texts.Get("Brightness calibration…", "Kalibracja jasności…")).Click += (_, _) => calibrate();
        ContextMenuStrip = menu;
        name.ContextMenuStrip = menu;
        if (!slider.IsDisposed)
            slider.ContextMenuStrip = menu;
        options = new PopupButton(PopupGlyph.More, iconOnly: true, inset: true)
        {
            Text = Texts.Get("Settings", "Ustawienia"),
            Dock = DockStyle.Fill, Margin = new Padding(LogicalToDeviceUnits(4), 0, 0, 0),
            BackColor = PopupAppearance.Current.Card
        };
        heading.ColumnStyles[3].Width = LogicalToDeviceUnits(32);
        heading.Controls.Add(options, 3, 0);
        options.Click += (_, _) => menu.Show(options, new Point(options.Width, options.Height), ToolStripDropDownDirection.BelowLeft);
        Disposed += (_, _) => menu.Dispose();
    }

    public void FitToDpi()
    {
        heading.ColumnStyles[0].Width = LogicalToDeviceUnits(31);
        heading.ColumnStyles[2].Width = slider.IsDisposed ? 0 : LogicalToDeviceUnits(65);
        heading.ColumnStyles[3].Width = options is null ? 0 : LogicalToDeviceUnits(31);
        Padding = new Padding(LogicalToDeviceUnits(13), LogicalToDeviceUnits(6),
            LogicalToDeviceUnits(13), LogicalToDeviceUnits(8));
    }

    public void SetTarget(int value)
    {
        if (slider.Value == value)
            return;

        slider.Value = value;
        valueLabel.Text = $"{value}%";
        BrightnessChanged?.Invoke(this, value);
        TargetChanged?.Invoke();
    }

    public void ShowResult(int? brightness, bool pending)
    {
        if (brightness is int actual)
            Confirmed = actual;
        if (pending)
            return;

        slider.Value = Confirmed;
        valueLabel.Text = $"{Confirmed}%";
        TargetChanged?.Invoke();
    }
}
