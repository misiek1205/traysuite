namespace Dimmli;

internal sealed class BrightnessPanel : Form
{
    private static readonly PopupAppearance Theme = PopupAppearance.Current;
    private readonly TableLayoutPanel layout = new();
    private readonly Panel scroller = new();
    private readonly TableLayoutPanel body = new();
    private readonly TableLayoutPanel rows = new();
    private readonly TableLayoutPanel overview = new();
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
    private bool masterKeyHeld;
    private bool overviewVisible;
    private bool guideVisible;
    private Rectangle workingArea;
    private Point anchor;
    private const int HeaderHeight = 77;
    private const int OverviewHeight = 214;
    private const int RowHeight = 87;
    private const int RowGap = 9;
    private const int FooterHeight = 55;

    public BrightnessPanel(Action refresh, Action<Profile> apply, Action<Profile> save, Action? toggleShortcuts = null)
    {
        Text = "Dimmli";
        ShowInTaskbar = false;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96, 96);
        TopMost = true;
        KeyPreview = true;
        Font = PopupAppearance.CaptionFont;
        BackColor = Theme.Border;
        ForeColor = Theme.Text;
        Padding = new Padding(1);

        layout.Dock = DockStyle.Fill;
        layout.BackColor = Theme.Background;
        layout.ColumnCount = 1;
        layout.RowCount = 3;
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, HeaderHeight));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, FooterHeight));
        Controls.Add(layout);

        var header = new TableLayoutPanel
        {
            ColumnCount = 3, RowCount = 2, Dock = DockStyle.Fill,
            Padding = new Padding(20, 12, 20, 9), Margin = Padding.Empty
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 56));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 43));
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 64));
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 36));
        var tips = new ToolTip();
        using var resource = typeof(BrightnessPanel).Assembly.GetManifestResourceStream("Dimmli.Sun.ico")!;
        using var logo = new Icon(resource, 48, 48);
        var bitmap = logo.ToBitmap();
        Disposed += (_, _) => { bitmap.Dispose(); tips.Dispose(); };
        var mark = new PictureBox
        {
            Image = bitmap, Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom,
            Margin = new Padding(0, 2, 12, 2)
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
        var masterLayout = new TableLayoutPanel
        {
            ColumnCount = 2, RowCount = 3, Dock = DockStyle.Fill, BackColor = Color.Transparent,
            Margin = Padding.Empty
        };
        masterLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        masterLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        masterLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 39));
        masterLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 24));
        masterLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 37));
        masterLayout.Controls.Add(CreateLabel(allDisplaysLabel, Theme.Text, PopupAppearance.ValueFont), 0, 0);

        offsetLabel.Dock = DockStyle.Fill;
        offsetLabel.ForeColor = Theme.Accent;
        offsetLabel.Font = PopupAppearance.LabelFont;
        offsetLabel.Margin = Padding.Empty;
        offsetLabel.TextAlign = ContentAlignment.MiddleLeft;
        masterLayout.Controls.Add(offsetLabel, 0, 1);

        master.Dock = DockStyle.Fill;
        master.Margin = Padding.Empty;
        master.AutoSize = false;
        master.Minimum = -100;
        master.Maximum = 100;
        master.SmallChange = 1;
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
            ColumnCount = 2, RowCount = 2, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 11)
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
            var pair = new TableLayoutPanel
            {
                ColumnCount = 2, RowCount = 1, Dock = DockStyle.Fill,
                Margin = column == 0 ? new Padding(0, 0, 5, 0) : new Padding(5, 0, 0, 0)
            };
            pair.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            pair.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48));
            var activate = new PopupButton(glyph, leftAligned: true)
            {
                Text = label, ForeColor = color, Font = PopupAppearance.LabelFont,
                Dock = DockStyle.Fill, Margin = new Padding(0, 0, 5, 0)
            };
            var store = new PopupButton(PopupGlyph.Save, iconOnly: true)
            {
                Text = saveLabel, ForeColor = Theme.Muted, Dock = DockStyle.Fill, Margin = Padding.Empty
            };
            tips.SetToolTip(store, saveLabel);
            activate.Click += (_, _) => apply(profile);
            store.Click += (_, _) => save(profile);
            pair.Controls.Add(activate, 0, 0);
            pair.Controls.Add(store, 1, 0);
            profiles.Controls.Add(pair, column, 1);
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

        var footer = new TableLayoutPanel
        {
            ColumnCount = 2, Dock = DockStyle.Fill, Margin = Padding.Empty,
            Padding = new Padding(20, 6, 20, 10)
        };
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

        DpiChanged += (_, _) => BeginInvoke(ResizeAndPlace);
    }

    private static Label CreateLabel(string text, Color color, Font font,
        ContentAlignment align = ContentAlignment.MiddleLeft) => new()
    {
        Text = text, ForeColor = color, Font = font, TextAlign = align, Dock = DockStyle.Fill,
        Margin = Padding.Empty, AutoEllipsis = true
    };

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

    protected override CreateParams CreateParams
    {
        get
        {
            const int DropShadow = 0x00020000;
            var parameters = base.CreateParams;
            parameters.ClassStyle |= DropShadow;
            return parameters;
        }
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        if (Width <= 0 || Height <= 0)
            return;

        using var outline = PopupAppearance.Rounded(new RectangleF(0, 0, Width, Height), LogicalToDeviceUnits(14));
        Region = new Region(outline);
    }

    private void ResizeAndPlace()
    {
        if (workingArea.IsEmpty)
            return;

        int S(int units) => LogicalToDeviceUnits(units);
        int gap = S(8);
        int width = Math.Min(S(468), Math.Max(1, workingArea.Width - 2 * gap));
        int overviewHeight = overviewVisible ? OverviewHeight : 0;
        int bodyHeight = guideVisible ? 320 : overviewHeight + Math.Max(1, rows.RowCount) * (RowHeight + RowGap);
        int height = Math.Min(S(2 + HeaderHeight + FooterHeight + bodyHeight),
            Math.Max(1, workingArea.Height - 2 * gap));
        layout.RowStyles[0].Height = S(HeaderHeight);
        body.RowStyles[0].Height = S(overviewHeight);
        layout.RowStyles[2].Height = S(FooterHeight);
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
            row.TargetChanged += UpdateMeter;
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
        UpdateMeter();
        int controllable = ControllableRows.Count();
        overviewVisible = controllable > 0;
        overview.Visible = overviewVisible;
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
        ResizeAndPlace();
    }

    public void ShiftBy(int delta)
    {
        ResetMaster();
        foreach (var row in ControllableRows)
            row.SetTarget(Math.Clamp(row.Target + delta, 0, 100));
    }

    private void UpdateMeter()
    {
        meter.UpdateLevel((int)Math.Round(ControllableRows.Select(row => row.Target).DefaultIfEmpty().Average()));
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
    private readonly Label valueLabel;
    public Display Display { get; }
    public int Confirmed { get; private set; }
    public int Target => slider.Value;
    public event Action<BrightnessRow, int>? BrightnessChanged;
    public event Action? TargetChanged;

    public BrightnessRow(Display display) : base(PopupAppearance.Current.Card)
    {
        Display = display;
        Confirmed = Math.Clamp(display.Brightness, 0, 100);
        var theme = PopupAppearance.Current;
        BackColor = theme.Background;
        Height = 87;
        Dock = DockStyle.Top;
        Margin = new Padding(0, 0, 0, 9);
        Padding = new Padding(14, 7, 15, 7);

        var card = new TableLayoutPanel
        {
            ColumnCount = 3,
            RowCount = 2,
            Dock = DockStyle.Fill,
            BackColor = theme.Card
        };
        card.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 34));
        card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        card.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 65));
        card.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        card.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        Controls.Add(card);
        var name = new Label
        {
            Text = display.Name,
            Font = PopupAppearance.LabelFont,
            AutoEllipsis = true,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = Padding.Empty
        };
        card.Controls.Add(new PopupIcon(PopupGlyph.Display, theme.Accent)
        {
            Dock = DockStyle.Fill, TabStop = false, Margin = new Padding(0, 0, 10, 0)
        }, 0, 0);
        card.Controls.Add(name, 1, 0);

        valueLabel = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleRight,
            Margin = Padding.Empty,
            Font = PopupAppearance.ValueFont,
            ForeColor = theme.Accent,
            Text = $"{Confirmed}%"
        };
        slider = new PopupSlider
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
                ForeColor = theme.Muted,
                Margin = Padding.Empty,
                Text = Texts.Get("Brightness control unavailable", "Regulacja jasności niedostępna")
            };
            card.Controls.Add(unavailable, 0, 1);
            card.SetColumnSpan(unavailable, 3);
            slider.Dispose();
            valueLabel.Dispose();
            return;
        }

        card.Controls.Add(valueLabel, 2, 0);
        card.Controls.Add(slider, 0, 1);
        card.SetColumnSpan(slider, 3);
        slider.Scroll += (_, _) =>
        {
            valueLabel.Text = $"{slider.Value}%";
            BrightnessChanged?.Invoke(this, slider.Value);
            TargetChanged?.Invoke();
        };
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
